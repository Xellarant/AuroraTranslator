using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

internal static class ContentCatalogReaderTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private const string Shared = "ID_CATALOG_SHARED";

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal Fixture()
        {
            string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CatalogReader");
            foreach (string file in Directory.GetFiles(fixtures, "*.xml", SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(fixtures, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        internal void Import(string? database = null) => ContentImport.ImportAsync(Root, database ?? Database, skipUnusableContent: true).GetAwaiter().GetResult();
        internal void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Database};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        }
        internal ContentCatalogDetail Detail(string id = Shared) => ContentCatalogReader.ReadDetail(Database, id) ?? throw new Exception($"Missing detail for {id}");
        public void Dispose() => work.Dispose();
    }

    internal static void EffectiveSummaries()
    {
        using var f = new Fixture(); f.Import();
        var snapshot = ContentCatalogReader.ReadSummaries(f.Database);
        var entries = snapshot.Entries;
        Require(snapshot.Metadata.DataVersion == ContentDatabaseReader.CurrentDataVersion, "Snapshot reports its database contract.");
        Require(entries.Count == 11 && entries.Select(e => e.AuroraId).Distinct(StringComparer.Ordinal).Count() == 11,
            "Every effective identity appears once; superseded declarations and forwarding addresses are not entries.");
        Require(entries.Select(e => e.AuroraId).SequenceEqual(entries.Select(e => e.AuroraId).Order(StringComparer.Ordinal)), "Catalog order is stable by exact ID.");
        Require(entries.Count(e => e.Name == "Staff of Flowers") == 2, "Same-name, distinct-ID definitions must both survive.");
        var local = entries.Single(e => e.AuroraId == Shared);
        Require(local.Name == "Effective local definition" && local.SourceBook == "Local Book"
            && local.Supplier?.PackageKind == "homebrew" && local.Supplier.RelativePath.Replace('\\', '/') == "user/catalog.xml",
            "Summary and supplier must describe the selected definition.");
        Require(local.SupersededDeclarationCount == 1 && local.ResolutionKind == "provisional", "A usable conflicting ID still exposes its review state.");
        Require(!local.SummaryText.Contains("Superseded") && local.SummaryText.Contains("Effective"), "Losing text must not leak.");
        Require(entries.Single(e => e.AuroraId == "ID_CATALOG_HIDDEN").CompendiumDisplay == false, "Presentation filtering belongs to consumers.");
        var spell = entries.Single(e => e.AuroraId == "ID_CATALOG_SPELL");
        Require(spell.Supplier?.PackageKind == "official" && spell.Spell is { Level: 2, School: "divination", IsRitual: true, IsConcentration: true }, "UA remains first party with typed spell filters.");
        Require(entries.Single(e => e.AuroraId == "ID_CATALOG_SUPPLEMENT").Supplier?.PackageKind == "third-party", "Published supplements are third party.");
        Require(entries.Single(e => e.AuroraId == "ID_CATALOG_DMG_STAFF").Supplier?.PackageKind == "core" && local.Spell == null, "Core classification and absent spell facets are preserved.");
    }

    internal static void DetailsAndExplicitLinks()
    {
        using var f = new Fixture(); f.Import();
        // Preparation normally rejects this alias; a reader must still prefer a live identity.
        f.Execute("INSERT INTO content_element_aliases VALUES ('ID_CATALOG_TARGET','ID_CATALOG_ACTION','fixture',NULL);");
        var detail = f.Detail();
        Require(detail.RequestedId == Shared && detail.Alias == null && detail.DescriptionText.Contains("Effective")
            && detail.DescriptionXml!.Contains("<b>local</b>"), "Detail preserves text and imported markup separately.");
        var xml = XElement.Parse(detail.EffectiveXml!);
        Require(xml.Element("supports")!.Value == "Appended support" && detail.AppliedAppends.Count == 1
            && detail.AppliedAppends[0].Source.PackageKind == "homebrew", "Materialized appends retain separate provenance.");
        Require(detail.Links.Count == 4 && detail.Links.All(l => l.Kind == "grant"), "Only effective and appended grants are exposed, exactly once.");
        var grant = detail.Links.Single(l => l.RequestedId == "ID_CATALOG_TARGET");
        Require(grant.Status == ContentCatalogLinkStatus.Resolved && grant.Target?.AuroraId == "ID_CATALOG_TARGET" && grant.Alias == null,
            "A real ID wins even when the XML attempted to redirect it.");
        var rule = XElement.Parse(grant.RuleXml!);
        Require((string?)rule.Attribute("level") == "3" && (string?)rule.Attribute("requirements") == "[level:3]", "Resolving a grant preserves its activation conditions.");
        Require(detail.Links.Single(l => l.RequestedId == "ID_CATALOG_MISSING").Status == ContentCatalogLinkStatus.MissingTarget,
            "A missing explicit reference stays visible; no name guess fills it.");
        var forwarded = f.Detail("ID_CATALOG_RETIRED_ACTION");
        Require(forwarded.RequestedId == "ID_CATALOG_RETIRED_ACTION" && forwarded.Summary.AuroraId == "ID_CATALOG_ACTION"
            && forwarded.Alias is { TargetId: "ID_CATALOG_ACTION" } && !string.IsNullOrEmpty(forwarded.Alias.Origin), "Detail identifies forwarding and its provenance.");
        Require(detail.Links.Single(l => l.RequestedId == forwarded.RequestedId).Alias == forwarded.Alias, "Linked lookups share the alias contract.");
        var companion = f.Detail("ID_CATALOG_COMPANION");
        Require(companion.Links.Count == 5 && companion.Links.Count(l => l.Status == ContentCatalogLinkStatus.Resolved) == 2
            && companion.Links.Count(l => l.Status == ContentCatalogLinkStatus.TypeMismatch) == 2
            && companion.Links.Count(l => l.Status == ContentCatalogLinkStatus.MissingTarget) == 1, "Companion references report wrong types and missing IDs instead of silently dropping them.");
        Require(f.Detail("ID_CATALOG_COPIED").Suppliers.Count == 2, "Identical surviving declarations credit both suppliers.");
        Require(ContentCatalogReader.ReadDetail(f.Database, "ID_UNKNOWN") == null, "Unknown IDs return no detail.");
        Require(ContentCatalogReader.ReadDetail(f.Database, Shared.ToLowerInvariant()) == null, "Public lookups preserve exact declaration identity.");
        f.Execute("INSERT INTO content_unavailable_elements VALUES ('ID_CATALOG_ACTION','fixture exclusion');");
        Require(ContentCatalogReader.ReadDetail(f.Database, "ID_CATALOG_RETIRED_ACTION") == null
            && ContentCatalogReader.ReadSummaries(f.Database).Entries.All(e => e.AuroraId != "ID_CATALOG_ACTION"),
            "Unavailable targets stay excluded through direct reads and aliases.");
    }

    internal static void PreferencesAndSnapshotBoundary()
    {
        using var f = new Fixture(); f.Import();
        string before = Projection(f.Database);
        f.Execute("UPDATE content_packages SET is_enabled=0,precedence_rank=0;");
        Require(before == Projection(f.Database), "Saved package preferences cannot filter or reorder the catalog.");
        string path = Path.Combine(f.Root, "user", "catalog.xml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Effective local definition", "Edited outside the snapshot"));
        byte[] databaseHash = SHA256.HashData(File.ReadAllBytes(f.Database));
        var inputHashes = Directory.GetFiles(f.Root, "*.xml", SearchOption.AllDirectories).ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        Require(before == Projection(f.Database), "Catalog reads must not silently merge live XML overlays.");
        Require(ContentDatabaseReader.IsStale([f.Root], f.Database), "The existing freshness API still reports an edited source.");
        Require(databaseHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(f.Database)))
            && inputHashes.All(p => p.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(p.Key)))), "Normal catalog reads do not write databases or XML.");
    }

    internal static void RefreshParity()
    {
        using var f = new Fixture(); f.Import();
        string initial = Projection(f.Database);
        f.Import(); Require(initial == Projection(f.Database), "An unchanged import preserves the catalog projection.");
        string path = Path.Combine(f.Root, "user", "catalog.xml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Effective <b>local</b> text.", "Updated <b>local</b> text."));
        f.Import(); string refreshed = Projection(f.Database);
        Require(refreshed != initial && f.Detail().DescriptionText.Contains("Updated"), "Changed XML reaches detail after import.");
        string fresh = Path.Combine(Path.GetDirectoryName(f.Database)!, "fresh.sqlite"); f.Import(fresh);
        Require(refreshed == Projection(fresh), "A fresh import and a refreshed database expose equivalent summaries and details.");
    }

    internal static void DatabaseErrors()
    {
        using var f = new Fixture();
        Expect<FileNotFoundException>(() => ContentCatalogReader.ReadSummaries(f.Database), "Import");
        Require(!File.Exists(f.Database), "A missing catalog read must not create an empty database.");
        f.Import();
        void Reject() { Expect<InvalidDataException>(() => ContentCatalogReader.ReadSummaries(f.Database), "ContentImport.ImportAsync"); Expect<InvalidDataException>(() => f.Detail(), "ContentImport.ImportAsync"); }
        f.Execute("UPDATE database_metadata SET data_version=15;"); Reject();
        f.Execute($"UPDATE database_metadata SET data_version={ContentDatabaseReader.CurrentDataVersion + 1};"); Reject();
        f.Execute($"UPDATE database_metadata SET data_version={ContentDatabaseReader.CurrentDataVersion},schema_version=2;"); Reject();
        f.Execute("UPDATE database_metadata SET schema_version=1; UPDATE content_preparation_metadata SET catalog_policy='filtered';"); Reject();
        f.Execute("DROP TABLE content_preparation_metadata;"); Reject();
    }

    // Deliberately excludes import timestamps/row IDs; it compares the public content contract.
    private static string Projection(string database)
    {
        var catalog = ContentCatalogReader.ReadSummaries(database);
        return JsonSerializer.Serialize(new { catalog.Entries, Details = catalog.Entries.Select(e =>
        {
            var d = ContentCatalogReader.ReadDetail(database, e.AuroraId)!;
            return new { d.RequestedId, d.Alias, d.Summary, d.DescriptionText, d.DescriptionXml, d.EffectiveXml, d.Suppliers, d.AppliedAppends, d.Links };
        }).ToArray() });
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T ex) { Require(ex.Message.Contains(message), $"Actionable diagnostic missing: {ex.Message}"); return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
