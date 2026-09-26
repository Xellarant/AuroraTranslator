using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

internal static class CorrectionActivationPolicyTests
{
    private const string Baseline = "<elements><element id='ID_POLICY_FIX' name='Original' type='Class Feature' source='Test'/></elements>";
    private static string Correction(string state = "review-pending", string? group = null) =>
        LocalCorrectionDocument.Create(Baseline.Replace("Original", "Protected"), Baseline, "core/base.xml",
            [new("protected-name", "replace", "ID_POLICY_FIX", null, null, state, group)]);

    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal Workspace(bool existing)
        {
            Write("core/base.xml", Baseline);
            Write("user/local/fix.xml", Correction());
            if (existing) Import();
        }
        internal void Write(string relative, string xml)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }
        internal ContentImportResult Import() => ContentImport.ImportAsync(Root, Database,
            skipUnusableContent: true).GetAwaiter().GetResult();
        internal long Count(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void RefuseWithoutActivation(Workspace w, string expected)
    {
        string? databaseHash = File.Exists(w.Database) ? Hash(w.Database) : null;
        var inputHashes = Directory.GetFiles(w.Root, "*.xml", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        bool rejected = false;
        try { w.Import(); }
        catch (InvalidDataException error)
        {
            Require(error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase),
                $"Expected '{expected}' in correction failure: {error}");
            rejected = true;
        }
        Require(rejected, "Invalid correction intent must refuse activation even when ordinary unusable content can be skipped.");
        Require(databaseHash == null ? !File.Exists(w.Database) : Hash(w.Database) == databaseHash,
            "Refusal must preserve the existing database or leave a first installation without an activated database.");
        Require(inputHashes.All(input => File.Exists(input.Key) && Hash(input.Key) == input.Value),
            "Failed correction preparation must not rewrite or retire the user's files.");
    }

    internal static void InvalidMetadataStopsActivation()
    {
        var invalid = new Func<string, string>[]
        {
            xml => xml.Replace("version=\"1\"", "version=\"99\""),
            xml => xml.Replace("operation=\"replace\"", "operation=\"unsupported\""),
            xml => xml.Replace("urn:aurora-lights:corrections:1", "urn:aurora-lights:corrections:99"),
            xml => xml.Replace("source-path=\"core/base.xml\"", "source-path=\"core/missing.xml\"")
        };
        foreach (bool existing in new[] { false, true })
            foreach (var invalidate in invalid)
            {
                using var w = new Workspace(existing);
                w.Write("user/local/fix.xml", invalidate(Correction()));
                RefuseWithoutActivation(w, "Correction preparation failed");
            }
    }

    internal static void OverlapStopsActivation()
    {
        foreach (bool existing in new[] { false, true })
        {
            using var w = new Workspace(existing);
            w.Write("user/local/other-fix.xml", Correction());
            RefuseWithoutActivation(w, "Multiple managed files target");
        }
    }

    internal static void PartialGroupStopsActivation()
    {
        foreach (bool existing in new[] { false, true })
        {
            using var w = new Workspace(existing);
            string second = Baseline.Replace("ID_POLICY_FIX", "ID_POLICY_SECOND");
            w.Write("core/second.xml", second);
            w.Write("user/local/fix.xml", Correction(group: "linked-fixes"));
            w.Write("user/local/second-fix.xml", LocalCorrectionDocument.Create(second, second, "core/second.xml",
                [new("second", "replace", "ID_POLICY_SECOND", null, null, "accepted-upstream", "linked-fixes")]));
            RefuseWithoutActivation(w, "linked-fixes");
        }
    }

    internal static void AmbiguousXmlStopsActivation()
    {
        foreach (bool existing in new[] { false, true })
            foreach (string malformed in new[]
            {
                "<elements><element name='truncated'",
                Correction()[..^8]
            })
            {
                using var w = new Workspace(existing);
                w.Write("user/local/fix.xml", malformed);
                RefuseWithoutActivation(w, "Cannot determine correction intent");
            }
    }

    internal static void OrdinaryLocalContentUsesUnreadablePolicy()
    {
        foreach (bool existing in new[] { false, true })
        {
            using var w = new Workspace(existing);
            w.Write("user/local/missing-id.xml", "<elements><element name='No ID' type='Feat' source='Local'/></elements>");
            w.Write("user/local/unsupported-root.xml", "<elements xmlns='urn:other'><element id='ID_IGNORED' name='Ignored' type='Feat' source='Local'/></elements>");
            w.Write("user/local/new-content.xml", "<elements xmlns:al='urn:aurora-lights:corrections:1'><element id='ID_LOCAL_CONTENT' name='Local addition' type='Feat' source='Local'/></elements>");
            var result = w.Import();
            Require(result.Skipped.Count == 2 && result.Skipped.All(skip => skip.Kind == "unreadable"),
                "Unmarked invalid Aurora content must use the unreadable policy, not be labeled a correction.");
            Require(w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_LOCAL_CONTENT'") == 1,
                "Valid ordinary local content must remain available.");
            Require(w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_POLICY_FIX' AND name='Protected' AND declaration_status='effective'") == 1 &&
                w.Count("SELECT COUNT(*) FROM local_corrections WHERE state='review-pending'") == 1,
                "Skipping ordinary malformed content must retain the separate valid protected correction.");
        }
    }

    internal static void MisplacedMetadataStopsActivation()
    {
        var misplaced = new[]
        {
            Correction(),
            Correction().Replace("<elements>", "<elements ignore='true'>"),
            Correction().Replace("<elements>", "<elements xmlns='urn:unsupported-root'>"),
            "<elements xmlns:al='urn:aurora-lights:corrections:1'><al:corrections"
        };
        foreach (bool existing in new[] { false, true })
            foreach (string xml in misplaced)
            {
                using var w = new Workspace(existing);
                w.Write("user/misplaced.xml", xml);
                RefuseWithoutActivation(w, "Correction metadata in");
            }
    }

    internal static void CorrectionTargetsCannotBeQuarantined()
    {
        foreach (bool existing in new[] { false, true })
            foreach (bool rename in new[] { false, true })
            {
                using var w = new Workspace(existing);
                string target = rename ? "ID_POLICY_RENAMED" : "ID_POLICY_FIX";
                if (rename)
                    w.Write("user/local/fix.xml", LocalCorrectionDocument.Create(
                        Baseline.Replace("ID_POLICY_FIX", target), Baseline, "core/base.xml",
                        [new("rename", "rename", "ID_POLICY_FIX", target, null)]));
                w.Write("core/conflicting.xml", Baseline.Replace("ID_POLICY_FIX", target).Replace("Original", "Conflicting"));
                RefuseWithoutActivation(w, "explicit correction targets this identity");
            }
    }

    internal static void UnresolvedCompanionPreventsRetirement()
    {
        using var w = new Workspace(existing: false);
        const string companion = "<element id='ID_POLICY_COMPANION' name='Companion' type='Class Feature' source='Test'/>";
        string baseline = Baseline.Replace("</elements>", companion + "</elements>");
        string published = baseline.Replace("Original", "Protected");
        w.Write("core/base.xml", published);
        w.Write("user/local/fix.xml", LocalCorrectionDocument.Create(published, baseline, "core/base.xml",
            [new("protected-name", "replace", "ID_POLICY_FIX", null, null, "accepted-upstream")]));
        w.Write("core/other.xml", "<elements>" + companion.Replace("name='Companion'", "name='Conflicting'") + "</elements>");
        string local = Path.Combine(w.Root, "user/local/fix.xml");
        string localHash = Hash(local);

        var result = w.Import();
        Require(result.Skipped.Count == 1 && result.Skipped[0].Kind == "definition-collision" &&
            ContentDatabaseReader.ReadUnavailableIds(w.Database).Count == 0,
            "A companion conflict has a reported provisional definition without invalidating the clean accepted correction target.");
        Require(w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_POLICY_FIX' AND name='Protected' AND declaration_status='effective'") == 1 &&
            w.Count("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_POLICY_COMPANION' AND declaration_status='effective'") == 1,
            "The accepted correction must stay effective while the conflicting companion is provisional.");
        Require(File.Exists(local) && Hash(local) == localHash &&
            Directory.GetFiles(Path.GetDirectoryName(local)!, "*.retired-*").Length == 0 &&
            w.Count("SELECT COUNT(*) FROM local_override_files WHERE status='retired'") == 0,
            "A matching accepted local file must remain recoverable and active until every companion collision is resolved.");

        w.Write("core/other.xml", "<elements>" + companion + "</elements>");
        w.Import();
        Require(ContentDatabaseReader.ReadUnavailableIds(w.Database).Count == 0 && !File.Exists(local) &&
            Directory.GetFiles(Path.GetDirectoryName(local)!, "*.retired-*").Length == 1,
            "Repairing the companion conflict must restore normal retirement of a fully redundant accepted correction file.");
    }
}
