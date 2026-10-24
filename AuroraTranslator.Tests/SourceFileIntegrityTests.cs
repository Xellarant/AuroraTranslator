using System.Text;
using Aurora.Content;

internal static class SourceFileIntegrityTests
{
    private const string Xml = """
<?xml version="1.0" encoding="utf-8"?>
<elements>
  <element id="ID_TEST_SOURCE_BYTES" name="Source bytes" type="Feat" source="Fixture">
    <description><p>Original description.</p></description>
  </element>
</elements>
""" + "\n";

    public static void PreparedInputsKeepTheirOriginalFingerprint()
    {
        foreach (bool bom in new[] { false, true })
        {
            using var workspace = TestWorkspace.Create();
            string root = CreateSource(workspace, bom);
            ContentImport.ImportAsync(root, workspace.DatabasePath).GetAwaiter().GetResult();
            byte[] input = File.ReadAllBytes(Path.Combine(root, "fixture.xml"));
            byte[] database = File.ReadAllBytes(workspace.DatabasePath);

            TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, root));
            TestAssert.Equal(true, input.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "fixture.xml"))));
            TestAssert.Equal(true, database.SequenceEqual(File.ReadAllBytes(workspace.DatabasePath)));
        }
    }

    public static void PreparedInputsStillDetectEditsAndMissingFiles()
    {
        using var workspace = TestWorkspace.Create();
        string root = CreateSource(workspace, false);
        string path = Path.Combine(root, "fixture.xml");
        ContentImport.ImportAsync(root, workspace.DatabasePath).GetAwaiter().GetResult();
        byte[] original = File.ReadAllBytes(path);
        File.WriteAllText(path, Xml.Replace("Original description.", "Edited description."), new UTF8Encoding(false));
        TestAssert.Sequence(new[] { "Source file hash mismatch: fixture.xml" },
            AuroraDataIntegrity.Check(workspace.DatabasePath, root));

        File.WriteAllBytes(path, original);
        TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, root));
        File.Delete(path);
        TestAssert.Sequence(new[] { "Source file unavailable: fixture.xml" },
            AuroraDataIntegrity.Check(workspace.DatabasePath, root));
    }

    public static void DirectImportsKeepTheirRawFingerprintCheck()
    {
        using var workspace = TestWorkspace.Create();
        string root = CreateSource(workspace, false);
        AuroraSqliteImporter.Import(AuroraCatalogBuilder.BuildAuroraImportCatalog(root),
            TestPaths.SchemaPath, workspace.DatabasePath);
        TestAssert.Sequence(Array.Empty<string>(), AuroraDataIntegrity.Check(workspace.DatabasePath, root));
        File.WriteAllText(Path.Combine(root, "fixture.xml"), Xml.Replace("Original description.", "Edited description."),
            new UTF8Encoding(false));
        TestAssert.Sequence(new[] { "Source file hash mismatch: fixture.xml" },
            AuroraDataIntegrity.Check(workspace.DatabasePath, root));
    }

    private static string CreateSource(TestWorkspace workspace, bool bom)
    {
        string root = Path.Combine(workspace.DirectoryPath, "source root");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "fixture.xml"), bom ? Xml.Replace("\n", "\r\n") : Xml,
            new UTF8Encoding(bom));
        return root;
    }
}
