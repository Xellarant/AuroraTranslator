using System.Xml.Linq;
using Aurora.Content.Preparation;
using Microsoft.Data.Sqlite;

/// <summary>
/// Golden expectations from Aurora-Lights' Legacy DataManager.GetCustomFiles/AppendElements
/// and tools/ContentDatabaseRehearsal/LegacyAppendAudit.cs. These do not derive their expected
/// results from the shared composer: XML/database parity alone cannot establish Legacy parity.
/// </summary>
internal static class LegacyAppendCompatibilityTests
{
    private const string MainId = "ID_LEGACY_APPEND";
    private const string EmptyId = "ID_LEGACY_APPEND_NO_DESCRIPTION";
    private const string HostId = "ID_LEGACY_APPEND_HOST";
    private const string MainDefinition = """
        <element id="ID_LEGACY_APPEND" name="Append fixture" type="Feat" source="Test">
          <description><p>Base</p></description>
          <rules><grant type="Proficiency" id="ID_BASE_GRANT"/></rules>
        </element>
        """;
    private const string SupplementXml = """
        <elements>
          <append id="ID_LEGACY_APPEND"><description><p>Supplement prose</p></description><rules>
            <grant type="Proficiency" id="ID_SUPPLEMENT_FIRST"/>
            <grant type="Proficiency" id="ID_SUPPLEMENT_SECOND"/>
            <grant type="Proficiency" id="ID_SUPPLEMENT_FIRST"/>
          </rules></append>
          <append id="ID_LEGACY_APPEND"><rules><grant type="Proficiency" id="ID_SUPPLEMENT_NEXT_APPEND"/></rules></append>
        </elements>
        """;
    private const string DescendantXml = """
        <elements><append id="ID_LEGACY_APPEND"><rules><grant type="Proficiency" id="ID_DESCENDANT"/></rules></append></elements>
        """;
    private const string HomebrewXml = """
        <elements>
          <append id="ID_LEGACY_APPEND"><description><p>Extra</p></description><rules><grant type="Proficiency" id="ID_HOMEBREW"/></rules></append>
          <append id="ID_LEGACY_APPEND_NO_DESCRIPTION"><description><p>Must not fill an absent description</p></description></append>
        </elements>
        """;
    private static readonly string[] ExpectedGrants =
    [
        "ID_BASE_GRANT", "ID_SUPPLEMENT_FIRST", "ID_SUPPLEMENT_SECOND", "ID_SUPPLEMENT_FIRST",
        "ID_SUPPLEMENT_NEXT_APPEND", "ID_DESCENDANT", "ID_HOMEBREW"
    ];

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal Fixture(bool appends = true)
        {
            var targets = ExpectedGrants.Distinct().Select(id =>
                new XElement("element", new XAttribute("id", id), new XAttribute("name", id),
                    new XAttribute("type", "Proficiency"), new XAttribute("source", "Test")));
            Write("core/base.xml", new XElement("elements", XElement.Parse(MainDefinition),
                new XElement("element", new XAttribute("id", EmptyId), new XAttribute("name", "No description"),
                    new XAttribute("type", "Feat"), new XAttribute("source", "Test")), targets).ToString());
            if (appends)
            {
                Write("supplements/b.xml", SupplementXml);
                Write("supplements/a-child/a.xml", DescendantXml);
                Write("homebrew/a.xml", HomebrewXml);
            }
        }
        internal void Write(string relative, string xml)
        {
            string path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }
        internal void Import() => ContentImport.ImportAsync(Root, Database).GetAwaiter().GetResult();
        internal SqliteConnection Open() => ContentDatabase.OpenReadableConnection(Database);
        internal PreparedCatalogFile Runtime(string relative, string xml, string? root = null) =>
            new(new(Path.Combine(root ?? Root, relative), relative, "fixture", "homebrew"), xml);
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static string[] Query(SqliteConnection connection, string sql, string id = MainId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static XElement Element(PreparedCatalogProjection projection, string id = MainId) =>
        XElement.Parse(projection.Elements.Single(e => e.AuroraId == id).Xml);

    private static void CheckElement(XElement element, string[]? expected = null)
    {
        TestAssert.Sequence(expected ?? ExpectedGrants,
            element.Element("rules")!.Elements("grant").Select(g => (string)g.Attribute("id")!).ToArray());
        Require(XNode.DeepEquals(XElement.Parse("<description><p>Base</p></description>"), element.Element("description")),
            "Legacy never transfers an appended description to the target.");
    }

    internal static void DescriptionsAreNeverTransferred()
    {
        // DataManager.AppendElements transfers supports, setters, rules and spellcasting only.
        // This includes targets lacking a description; append prose is not a fallback description.
        var append = XElement.Parse("""
            <append id="ID_LEGACY_APPEND"><description format="ignored"><p>Extra</p></description>
              <supports>Useful support</supports><description><![CDATA[Another fragment]]></description>
            </append>
            """);
        foreach (string definition in new[] { MainDefinition,
            "<element id='ID_LEGACY_APPEND' name='No description' type='Feat' source='Test'/>" })
        {
            var target = XElement.Parse(definition);
            var originalTarget = new XElement(target);
            var originalAppend = new XElement(append);
            var actual = ContentAppendComposer.Apply(target, append);
            Require(XNode.DeepEquals(originalTarget.Element("description"), actual.Element("description")),
                "An append must leave both present and absent descriptions unchanged.");
            Require(actual.Element("supports")?.Value == "Useful support", "Ignoring prose must retain supported mechanics.");
            Require(XNode.DeepEquals(originalTarget, target) && XNode.DeepEquals(originalAppend, append),
                "Composition must not rewrite either authored input.");
        }
    }

    internal static void ImportedRowsMatchLegacy()
    {
        using var fixture = new Fixture();
        fixture.Import();
        using var connection = fixture.Open();
        var effective = XElement.Parse(Query(connection,
            "SELECT effective_xml FROM content_prepared_elements WHERE aurora_id=$id").Single());
        CheckElement(effective);
        TestAssert.Sequence(ExpectedGrants, Query(connection, """
            SELECT g.target_aurora_id FROM resolved_elements_cache r
            JOIN elements e ON e.element_id=r.winning_element_id
            JOIN rule_scopes s ON s.owner_element_id=e.element_id AND s.owner_kind='element'
            JOIN grants g ON g.rule_scope_id=s.rule_scope_id
            WHERE e.aurora_id=$id ORDER BY g.ordinal
            """));
        const string descriptions = """
            SELECT t.body FROM element_texts t JOIN elements e ON e.element_id=t.element_id
            WHERE e.aurora_id=$id AND t.text_kind='description' ORDER BY t.ordinal
            """;
        TestAssert.Sequence(["Base"], Query(connection, descriptions));
        TestAssert.Sequence([], Query(connection, descriptions, EmptyId));
        Require(XElement.Parse(Query(connection,
                "SELECT effective_xml FROM content_prepared_elements WHERE aurora_id=$id", EmptyId).Single()).Element("description") == null,
            "A description-only append cannot supply missing display content.");

        // Ignoring a display effect must not erase the authored operation or its source.
        var operations = Query(connection, """
            SELECT operation_xml FROM content_append_operations
            WHERE REPLACE(relative_path,'\','/')='homebrew/a.xml' ORDER BY ordinal
            """);
        var authored = XElement.Parse(HomebrewXml).Elements("append").ToArray();
        Require(operations.Length == authored.Length && operations.Select(XElement.Parse)
                .Zip(authored).All(pair => XNode.DeepEquals(pair.First, pair.Second)),
            "Persisted append provenance retains ignored descriptions and original mechanics.");
        TestAssert.Sequence(["homebrew/a.xml", "supplements/a-child/a.xml", "supplements/b.xml"], Query(connection, """
            SELECT DISTINCT REPLACE(relative_path,'\','/') AS path
            FROM content_append_operations WHERE status='applied' ORDER BY path
            """));
        var detail = ContentCatalogReader.ReadDetail(fixture.Database, MainId)
            ?? throw new Exception("The imported target must expose a catalog detail.");
        TestAssert.Sequence(["supplements/b.xml", "supplements/b.xml", "supplements/a-child/a.xml", "homebrew/a.xml"],
            detail.AppliedAppends.Select(a => a.Source.RelativePath.Replace('\\', '/')).ToArray());
        TestAssert.Sequence(["0", "1", "0", "0"], detail.AppliedAppends.Select(a => a.Ordinal.ToString()).ToArray());
    }

    internal static void StoredAndRuntimeReplayMatchLegacy()
    {
        using var fixture = new Fixture();
        fixture.Import();
        using var connection = fixture.Open();
        CheckElement(Element(PreparedCatalogReader.Read(connection)));

        // Replace one stored file with a runtime revision. Old operations must not run again.
        string changed = HomebrewXml.Replace("ID_HOMEBREW", "ID_RUNTIME_HOMEBREW", StringComparison.Ordinal);
        var mixed = PreparedCatalogReader.Read(connection,
            runtimeFiles: [fixture.Runtime("homebrew/a.xml", changed)]);
        CheckElement(Element(mixed), ExpectedGrants[..^1].Append("ID_RUNTIME_HOMEBREW").ToArray());
        Require(Element(mixed, EmptyId).Element("description") == null && mixed.UnresolvedAppends.Count == 0,
            "Runtime replacement preserves absent descriptions and does not duplicate operations.");
        CheckElement(Element(PreparedCatalogReader.Read(connection)));
    }

    internal static void HostAndRuntimeOrderUsesRelativePaths()
    {
        using var fixture = new Fixture(appends: false);
        fixture.Write("supplements/b.xml", SupplementXml.Replace(MainId, HostId, StringComparison.Ordinal));
        fixture.Import();
        using var connection = fixture.Open();
        Require(PreparedCatalogReader.Read(connection).UnresolvedAppends.Count == 2,
            "The two stored operations remain available for an exact host-provided target.");
        var hostSource = new PreparedCatalogSource("resource://fixture", "runtime/fixture.xml", "runtime", "core");
        var host = new PreparedCatalogElement(HostId, hostSource, MainDefinition.Replace(MainId, HostId, StringComparison.Ordinal));
        string homebrew = HomebrewXml.Replace($"id=\"{MainId}\"", $"id=\"{HostId}\"", StringComparison.Ordinal);
        string descendant = DescendantXml.Replace(MainId, HostId, StringComparison.Ordinal);
        // Source-relative ordering must be invariant under absolute source-root spelling.
        // This does not claim multi-root Legacy parity: Legacy separately processes roots in
        // configured order, which the current PreparedCatalogSource contract does not represent.
        // Absolute root names deliberately disagree with both the ladder and depth-first file walk.
        // Legacy enumerates a directory's own files before descendants; its sibling enumeration
        // is filesystem-provided, so this fixture makes no alphabetical sibling-order claim.
        foreach (bool reverse in new[] { false, true })
        {
            string first = Path.Combine(fixture.Root, reverse ? "zzz-external" : "aaa-external");
            string last = Path.Combine(fixture.Root, reverse ? "aaa-external" : "zzz-external");
            PreparedCatalogFile[] files =
            [
                fixture.Runtime("homebrew/a.xml", homebrew, first),
                fixture.Runtime("supplements/a-child/a.xml", descendant, last)
            ];
            var projection = PreparedCatalogReader.Read(connection, hostDefinitions: [host], runtimeFiles: files);
            CheckElement(Element(projection, HostId));
            Require(projection.UnresolvedAppends.Count == 0, "Stored and runtime extensions resolve to the host by exact ID.");
        }
    }
}
