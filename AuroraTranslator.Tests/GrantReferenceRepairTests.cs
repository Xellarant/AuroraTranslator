using AuroraTranslator;
using AuroraTranslator.Content;
using Builder.Data.Files;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Xml.Linq;

internal static class GrantReferenceRepairTests
{
    internal static void ProtectedRepairs()
    {
        using var work = TestWorkspace.Create();
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GrantReferenceRepairs");
        string root = Path.Combine(work.DirectoryPath, "content");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "repairs.json")));
        var diagnostics = manifest.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        var localFiles = new List<string>();
        foreach (var file in diagnostics.GroupBy(d => d.GetProperty("fileName").GetString()!))
        {
            string original = File.ReadAllText(Path.Combine(fixtures, "content", file.Key));
            string origin = Path.Combine(root, file.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(origin)!);
            File.WriteAllText(origin, original);
            var baseline = LocalCorrectionDocument.Parse(original);
            var fixedDocument = LocalCorrectionDocument.Parse(original);
            var corrections = new List<LocalCorrection>();
            foreach (var owner in file.GroupBy(d => d.GetProperty("elementId").GetString()!))
            {
                XElement originalOwner = baseline.Root!.Elements("element").Single(e => (string?)e.Attribute("id") == owner.Key);
                XElement fixedOwner = fixedDocument.Root!.Elements("element").Single(e => (string?)e.Attribute("id") == owner.Key);
                foreach (var diagnostic in owner)
                {
                    var repair = diagnostic.GetProperty("repairs")[0];
                    TestAssert.Equal("set-rule-attribute", repair.GetProperty("kind").GetString());
                    TestAssert.Equal("id", repair.GetProperty("attribute").GetString());
                    var grant = fixedOwner.Descendants("grant").Single(g => (string?)g.Attribute("id") == repair.GetProperty("currentValue").GetString());
                    var expected = XElement.Parse(repair.GetProperty("target").GetProperty("nodeXml").GetString()!);
                    TestAssert.Equal(LocalCorrectionDocument.Fingerprint(expected), LocalCorrectionDocument.Fingerprint(grant));
                    grant.SetAttributeValue("id", repair.GetProperty("replacementValue").GetString());
                    TestAssert.Equal(LocalCorrectionDocument.Fingerprint(XElement.Parse(repair.GetProperty("sample").GetString()!)), LocalCorrectionDocument.Fingerprint(grant));
                }
                corrections.Add(new("repair-" + owner.Key, "replace", owner.Key, null,
                    LocalCorrectionDocument.Fingerprint(originalOwner), Reason: string.Join(" ", owner.Select(d => d.GetProperty("suggestion").GetString()))));
            }
            string localXml = LocalCorrectionDocument.Create(fixedDocument.ToString(SaveOptions.DisableFormatting), original, file.Key, corrections);
            var evaluation = LocalCorrectionDocument.Evaluate(localXml, original);
            TestAssert.Equal(LocalCorrectionDocument.Fingerprint(LocalCorrectionDocument.Parse(original).Root!),
                LocalCorrectionDocument.Fingerprint(LocalCorrectionDocument.Parse(evaluation.BaselineXml).Root!));
            TestAssert.Equal(false, evaluation.CanRetire);
            TestAssert.Equal(true, evaluation.Corrections.All(c => c.State == "review-pending"));
            string local = Path.Combine(root, "user", "local", Path.GetFileName(file.Key));
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            File.WriteAllText(local, localXml);
            localFiles.Add(local);
        }
        foreach (var diagnostic in manifest.RootElement.GetProperty("unresolvedDiagnostics").EnumerateArray())
        {
            string path = diagnostic.GetProperty("fileName").GetString()!;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            File.Copy(Path.Combine(fixtures, "content", path), Path.Combine(root, path));
        }
        Directory.CreateDirectory(Path.Combine(root, "core"));
        File.Copy(Path.Combine(fixtures, "targets.xml"), Path.Combine(root, "core", "repair-targets.xml"), overwrite: false);
        LocalCorrectionSync.ImportAsync([root], work.DatabasePath, (roots, candidate, _) =>
        {
            AuroraSqliteImporter.ImportFinalized(AuroraTranslator.Program.BuildAuroraImportCatalog(roots[0]), TestPaths.SchemaPath, candidate);
            return Task.FromResult(new CorrectionImportResult(true));
        }).GetAwaiter().GetResult();
        using var connection = new SqliteConnection($"Data Source={work.DatabasePath};Pooling=False"); connection.Open();
        foreach (var diagnostic in diagnostics)
        {
            using var query = connection.CreateCommand();
            query.CommandText = """
                SELECT COUNT(*) FROM grants g JOIN rule_scopes rs ON rs.rule_scope_id=g.rule_scope_id
                JOIN elements owner ON owner.element_id=rs.owner_element_id
                JOIN elements target ON target.element_id=g.target_element_id
                WHERE owner.aurora_id=$owner AND g.target_aurora_id=$target AND target.aurora_id=$target;
                """;
            query.Parameters.AddWithValue("$owner", diagnostic.GetProperty("elementId").GetString());
            query.Parameters.AddWithValue("$target", diagnostic.GetProperty("repairs")[0].GetProperty("replacementValue").GetString());
            TestAssert.Equal(1L, (long)query.ExecuteScalar()!);
        }
        TestAssert.Equal(true, localFiles.All(File.Exists));
        using var mirror = connection.CreateCommand();
        mirror.CommandText = "SELECT COUNT(*) FROM local_corrections WHERE state <> 'review-pending';";
        TestAssert.Equal(0L, (long)mirror.ExecuteScalar()!);
        // The explicit local correction changes Stoneheart's grant while both
        // independent spell definitions remain present.
        mirror.CommandText = "SELECT COUNT(*) FROM elements WHERE aurora_id IN ('ID_POTA_SPELL_ERUPTINGEARTH','ID_XGTE_SPELL_ERUPTING_EARTH');";
        TestAssert.Equal(2L, (long)mirror.ExecuteScalar()!);
        mirror.CommandText = "SELECT COUNT(*) FROM grants WHERE target_aurora_id='ID_PHB_SPELL_ERUPTING_EARTH';";
        TestAssert.Equal(0L, (long)mirror.ExecuteScalar()!);
        mirror.CommandText = "SELECT COUNT(*) FROM local_corrections WHERE target_id='ID_KT_SS_ARCHETYPE_SORCERER_STONEHEART' AND state='review-pending' AND reason LIKE '%User-authorized local correction%' AND reason LIKE '%Pending confirmation with KibblesTasty%';";
        TestAssert.Equal(1L, (long)mirror.ExecuteScalar()!);
    }
}
