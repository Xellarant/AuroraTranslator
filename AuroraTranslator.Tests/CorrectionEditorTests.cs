using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;

internal static class CorrectionEditorTests
{
    private const string Spell = "<element id='ID_EDIT_SPELL' name='Original spell' type='Spell' source='Test'><description>Original description</description><supports>Wizard</supports><setters><set name='level'>1</set></setters></element>";
    private const string Companion = "<element id='ID_EDIT_COMPANION' name='Original companion' type='Class Feature' source='Test'/>";
    private const string Baseline = "<elements>" + Spell + Companion + "</elements>";
    private static string OriginalFingerprint => LocalCorrectionDocument.Fingerprint(XElement.Parse(Spell));
    private static string AmendedSpell => Spell.Replace("Original spell", "Amended spell")
        .Replace("Original description", "Amended description").Replace(">1</set>", ">3</set>");

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal string Local => Path.Combine(Root, "user", "local", "correction.xml");
        internal string Source => Path.Combine(Root, "core", "spells.xml");
        internal Fixture(string? group = null)
        {
            Write("core/spells.xml", Baseline.Replace("Original companion", "Latest companion"));
            Write("user/local/correction.xml", LocalCorrectionDocument.Create(
                Baseline.Replace("Original spell", "Protected spell"), Baseline, "core/spells.xml",
                [new("spell-fix", "replace", "ID_EDIT_SPELL", null, OriginalFingerprint, Group: group, Reason: "Retain the intended spell") ]));
        }
        internal string Write(string relative, string xml)
        {
            string path = Path.GetFullPath(Path.Combine(Root, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
            return path;
        }
        internal void Import() => ContentImport.ImportAsync(Root, Database).GetAwaiter().GetResult();
        internal string Query(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        internal string Peer(string state)
        {
            string peer = "<elements><element id='ID_EDIT_PEER' name='Peer' type='Class Feature' source='Test'/></elements>";
            Write("core/peer.xml", peer);
            return Write("user/local/peer.xml", LocalCorrectionDocument.Create(peer, peer, "core/peer.xml",
                [new("peer-fix", "replace", "ID_EDIT_PEER", null, null, state, "shared-repair")]));
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static string Hash(string path)
    {
        SqliteConnection.ClearAllPools();
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
    private static Dictionary<string, string> Replacement(string? xml = null) => new(StringComparer.Ordinal)
    { ["spell-fix"] = xml ?? AmendedSpell };
    private static void Refuse(Action action, string expected)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            Require(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase),
                $"Expected '{expected}' in diagnostic: {error}");
            return;
        }
        throw new Exception("Expected correction editor to refuse: " + expected);
    }
    private static void NoTemporaryFiles(Fixture f) => Require(
        Directory.GetFiles(Path.GetDirectoryName(f.Local)!, "*.edit-*").Length == 0,
        "No temporary correction edit should remain after save or refusal.");

    internal static void PreviewSaveAndImportBoundary()
    {
        using var f = new Fixture();
        f.Import();
        string localHash = Hash(f.Local), sourceHash = Hash(f.Source), databaseHash = Hash(f.Database);
        Require(!ContentDatabaseReader.IsStale([f.Root], f.Database), "Initial fixture is a current imported correction.");
        var reviewed = ContentCorrectionEditor.Read(f.Local);
        Require(reviewed.LocalHash == localHash && reviewed.UpstreamHash == sourceHash
            && reviewed.FilePath == f.Local && reviewed.SourcePath == f.Source,
            "Review captures the actual paths and byte fingerprints.");
        var preview = ContentCorrectionEditor.PreviewReplacement(reviewed, Replacement());
        Require(Hash(f.Local) == localHash && Hash(f.Source) == sourceHash && Hash(f.Database) == databaseHash,
            "Reading and previewing must not write XML or the database.");
        Require(preview.BaselineXml == Baseline && preview.Corrections.Single().OriginalFingerprint == OriginalFingerprint,
            "Amending a correction preserves its embedded original and declaration fingerprint.");
        Require(preview.Corrections.Single().State == "review-pending" && !preview.CanRetire
            && preview.Corrections.Single().Reason == "Retain the intended spell",
            "The amended correction remains protected with its review context.");
        var effective = LocalCorrectionDocument.Parse(preview.EffectiveXml);
        Require((string?)effective.Root!.Elements("element").Single(e => (string?)e.Attribute("id") == "ID_EDIT_SPELL").Attribute("name") == "Amended spell"
            && (string?)effective.Root.Elements("element").Single(e => (string?)e.Attribute("id") == "ID_EDIT_COMPANION").Attribute("name") == "Latest companion",
            "Preview applies the amendment while unchanged companions follow authoritative updates.");

        var saved = ContentCorrectionEditor.Replace(reviewed, Replacement());
        Require(File.ReadAllText(f.Local) == saved.LocalXml && saved.LocalXml == preview.LocalXml,
            "Save writes exactly the validated replacement preview.");
        Require(saved.BaselineXml == Baseline && saved.Corrections.Single().OriginalFingerprint == OriginalFingerprint,
            "Saving preserves the baseline and original fingerprint.");
        Require(Hash(f.Source) == sourceHash && Hash(f.Database) == databaseHash,
            "Saving local XML must leave authoritative XML and the database byte-for-byte unchanged.");
        Require(ContentDatabaseReader.IsStale([f.Root], f.Database)
            && f.Query("SELECT name FROM elements WHERE aurora_id='ID_EDIT_SPELL'") == "Protected spell",
            "Saving requests a later import; existing records still describe the old correction.");
        NoTemporaryFiles(f);
        f.Import();
        Require(f.Query("SELECT name FROM elements WHERE aurora_id='ID_EDIT_SPELL'") == "Amended spell"
            && f.Query("SELECT spell_level FROM spells s JOIN elements e ON e.element_id=s.element_id WHERE e.aurora_id='ID_EDIT_SPELL'") == "3",
            "Explicit import updates SQLite with the new typed definition.");
        Require(f.Query("SELECT baseline_xml FROM local_override_files") == Baseline
            && f.Query("SELECT state FROM local_corrections") == "review-pending"
            && File.Exists(f.Local) && !ContentDatabaseReader.IsStale([f.Root], f.Database),
            "Import mirrors the original baseline and pending intent without retiring the correction.");
    }

    internal static void ChangedInputsRejectSave()
    {
        foreach (bool changeLocal in new[] { true, false })
        {
            using var f = new Fixture();
            f.Import();
            var review = ContentCorrectionEditor.Read(f.Local);
            string path = changeLocal ? f.Local : f.Source;
            File.AppendAllText(path, "\n<!-- another edit -->\n");
            string local = Hash(f.Local), upstream = Hash(f.Source), database = Hash(f.Database);
            Refuse(() => ContentCorrectionEditor.PreviewReplacement(review, Replacement()), "changed since review");
            Refuse(() => ContentCorrectionEditor.Replace(review, Replacement()), "changed since review");
            Require(Hash(f.Local) == local && Hash(f.Source) == upstream && Hash(f.Database) == database,
                "A stale preview or save must preserve intervening edits and the working database.");
            NoTemporaryFiles(f);
        }
    }

    internal static void PublicSnapshotsCannotForgeInputs()
    {
        using var f = new Fixture();
        var review = ContentCorrectionEditor.Read(f.Local);
        var forged = review with
        {
            LocalXml = review.LocalXml.Replace("Original companion", "Injected companion"),
            UpstreamXml = review.UpstreamXml.Replace("Latest companion", "Injected authoritative companion")
        };
        var preview = ContentCorrectionEditor.PreviewReplacement(forged, Replacement());
        Require(preview.BaselineXml == Baseline && preview.UpstreamXml == review.UpstreamXml
            && !preview.LocalXml.Contains("Injected") && !preview.EffectiveXml.Contains("Injected"),
            "Public snapshot XML is untrusted; previews must recompute from the files identified by the reviewed hashes.");
        var fakeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(forged.LocalXml)));
        string local = Hash(f.Local), source = Hash(f.Source);
        Refuse(() => ContentCorrectionEditor.Replace(forged with { LocalHash = fakeHash }, Replacement()), "changed since review");
        Require(Hash(f.Local) == local && Hash(f.Source) == source, "A forged fingerprint cannot authorize saving different input XML.");
        var saved = ContentCorrectionEditor.Replace(forged, Replacement());
        Require(saved.BaselineXml == Baseline && saved.UpstreamXml == review.UpstreamXml
            && !File.ReadAllText(f.Local).Contains("Injected") && Hash(f.Source) == source,
            "Saving also recomputes from disk instead of trusting XML in the public snapshot.");
        NoTemporaryFiles(f);
    }

    internal static void TypedInvalidReplacementsDoNotWrite()
    {
        foreach (bool disabled in new[] { false, true })
        {
            using var f = new Fixture();
            f.Import();
            if (disabled)
            {
                var local = LocalCorrectionDocument.Parse(File.ReadAllText(f.Local));
                local.Root!.SetAttributeValue("ignore", "true");
                File.WriteAllText(f.Local, local.ToString(SaveOptions.DisableFormatting));
            }
            var reviewed = ContentCorrectionEditor.Read(f.Local);
            string localHash = Hash(f.Local), sourceHash = Hash(f.Source), databaseHash = Hash(f.Database);
            // Setter parsing deliberately retains unparseable raw values, so use a typed
            // boolean that the importer itself rejects instead of imposing a stricter editor policy.
            var invalid = Replacement(AmendedSpell.Replace("</element>", "<compendium display='perhaps'/></element>"));
            Refuse(() => ContentCorrectionEditor.PreviewReplacement(reviewed, invalid), "ID_EDIT_SPELL");
            Refuse(() => ContentCorrectionEditor.Replace(reviewed, invalid), "invalid definition");
            Require(Hash(f.Local) == localHash && Hash(f.Source) == sourceHash && Hash(f.Database) == databaseHash,
                "Typed invalid content must never be saved, including when a correction is disabled.");
            NoTemporaryFiles(f);
        }
    }

    internal static void ExternalCorrectionGroupsStayCoordinated()
    {
        using var f = new Fixture("shared-repair");
        string peer = f.Peer("accepted-upstream");
        var review = ContentCorrectionEditor.Read(f.Local);
        string localHash = Hash(f.Local), peerHash = Hash(peer), sourceHash = Hash(f.Source);
        Refuse(() => ContentCorrectionEditor.PreviewReplacement(review, Replacement()), "shared-repair");
        Refuse(() => ContentCorrectionEditor.Replace(review, Replacement()), "Coordinated edits across files");
        Require(Hash(f.Local) == localHash && Hash(peer) == peerHash && Hash(f.Source) == sourceHash,
            "An accepted group peer requires coordinated reopening; refusal must leave both correction files unchanged.");
        NoTemporaryFiles(f);

        f.Peer("review-pending");
        peerHash = Hash(peer);
        var preview = ContentCorrectionEditor.PreviewReplacement(review, Replacement());
        Require(preview.Corrections.Single().Group == "shared-repair", "A supported edit keeps the existing group identity.");
        var saved = ContentCorrectionEditor.Replace(review, Replacement());
        Require(saved.Corrections.Single().State == "review-pending" && Hash(peer) == peerHash && Hash(f.Source) == sourceHash,
            "An already pending external group permits a local amendment without rewriting its peer or source.");
        f.Import();
        Require(f.Query("SELECT COUNT(*) FROM local_corrections WHERE state='review-pending'") == "2"
            && f.Query("SELECT name FROM elements WHERE aurora_id='ID_EDIT_SPELL'") == "Amended spell",
            "A subsequent import accepts the coherent all-pending group and mirrors both corrections.");
        NoTemporaryFiles(f);
    }
}
