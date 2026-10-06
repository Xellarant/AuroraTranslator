using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Aurora.Content.Contracts;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

internal static class LocalApprovalImportTests
{
    private const string Target = "<element id='ID_APPROVAL_TARGET' name='Original target' type='Class Feature' source='Test'><description>Original mechanics</description></element>";
    private const string Companion = "<element id='ID_APPROVAL_COMPANION' name='Original companion' type='Class Feature' source='Test'/>";
    private const string Addition = "<element id='ID_APPROVAL_ADD' name='Local addition' type='Feat' source='Local'><description>Local mechanics</description></element>";
    private const string Baseline = "<elements>" + Target + Companion + "</elements>";
    private static string Protected => Baseline.Replace("Original target", "Protected target");
    private static readonly XNamespace Ns = LocalCorrectionDocument.Namespace;

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal string Local => Path.Combine(Root, "user", "local", "approved.xml");
        internal string Source => Path.Combine(Root, "core", "features.xml");
        internal string Key { get; }
        internal Fixture(bool addition = false)
        {
            Key = addition ? "add" : "fix";
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Local)!);
            File.WriteAllText(Source, Baseline);
            File.WriteAllText(Local, LocalCorrectionDocument.Create(
                addition ? Baseline.Replace("</elements>", Addition + "</elements>") : Protected,
                Baseline, "core/features.xml",
                [new(Key, addition ? "add" : "replace", addition ? "ID_APPROVAL_ADD" : "ID_APPROVAL_TARGET", null,
                    addition ? null : LocalCorrectionDocument.Fingerprint(XElement.Parse(Target)))]));
        }
        internal void Approve()
        {
            var approved = LocalCorrectionDocument.ApproveLocal(File.ReadAllText(Local), File.ReadAllText(Source), [Key]);
            File.WriteAllText(Local, approved.LocalXml);
        }
        internal void Import() => ContentImport.ImportAsync(Root, Database).GetAwaiter().GetResult();
        internal SqliteConnection Open()
        {
            var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open();
            return connection;
        }
        internal string Query(string sql)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "";
        }
        internal string State => Query("SELECT state FROM local_corrections");
        internal string Status => ContentDatabaseReader.ReadLocalCorrections(Database).Single().Status;
        internal string[] Reviews => JsonSerializer.Deserialize<string[]>(Query("SELECT review_details FROM local_override_files"))!;
        internal PreparedCatalogProjection Project(bool runtime, Func<PreparedCatalogSource, bool>? include = null)
        {
            using var connection = Open();
            var files = runtime ? RuntimeContentFiles.Read(connection, Root, [], includePrimary: true) : null;
            return PreparedCatalogReader.Read(connection, includeSource: include, runtimeFiles: files);
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
    private static string PersistedState(Fixture f) => (string)LocalCorrectionDocument.Parse(File.ReadAllText(f.Local))
        .Root!.Element(Ns + "corrections")!.Element(Ns + "correction")!.Attribute("state")!;

    internal static void ApprovedAdditionKeepsLocalOwnership()
    {
        using var f = new Fixture(addition: true);
        f.Import();
        f.Approve();
        string localHash = Hash(f.Local);
        f.Import();
        Require(f.State == "approved-local" && f.Status == "approved-local" && f.Reviews.Length == 0,
            "Valid approval is mirrored as approved-local without a pending-review diagnostic.");
        Require(Hash(f.Local) == localHash && File.Exists(f.Local), "Approval remains a protected local file after import.");
        foreach (bool runtime in new[] { false, true })
        {
            var addition = f.Project(runtime).Elements.Single(e => e.AuroraId == "ID_APPROVAL_ADD");
            Require(addition.Source.FilePath == f.Local && addition.Source.PackageKind == "homebrew"
                && addition.Source.RelativePath.Replace('\\', '/') == "user/local/approved.xml",
                "An approved addition belongs to its local homebrew supplier in stored and runtime projections.");
            Require(!f.Project(runtime, source => source.PackageKind == "core").Elements.Any(e => e.AuroraId == "ID_APPROVAL_ADD"),
                "Filtering local content cannot expose an approved addition as authoritative core content.");
        }
        var detail = ContentCatalogReader.ReadDetail(f.Database, "ID_APPROVAL_ADD")!;
        Require(detail.Summary.Supplier?.PackageKind == "homebrew" && detail.Suppliers.Single().RelativePath.Replace('\\', '/') == "user/local/approved.xml",
            "The typed catalog reader reports the same local provenance.");
    }

    internal static void ApprovedReplacementStaysPinned()
    {
        using var f = new Fixture();
        f.Approve();
        string localHash = Hash(f.Local), sourceHash = Hash(f.Source);
        f.Import(); f.Import();
        Require(f.State == "approved-local" && f.Status == "approved-local" && f.Reviews.Length == 0,
            "A valid approved replacement needs no repeated review while both target declarations are unchanged.");
        Require(f.Query("SELECT name FROM elements WHERE aurora_id='ID_APPROVAL_TARGET'") == "Protected target",
            "Approving local intent keeps its replacement active.");
        Require(f.Query("SELECT baseline_xml FROM local_override_files") == Baseline
            && f.Query("SELECT original_fingerprint FROM local_corrections") == LocalCorrectionDocument.Fingerprint(XElement.Parse(Target)),
            "The mirrored original baseline and declaration fingerprint remain intact.");
        Require(Hash(f.Local) == localHash && Hash(f.Source) == sourceHash
            && Directory.GetFiles(Path.GetDirectoryName(f.Local)!, "*.retired-*").Length == 0,
            "Approved-local does not authorize rewriting authoritative XML or retiring the local correction.");
        foreach (bool runtime in new[] { false, true })
            Require(XElement.Parse(f.Project(runtime).Elements.Single(e => e.AuroraId == "ID_APPROVAL_TARGET").Xml)
                .Attribute("name")?.Value == "Protected target", "Stored and runtime projections both apply approved replacements.");
    }

    internal static void ChangedTargetsReturnToReview()
    {
        foreach (bool changeLocal in new[] { true, false })
        {
            using var f = new Fixture();
            f.Approve(); f.Import();
            if (changeLocal)
                File.WriteAllText(f.Local, File.ReadAllText(f.Local).Replace("Protected target", "Revised local target"));
            else
                File.WriteAllText(f.Source, Baseline.Replace("Original target", "Revised upstream target"));
            string localHash = Hash(f.Local), sourceHash = Hash(f.Source);
            Require(ContentDatabaseReader.IsStale([f.Root], f.Database), "A changed target requests an import.");
            f.Import();
            Require(f.State == "review-pending" && f.Status == "review-required" && f.Reviews.Length > 0,
                "Changing either approved target normalizes the mirrored state back to review-pending.");
            Require(PersistedState(f) == "approved-local" && Hash(f.Local) == localHash && Hash(f.Source) == sourceHash
                && f.Query("SELECT local_xml FROM local_override_files") == File.ReadAllText(f.Local),
                "Import records normalized state without silently rewriting the stored approval stamp or raw XML mirror.");
            string expected = changeLocal ? "Revised local target" : "Protected target";
            Require(f.Query("SELECT name FROM elements WHERE aurora_id='ID_APPROVAL_TARGET'") == expected,
                "Invalidated approval still preserves active local correction intent.");
            foreach (bool runtime in new[] { false, true })
                Require(XElement.Parse(f.Project(runtime).Elements.Single(e => e.AuroraId == "ID_APPROVAL_TARGET").Xml)
                    .Attribute("name")?.Value == expected, "Runtime and persisted content agree after approval invalidation.");
        }
    }

    internal static void UnrelatedEditsPreserveApproval()
    {
        using var f = new Fixture();
        f.Approve(); f.Import();
        File.WriteAllText(f.Source, Baseline.Replace("Original companion", "Updated companion"));
        File.AppendAllText(f.Local, "\n<!-- An unrelated authoring note. -->\n");
        string localHash = Hash(f.Local);
        f.Import();
        Require(f.State == "approved-local" && f.Status == "approved-local" && f.Reviews.Length == 0,
            "Unrelated companion and comment edits do not invalidate declaration-level approval.");
        Require(f.Query("SELECT name FROM elements WHERE aurora_id='ID_APPROVAL_COMPANION'") == "Updated companion"
            && Hash(f.Local) == localHash, "Unchanged local companions follow the updated source without rewriting the correction.");
        foreach (bool runtime in new[] { false, true })
            Require(XElement.Parse(f.Project(runtime).Elements.Single(e => e.AuroraId == "ID_APPROVAL_COMPANION").Xml)
                .Attribute("name")?.Value == "Updated companion", "Runtime also follows the unrelated authoritative update.");
    }

    internal static void MixedUnclassifiedChangesRequireReview()
    {
        foreach (string state in new[] { "approved-local", "accepted-upstream" })
        {
            using var f = new Fixture();
            if (state == "approved-local") f.Approve();
            else
            {
                var document = LocalCorrectionDocument.Parse(File.ReadAllText(f.Local));
                document.Root!.Element(Ns + "corrections")!.Element(Ns + "correction")!.SetAttributeValue("state", state);
                File.WriteAllText(f.Local, document.ToString(SaveOptions.DisableFormatting));
            }
            var local = LocalCorrectionDocument.Parse(File.ReadAllText(f.Local));
            local.Root!.Add(XElement.Parse(Addition));
            File.WriteAllText(f.Local, local.ToString(SaveOptions.DisableFormatting));
            f.Import();
            Require(f.State == state && f.Status == "review-required" && f.Reviews.Length > 0,
                "An approved or accepted correction cannot hide a separate unclassified local edit in the same file.");
            Require(File.Exists(f.Local), "Mixed files cannot retire while unclassified local work remains.");
        }
    }

    internal static void OldDatabaseRefreshAndExplicitRetirement()
    {
        using var f = new Fixture();
        f.Import();
        f.Query("UPDATE database_metadata SET data_version=18;");
        f.Approve();
        Require(ContentDatabaseReader.IsStale([f.Root], f.Database), "Approval and an older data contract require a refresh.");
        f.Import();
        Require(f.Query("SELECT data_version FROM database_metadata") == ContentDatabaseReader.CurrentDataVersion.ToString()
            && f.State == "approved-local" && f.Status == "approved-local",
            "Refreshing an existing data-18 database records the new approval state without a schema replacement.");

        File.WriteAllText(f.Source, Protected);
        f.Import();
        Require(File.Exists(f.Local) && f.Status == "review-required" && f.Reviews.Length > 0,
            "A matching installed authoritative file needs review; it does not authorize retirement of approved local intent.");
        LocalCorrectionDocument.AcceptUpstream(f.Local, Hash(f.Local), Hash(f.Source), [f.Key]);
        f.Import();
        Require(!File.Exists(f.Local) && f.Status == "retired"
            && Directory.GetFiles(Path.GetDirectoryName(f.Local)!, "*.retired-*").Length == 1,
            "Explicit upstream acceptance followed by successful import still permits recoverable retirement.");
        Require(f.Query("SELECT name FROM elements WHERE aurora_id='ID_APPROVAL_TARGET'") == "Protected target",
            "Retirement keeps the incorporated authoritative definition available.");
    }
}
