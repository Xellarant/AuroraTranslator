using AuroraTranslator;
using AuroraTranslator.Content;
using Builder.Data.Files;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Xml.Linq;

internal static class ContentPreparationTests
{
    private const string Baseline = "<elements><element id='ID_FIX' name='Old' type='Item' source='Test'><description>old</description></element><element id='ID_COMPANION' name='Companion' type='Item' source='Test'><description>old companion</description></element></elements>";
    private static string Fixed => Baseline.Replace("name='Old'", "name='Fixed'").Replace(">old</description>", ">corrected</description>");
    private static LocalCorrection Replacement(string state = "review-pending") => new("fix", "replace", "ID_FIX", null, null, state);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException) { Require(ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase), ex.ToString()); return; }
        throw new Exception("Expected rejection: " + message);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal string Origin => Path.Combine(Root, "core", "features.xml");
        internal string Local => Path.Combine(Root, "user", "local", "fix.xml");
        internal Workspace() { Write("core/features.xml", Baseline); }
        internal void Write(string relative, string xml) { string path = Path.Combine(Root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, xml); }
        internal void Mark(string state = "review-pending", string? local = null) => Write("user/local/fix.xml", LocalCorrectionDocument.Create(local ?? Fixed, Baseline, "core/features.xml", [Replacement(state)]));
        internal void Import(Action<string>? afterWrite = null, bool success = true) => LocalCorrectionSync.ImportAsync([Root], Database, (roots, candidate, _) =>
        {
            AuroraSqliteImporter.ImportFinalized(AuroraTranslator.Program.BuildAuroraImportCatalog(roots[0]), TestPaths.SchemaPath, candidate);
            afterWrite?.Invoke(candidate);
            return Task.FromResult(new CorrectionImportResult(success));
        }).GetAwaiter().GetResult();
        internal string Query(string sql) => QueryDatabase(Database, sql);
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }
    private static string QueryDatabase(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    internal static void Lifecycle()
    {
        using var w = new Workspace(); w.Mark();
        w.Write("core/features.xml", Baseline.Replace("old companion", "new companion").Replace(">old</description>", ">upstream change</description>"));
        string localHash = Hash(w.Local); w.Import();
        Require(w.Query("SELECT effective_xml FROM local_override_files").Contains("new companion"), "Companion did not follow upstream");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_FIX'") == "Fixed", "Local fix lost");
        Require(Hash(w.Local) == localHash, "Import rewrote local input");
        Require(w.Query("SELECT state FROM local_corrections") == "review-pending", "Protection lost");
        Require(w.Query("SELECT baseline_xml FROM local_override_files") == Baseline, "Baseline lost");
        w.Write("core/features.xml", Fixed); w.Import();
        Require(File.Exists(w.Local) && w.Query("SELECT review_details FROM local_override_files").Contains("incorporated"), "Disk match accepted automatically");
        File.Delete(w.Local); w.Write("core/features.xml", Baseline); w.Import();
        Require(w.Query("SELECT COUNT(*) FROM local_corrections") == "0", "Deleted mirror survived");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_FIX'") == "Old", "Base not restored");
    }

    internal static void Retirement()
    {
        using var w = new Workspace(); w.Mark(); w.Write("core/features.xml", Fixed);
        LocalCorrectionDocument.AcceptUpstream(w.Local, Hash(w.Local), Hash(w.Origin), ["fix"]);
        w.Import(success: false); Require(File.Exists(w.Local) && !File.Exists(w.Database), "Failed import retired or activated");
        w.Import(); Require(!File.Exists(w.Local), "Accepted redundant file not retired");
        Require(Directory.GetFiles(Path.GetDirectoryName(w.Local)!, "*.retired-*").Length == 1, "Retired XML not recoverable");
        Require(w.Query("SELECT status FROM local_override_files") == "retired", "Retirement status missing");
        w.Mark("accepted-upstream", Fixed.Replace("</elements>", "<element id='ID_MINE' name='Mine' type='Item' source='Local'/></elements>"));
        w.Import(); Require(File.Exists(w.Local), "Mixed local file retired");
        Require(w.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_MINE'") == "1", "Unique local content lost");
    }

    internal static void Conflicts()
    {
        using var w = new Workspace(); w.Import(); string hash = Hash(w.Database);
        w.Write("other.xml", Baseline.Replace("name='Old'", "name='Different'"));
        Reject(() => w.Import(), "duplicate-element-id"); Require(Hash(w.Database) == hash, "Conflict changed working DB");
        File.Delete(Path.Combine(w.Root, "other.xml")); w.Mark();
        File.WriteAllText(w.Local, File.ReadAllText(w.Local).Replace("version=\"1\"", "version=\"99\""));
        Reject(() => w.Import(), "metadata"); Require(Hash(w.Database) == hash, "Malformed correction changed DB");
    }

    internal static void Failures()
    {
        using var w = new Workspace(); w.Import(); string hash = Hash(w.Database); w.Mark();
        w.Import(success: false); Require(Hash(w.Database) == hash && File.Exists(w.Local), "Writer failure changed state");
        Reject(() => w.Import(_ => w.Write("late.xml", "<elements/>")), "changed during sync");
        Require(Hash(w.Database) == hash, "Race changed DB"); File.Delete(Path.Combine(w.Root, "late.xml"));
        Reject(() => w.Import(candidate => QueryDatabase(candidate, "DELETE FROM elements WHERE aurora_id='ID_FIX'; SELECT 1")), "missing");
        Require(Hash(w.Database) == hash, "Invalid candidate activated");
        Require(Directory.GetFiles(Path.GetDirectoryName(w.Database)!, ".aurora-candidate-*").Length == 0, "Candidate leaked");
    }

    internal static void Suppliers()
    {
        using var w = new Workspace(); w.Write("copy.xml", Baseline); w.Import();
        Require(w.Query("SELECT COUNT(*) FROM elements") == "2", "Identical definitions not consolidated");
        Require(w.Query("SELECT COUNT(*) FROM content_declaration_provenance") == "4", "Supplier provenance lost");
        string owner = w.Query("SELECT sf.relative_path FROM elements e JOIN source_files sf ON sf.source_file_id=e.source_file_id WHERE e.aurora_id='ID_FIX'");
        File.Delete(Path.Combine(w.Root, owner)); w.Import();
        Require(w.Query("SELECT COUNT(*) FROM elements") == "2", "Removing supplier removed surviving definitions");
        Require(w.Query("SELECT COUNT(*) FROM content_declaration_provenance") == "2", "Old suppliers retained");
        string surviving = Directory.GetFiles(w.Root, "*.xml", SearchOption.AllDirectories).Single();
        File.WriteAllText(surviving, Baseline.Replace("<elements>", "<elements ignore='true'>")); w.Import();
        Require(w.Query("SELECT COUNT(*) FROM elements") == "0", "Ignored input remained active");
    }

    internal static void Contract()
    {
        Require(LocalCorrectionDocument.Fingerprint(XElement.Parse("<element id='ID'/>")) == "D19DAA54B23AE7E39163AAC501B408A5CF9BDA12A6B294A01E7FCFBB6B57C435", "V1 declaration fingerprint changed");
        var correction = LocalCorrectionDocument.Create(Fixed, Baseline, "core/features.xml", [Replacement()]);
        var result = LocalCorrectionDocument.Evaluate(correction, Baseline.Replace("<element id='ID_COMPANION' name='Companion' type='Item' source='Test'><description>old companion</description></element>", ""));
        Require(!result.EffectiveXml.Contains("ID_COMPANION"), "Removed companion resurrected");
        using var w = new Workspace(); w.Mark();
        Reject(() => LocalCorrectionDocument.AcceptUpstream(w.Local, "stale", Hash(w.Origin), ["fix"]), "changed");
        w.Write("core/other.xml", Baseline);
        w.Write("user/local/fix.xml", LocalCorrectionDocument.Create(Fixed, Baseline, "core/features.xml", [Replacement() with { Group = "cross-file" }]));
        w.Write("user/local/other.xml", LocalCorrectionDocument.Create(Fixed, Baseline, "core/other.xml", [Replacement("accepted-upstream") with { Group = "cross-file" }]));
        Reject(() => w.Import(), "cross-file");
        File.Delete(Path.Combine(w.Root, "core/other.xml")); File.Delete(Path.Combine(w.Root, "user/local/other.xml"));
        w.Write("user/local/fix.xml", correction.Replace("core/features.xml", "../outside.xml"));
        Reject(() => w.Import(), "outside");
        w.Write("user/local/fix.xml", correction.Replace("<elements>", "<elements ignore='true'>"));
        w.Import(); Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_FIX'") == "Old", "Disabled correction applied");
        Require(File.Exists(w.Local), "Disabled local retired");
        w.Write("operational.xml", "<elements><append id='ID_FIX'><supports>Local</supports></append></elements>");
        Reject(() => w.Import(), "append");
    }

    internal static void WriterGuard()
    {
        using var w = new Workspace(); w.Write("copy.xml", Baseline);
        var catalog = AuroraTranslator.Program.BuildAuroraImportCatalog(w.Root);
        Reject(() => AuroraSqliteImporter.ImportFinalized(catalog, TestPaths.SchemaPath, w.Database), "finalized");
        Require(!File.Exists(w.Database), "Writer opened database before validation");
    }

    internal static void PackageSettings()
    {
        using var w = new Workspace(); w.Import();
        w.Query("UPDATE content_packages SET is_enabled=0, precedence_rank=731; SELECT 1");
        w.Import();
        Require(w.Query("SELECT is_enabled || ':' || precedence_rank FROM content_packages") == "0:731", "Prepared import overwrote package settings");
        Require(w.Query("SELECT COUNT(*) FROM elements") == "2", "Disabled package declarations were deleted");
        Require(w.Query("SELECT COUNT(*) FROM resolved_elements_cache") == "0", "Disabled package content was activated");
    }

    internal static void EnabledSupplier()
    {
        using var w = new Workspace(); w.Write("supplements/copy.xml", Baseline); w.Import();
        string packageKey = w.Query("SELECT package_key FROM content_packages WHERE package_kind='core'");
        string beforeUpdate = Hash(w.Database);
        Reject(() => AuroraSqliteImporter.UpdateContentPackageSettings(w.Database, packageKey, isEnabled: false), "preparation");
        SqliteConnection.ClearAllPools();
        Require(Hash(w.Database) == beforeUpdate, "Unsupported package maintenance changed the working database");
        // Model settings supplied before preparation; the legacy database-only
        // command cannot express the new supplier-selection workflow.
        w.Query("UPDATE content_packages SET is_enabled=0 WHERE package_kind='core'; SELECT 1");
        w.Import();
        Require(w.Query("SELECT COUNT(*) FROM resolved_elements_cache") == "2", "Disabled representative hid an enabled identical supplier");
        Require(w.Query("SELECT COUNT(*) FROM content_declaration_provenance") == "4", "Disabled supplier provenance was lost");
        w.Query("UPDATE content_packages SET is_enabled=CASE WHEN package_kind='core' THEN 1 ELSE 0 END; SELECT 1");
        w.Import();
        Require(w.Query("SELECT COUNT(*) FROM resolved_elements_cache WHERE package_kind='core'") == "2", "Re-enabled supplier was not selected during preparation");
    }

    internal static void Cli()
    {
        using var w = new Workspace(); w.Mark(); string localHash = Hash(w.Local);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(AuroraTranslator.Program).Assembly.Location);
        start.ArgumentList.Add("sqlite-import"); start.ArgumentList.Add(w.Root); start.ArgumentList.Add(w.Database);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        Require(process.WaitForExit(60000), "Standalone CLI timed out");
        Require(process.ExitCode == 0, "Standalone CLI failed: " + error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
        Require(w.Query("SELECT state FROM local_corrections") == "review-pending", "CLI did not mirror protection");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_FIX'") == "Fixed", "CLI did not prepare corrections");
        Require(Hash(w.Local) == localHash, "CLI mutated local XML");
    }

    internal static void Fixtures()
    {
        using var w = new Workspace(); File.Delete(w.Origin);
        foreach (string fixture in new[] { "staff", "devout", "tatsumi", "musketball" })
        {
            string fixedXml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CorrectionMetadata", fixture + ".xml"));
            var baseline = LocalCorrectionDocument.Parse(fixedXml);
            var elements = baseline.Root!.Elements("element").ToArray();
            var corrections = new List<LocalCorrection>();
            if (fixture == "staff")
            {
                string replacement = (string)elements[0].Attribute("id")!;
                elements[0].SetAttributeValue("id", "ID_WOTC_XGTE_MAGIC_ITEM_STAFF_OF_FLOWERS");
                elements[0].SetAttributeValue("source", "Xanathar's Guide to Everything");
                corrections.Add(new("staff", "rename", (string)elements[0].Attribute("id")!, replacement, LocalCorrectionDocument.Fingerprint(elements[0])));
                var xgte = new XElement(elements[0]); xgte.Element("description")!.Value = "Legitimate XGTE Staff definition";
                w.Write("supplements/xgte.xml", new XDocument(new XElement("elements", xgte)).ToString());
            }
            else if (fixture == "musketball")
            {
                var obsolete = new XElement(elements[0]);
                obsolete.SetAttributeValue("name", "Musketball (20 pieces)");
                baseline.Root.Add(obsolete);
                corrections.Add(new("remove-pack", "remove", (string)obsolete.Attribute("id")!, null, LocalCorrectionDocument.Fingerprint(obsolete)));
            }
            else
            {
                foreach (var e in elements.Skip(fixture == "devout" ? 1 : 2))
                {
                    string replacement = (string)e.Attribute("id")!;
                    string target = fixture == "devout" ? "ID_DEVOUT_COLLISION" : (string)elements[1].Attribute("id")!;
                    e.SetAttributeValue("id", target);
                    corrections.Add(new(replacement, "rename", target, replacement, LocalCorrectionDocument.Fingerprint(e), Group: fixture));
                }
                foreach (var grant in elements[0].Descendants("grant"))
                {
                    if (fixture == "devout") grant.SetAttributeValue("id", "");
                    else if (((string?)grant.Attribute("id"))?.EndsWith("HEARTENING_BREATH") == true)
                        grant.SetAttributeValue("id", (string)elements[1].Attribute("id")!);
                }
                corrections.Add(new("parent", "replace", (string)elements[0].Attribute("id")!, null, LocalCorrectionDocument.Fingerprint(elements[0]), Group: fixture));
                string partial = LocalCorrectionDocument.Create(fixedXml, baseline.ToString(SaveOptions.DisableFormatting), "core/" + fixture + ".xml", corrections.Select((c, i) => i == 0 ? c with { State = "accepted-upstream" } : c));
                Reject(() => LocalCorrectionDocument.Evaluate(partial, fixedXml), "together");
            }
            string baselineXml = baseline.ToString(SaveOptions.DisableFormatting);
            w.Write("core/" + fixture + ".xml", baselineXml);
            w.Write("user/local/" + fixture + ".xml", LocalCorrectionDocument.Create(fixedXml, baselineXml, "core/" + fixture + ".xml", corrections));
        }
        w.Import();
        Require(w.Query("SELECT COUNT(*) FROM elements WHERE name='Staff of Flowers'") == "2", "Staff definition suppressed across files");
        Require(w.Query("SELECT COUNT(*) FROM grants g JOIN elements e ON e.element_id=g.target_element_id WHERE e.aurora_id IN ('ID_JONOMAN3000_ARCHETYPE_FEATURE_DEVOUT_ZEALOTS_DEVOTION_DEFENDER_OF_KIN','ID_JONOMAN3000_ARCHETYPE_FEATURE_DEVOUT_ZEALOTS_DEVOTION_SLAYER_OF_FOES','ID_RGTTYR_RACIAL_TRAIT_TATSUMI_RYUJIN_HEARTENING_BREATH') AND g.target_aurora_id=e.aurora_id") == "3", "Repaired grants do not resolve");
        Require(w.Query("SELECT COUNT(*) FROM local_corrections WHERE state='review-pending'") == "7", "Fixture correction intent not mirrored");
        Require(w.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_RDDT_AA_MUSKETBALL'") == "1", "Removal deleted the retained musketball");
    }
}
