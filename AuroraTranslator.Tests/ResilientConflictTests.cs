using Aurora.Content.Preparation;
using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Xml.Linq;

internal static class ResilientConflictTests
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Element(string id, string name) => $"<element id='{id}' name='{name}' type='Class Feature' source='Test'><description>{name}</description></element>";
    private static string Origin(string owner) => $"<info><update version='0.0.1'><file name='items.xml' url='https://raw.githubusercontent.com/{owner}/elements/master/core/items.xml'/></update></info>";
    private sealed class Workspace : IDisposable
    {
        private readonly TestWorkspace work = TestWorkspace.Create();
        internal string Root => Path.Combine(work.DirectoryPath, "content");
        internal string Database => work.DatabasePath;
        internal void WriteRaw(string path, string xml)
        {
            path = Path.Combine(Root, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
        }
        internal void Write(string path, string xml)
        {
            path = Path.Combine(Root, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<elements>" + xml + "</elements>");
        }
        internal ContentImportResult Import(bool skip = true) => ContentImport.ImportAsync(Root, Database, skipUnusableContent: skip).GetAwaiter().GetResult();
        internal SqliteConnection Open() { var c = new SqliteConnection($"Data Source={Database};Pooling=False"); c.Open(); return c; }
        internal string Query(string sql) { using var c = Open(); using var q = c.CreateCommand(); q.CommandText = sql; return Convert.ToString(q.ExecuteScalar()) ?? ""; }
        internal string Project(string id, bool runtime = false)
        {
            using var c = Open();
            var files = runtime ? RuntimeContentFiles.Read(c, Root, [], includePrimary: true) : [];
            return PreparedCatalogReader.Read(c, runtimeFiles: files).Elements.Single(e => e.AuroraId == id).Xml;
        }
        public void Dispose() { SqliteConnection.ClearAllPools(); work.Dispose(); }
    }

    internal static void FirstValidAndRuntime()
    {
        using var w = new Workspace();
        w.Write("user/late.xml", Element("ID_COLLISION", "LoadsLast") + Element("ID_LOCAL", "Local"));
        w.Write("core/early.xml", Element("ID_COLLISION", "EarlierBucket") + Element("ID_COLLISION", "SameFileEarlier"));
        var hashes = Directory.GetFiles(w.Root, "*.xml", SearchOption.AllDirectories).ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        var result = w.Import();
        Require(w.Query("SELECT e.name FROM resolved_elements_cache r JOIN elements e ON e.element_id=r.winning_element_id WHERE r.aurora_id='ID_COLLISION'") == "LoadsLast", "The declaration Legacy loads last owns the id: files directly under user outrank core.");
        Require(result.Skipped.Count(s => s.Kind == "definition-collision") == 2, "Every rejected alternative is reported.");
        Require(ContentDatabaseReader.ReadUnavailableIds(w.Database).Count == 0, "A usable provisional choice is available.");
        Require(w.Project("ID_COLLISION", true).Contains("LoadsLast") && w.Project("ID_LOCAL", true).Contains("Local"), "Runtime keeps the selected choice and unrelated local content, including same-file collisions.");
        Require(w.Query("SELECT COUNT(*) FROM content_declaration_provenance WHERE aurora_id='ID_COLLISION'") == "3", "All suppliers remain inspectable.");
        Require(w.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_COLLISION'") == "3"
            && w.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_COLLISION' AND declaration_status='superseded'") == "2",
            "Every declaration keeps a row of its own, with the two that lost marked superseded.");
        Require(w.Query("SELECT COUNT(*) FROM v_duplicate_aurora_ids WHERE aurora_id='ID_COLLISION' AND is_winner=1") == "1",
            "The disagreement is inspectable and names exactly one winner.");
        Require(hashes.All(x => x.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x.Key)))), "No original XML was rewritten.");
        w.Import();
        Require(w.Query("SELECT e.name FROM resolved_elements_cache r JOIN elements e ON e.element_id=r.winning_element_id WHERE r.aurora_id='ID_COLLISION'") == "LoadsLast", "Repeated imports preserve the same working choice.");
    }

    internal static void RuntimeUnrelatedEditsPreserveChoices()
    {
        using var w = new Workspace();
        w.Write("user/a.xml", Element("ID_SHARED", "Earlier") + Element("ID_OTHER", "Old"));
        w.Write("user/z.xml", Element("ID_SHARED", "Winner") + Element("ID_WINNER_OTHER", "Old"));
        var imported = w.Import();
        var issue = imported.Skipped.Single(s => s.Kind == "definition-collision");
        Require(issue.Detail.Contains("ID_SHARED") && issue.Detail.Contains("user/a.xml".Replace('/', Path.DirectorySeparatorChar))
            && issue.Detail.Contains("user/z.xml".Replace('/', Path.DirectorySeparatorChar))
            && issue.Detail.Contains("requires review") && issue.Detail.Contains("distinct IDs"),
            "A provisional choice names the conflicting ID and both files, and explains how to intervene.");
        string before = w.Query("SELECT selected_xml FROM content_definition_resolutions WHERE aurora_id='ID_SHARED'");

        // Insert and move unrelated entries in both files. Absolute declaration ordinals change,
        // but the competing definitions and their within-ID order are untouched.
        w.Write("user/a.xml", Element("ID_INSERTED", "Added") + Element("ID_OTHER", "Edited") + Element("ID_SHARED", "Earlier"));
        w.Write("user/z.xml", Element("ID_WINNER_OTHER", "AlsoEdited") + Element("ID_SHARED", "Winner"));
        Require(w.Project("ID_SHARED", true).Contains("Winner"), "An unrelated edit preserves the existing duplicate choice.");
        Require(w.Project("ID_OTHER", true).Contains("Edited") && w.Project("ID_WINNER_OTHER", true).Contains("AlsoEdited")
            && w.Project("ID_INSERTED", true).Contains("Added"), "New and edited unrelated definitions are still read.");
        using (var c = w.Open())
        {
            var files = RuntimeContentFiles.Read(c, w.Root, []);
            Require(PreparedCatalogReader.Read(c, runtimeFiles: files).Elements.Single(e => e.AuroraId == "ID_SHARED").Xml.Contains("Winner"),
                "The default user-overlay read also preserves the decision.");
            Require(!PreparedCatalogReader.InputsMatch(c, [w.Root]), "Declaration matching must not claim the database itself is fresh.");
        }
        Require(w.Query("SELECT selected_xml FROM content_definition_resolutions WHERE aurora_id='ID_SHARED'") == before
            && w.Query("SELECT COUNT(*) FROM content_skipped_files WHERE kind='definition-collision'") == "1",
            "Runtime reads neither rewrite the saved decision nor dismiss the review issue.");
        var refreshed = w.Import();
        Require(refreshed.Skipped.Any(s => s.Kind == "definition-collision")
            && w.Query("SELECT kind FROM content_definition_resolutions WHERE aurora_id='ID_SHARED'") == "provisional",
            "Repeated import keeps the unresolved disagreement flagged even when one version works.");
    }

    internal static void RuntimeChangedCandidatesRequireReview()
    {
        foreach (string change in new[] { "winner", "rejected", "new supplier" })
        {
            using var w = new Workspace();
            w.Write("user/a.xml", Element("ID_SHARED", "Earlier") + Element("ID_UNRELATED_CONFLICT", "EarlierOther"));
            w.Write("user/z.xml", Element("ID_SHARED", "Winner") + Element("ID_UNRELATED_CONFLICT", "WinnerOther"));
            w.Import();
            if (change == "winner")
                w.Write("user/z.xml", Element("ID_SHARED", "WinnerEdited") + Element("ID_UNRELATED_CONFLICT", "WinnerOther"));
            else if (change == "rejected")
                w.Write("user/a.xml", Element("ID_SHARED", "EarlierEdited") + Element("ID_UNRELATED_CONFLICT", "EarlierOther"));
            else
                w.Write("user/zz.xml", Element("ID_SHARED", "NewCompetitor"));
            using (var c = w.Open())
            {
                var files = RuntimeContentFiles.Read(c, w.Root, []);
                var candidates = files.SelectMany(f => XElement.Parse(f.Xml).Elements("element")).ToArray();
                Require(candidates.Count(e => (string?)e.Attribute("id") == "ID_SHARED") >= 2,
                    "A changed " + change + " invalidates all stale exclusions for that ID.");
                Require(candidates.Count(e => (string?)e.Attribute("id") == "ID_UNRELATED_CONFLICT") == 1,
                    "Another ID's unchanged resolution survives in those same files.");
                try { PreparedCatalogReader.Read(c, runtimeFiles: files); throw new Exception("Changed conflicting definitions were silently accepted."); }
                catch (InvalidDataException ex)
                {
                    Require(ex.Message.Contains("ID_SHARED") && ex.Message.Contains("a.xml") && ex.Message.Contains("z.xml")
                        && ex.Message.Contains("Refresh the database"), "The runtime conflict names its suppliers and the next action.");
                }
            }
            var refreshed = w.Import();
            Require(w.Project("ID_SHARED", true).Contains(change == "new supplier" ? "NewCompetitor" : change == "winner" ? "WinnerEdited" : "Winner"),
                "A successful import reevaluates the changed ID with current load order.");
            Require(refreshed.Skipped.Any(s => s.Kind == "definition-collision"), "The usable choice still requires conflict review.");
        }
    }

    internal static void RuntimeSameIdOrderAndCountsMatter()
    {
        using var w = new Workspace();
        w.Write("user/a.xml", Element("ID_SHARED", "First") + Element("ID_SHARED", "Second"));
        w.Import();
        w.Write("user/a.xml", Element("ID_UNRELATED", "New") + Element("ID_SHARED", "First") + Element("ID_SHARED", "Second"));
        Require(w.Project("ID_SHARED", true).Contains("Second"), "Inserting a different ID does not change a within-file duplicate decision.");
        foreach (string competing in new[] {
            Element("ID_SHARED", "Second") + Element("ID_SHARED", "First"),
            Element("ID_SHARED", "First") + Element("ID_SHARED", "Second") + Element("ID_SHARED", "First") })
        {
            w.Write("user/a.xml", competing);
            try { w.Project("ID_SHARED", true); throw new Exception("A changed within-ID order or count reused a stale decision."); }
            catch (InvalidDataException ex) { Require(ex.Message.Contains("Conflicting runtime definitions"), "Expected a duplicate-review diagnostic."); }
        }
        w.Write("user/a.xml", Element("ID_SHARED", "First"));
        Require(w.Project("ID_SHARED", true).Contains("First"), "Removing the winner releases an unchanged rejected copy instead of hiding the remaining definition.");
        w.Write("user/a.xml", Element("ID_SHARED", "Second") + Element("ID_SHARED", "Second"));
        Require(w.Project("ID_SHARED", true).Contains("Second"), "Definitions repaired to agree can consolidate immediately.");
    }

    internal static void RetentionAndRepair()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_SHARED", "Working"));
        w.Write("core/append.xml", "<append id='ID_SHARED'><supports>Old extension</supports></append>");
        w.Import();
        // Reproduce the earlier prepared database contract, without the new decision tables.
        w.Query("UPDATE database_metadata SET data_version=13; DROP TABLE content_definition_resolutions; DROP TABLE content_definition_suppliers; DROP TABLE content_rejected_declarations;");
        string previous = w.Query("SELECT effective_xml FROM content_prepared_elements WHERE aurora_id='ID_SHARED'");
        // Retention answers only for an unreadable supplier now, so break the owner instead of
        // editing it: a disagreement between readable declarations is settled by load order, not
        // by whatever the database happened to import first.
        File.WriteAllText(Path.Combine(w.Root, "core/a.xml"), "<elements><broken");
        w.Write("core/b.xml", Element("ID_SHARED", "Other") + Element("ID_NEW", "Unaffected"));
        w.Write("core/append.xml", "<append id='ID_SHARED'><supports>New extension</supports></append>");
        w.Import();
        Require(w.Query("SELECT effective_xml FROM content_prepared_elements WHERE aurora_id='ID_SHARED'") == previous, "Keep the exact previously effective definition, not just its name or base XML.");
        Require(w.Query("SELECT data_version FROM database_metadata") == ContentDatabaseReader.CurrentDataVersion.ToString(), "Refresh migrates the old prepared database to the current reader contract.");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_NEW'") == "Unaffected", "Unrelated updates continue.");
        foreach (bool runtime in new[] { false, true })
        {
            string xml = w.Project("ID_SHARED", runtime);
            Require(xml.Contains("Working") && xml.Contains("Old extension") && !xml.Contains("New extension"), "SQL and runtime projections preserve mechanics without replaying new/doubled appends.");
        }
        w.Import();
        Require(w.Project("ID_SHARED", true).Contains("Working"), "Retention survives repeated import and fresh readers.");
        // Repairing means the owner is readable again and nothing competes for the id.
        w.Write("core/a.xml", Element("ID_SHARED", "Changed"));
        w.Write("core/b.xml", Element("ID_NEW", "Unaffected"));
        var repaired = w.Import();
        Require(w.Project("ID_SHARED", true).Contains("Changed") && w.Project("ID_SHARED", true).Contains("New extension"), "Repair releases retention and reapplies current appends once.");
        Require(repaired.Skipped.Count == 0 && w.Query("SELECT COUNT(*) FROM content_definition_resolutions") == "0", "Repair clears outstanding decisions.");
    }

    internal static void UpstreamAuthority()
    {
        using var w = new Workspace();
        w.Write("aggregate/a.xml", Origin("aurorabuilder") + Element("ID_SHARED", "Archived") + Element("ID_ONLY_OLD", "Unique"));
        w.Import(false);
        w.Write("aggregate/z.xml", Origin("AuroraLegacy") + Element("ID_SHARED", "Maintained"));
        var updated = w.Import(false);
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_SHARED' AND declaration_status='effective'") == "Maintained", "An established successor updates an existing canonical definition even in strict mode.");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_ONLY_OLD'") == "Unique", "Distinct archived content survives.");
        Require(updated.Skipped.Single().Kind == "superseded-definition", "Authority decisions are distinguished from unresolved collisions.");
        Require(w.Project("ID_SHARED", true).Contains("Maintained"), "Raw primary reads cannot resurrect the older definition.");
        w.Write("aggregate/z.xml", Origin("AuroraLegacy") + Element("ID_SHARED", "UpdatedAgain"));
        w.Import();
        Require(w.Project("ID_SHARED").Contains("UpdatedAgain"), "New valid upstream revisions replace earlier accepted revisions.");
        w.Write("aggregate/other.xml", Element("ID_SHARED", "Independent"));
        w.Import();
        Require(w.Project("ID_SHARED").Contains("UpdatedAgain") && w.Query("SELECT kind FROM content_definition_resolutions WHERE aurora_id='ID_SHARED'") == "provisional", "Successor authority does not silently settle a collision with an unrelated supplier.");
    }

    internal static void UnreadableAlternative()
    {
        using var w = new Workspace();
        // z-owner loads after a-rival within the same bucket, so it owns the id; breaking the owner
        // is what puts retention to the test. Breaking the rival would prove nothing, because the
        // rival never supplied the effective definition.
        w.Write("core/z-owner.xml", Element("ID_SHARED", "Working")); w.Import();
        w.Write("core/a-rival.xml", Element("ID_SHARED", "Alternative")); w.Import();
        File.WriteAllText(Path.Combine(w.Root, "core/z-owner.xml"), "<elements><broken");
        w.Write("core/a-rival.xml", Element("ID_SHARED", "AlternativeChanged") + Element("ID_NEW", "New"));
        var result = w.Import();
        Require(w.Project("ID_SHARED", true).Contains("Working"), "An unreadable prior supplier does not promote the competing definition.");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_NEW'") == "New" && result.Skipped.Any(s => s.Kind == "unreadable"), "The rest imports and the broken file is reported.");
        w.Import();
        Require(w.Project("ID_SHARED", true).Contains("Working"), "Repeated refresh remembers unreadable suppliers.");
        File.WriteAllText(Path.Combine(w.Root, "core/a-rival.xml"), "<elements><broken");
        w.Import(); w.Import();
        Require(w.Project("ID_SHARED", true).Contains("Working") && w.Project("ID_NEW", true).Contains("New"), "When every supplier is unreadable, cached definitions survive repeated refreshes.");
    }

    /// <summary>
    /// A correction is staged as the file it corrects, not as the user/local file it lives in, so it
    /// competes for an id where that upstream file sits in the load order rather than after every
    /// content pack. Only ids the correction adds, which have no upstream to stand in for, keep local
    /// precedence.
    ///
    /// That is what stops a correction from reaching past its own file: a correction file is a whole
    /// copy of its upstream, so most of what it holds is an incidental copy rather than an authored
    /// change - measured on real content, 42 marked corrections against 259 incidental copies. If
    /// those copies were attributed to the correction file they would sit above every pack and revert
    /// a book's deliberate override of an id the correction never touched. Here a correction to core
    /// marks only ID_MARKED while a book revises ID_COPIED, which the correction merely carries along.
    /// </summary>
    internal static void IncidentalCopiesDoNotOutrankPacks()
    {
        using var w = new Workspace();
        string baseline = "<elements>" + Element("ID_MARKED", "Old") + Element("ID_COPIED", "Upstream") + Element("ID_UNRELATED", "Unchanged") + "</elements>";
        string corrected = baseline.Replace("'Old'", "'Fixed'");
        w.WriteRaw("core/features.xml", baseline);
        w.WriteRaw("user/local/fix.xml", LocalCorrectionDocument.Create(corrected, baseline, "core/features.xml",
            [new LocalCorrection("fix", "replace", "ID_MARKED", null, null, "review-pending")]));
        w.Write("the-book-of-xellarant/revised.xml", Element("ID_COPIED", "Revised"));
        w.Import();

        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_COPIED'"
            + " AND declaration_status='effective'") == "Revised",
            "A book's override must survive an unmarked copy of the same id inside a correction file.");
        Require(w.Query("SELECT name FROM elements WHERE aurora_id='ID_MARKED'"
            + " AND declaration_status='effective'") == "Fixed",
            "The id the correction does mark still takes effect.");

        // Attribution has to follow the same rule, because source filtering keys off it.
        string credited = w.Query("SELECT f.relative_path FROM resolved_elements_cache r"
            + " JOIN elements e ON e.element_id=r.winning_element_id"
            + " JOIN source_files f ON f.source_file_id=e.source_file_id"
            + " WHERE r.aurora_id='ID_COPIED'");
        Require(credited.Contains("xellarant") && credited.EndsWith("revised.xml"),
            "The book that won must be credited with the id, not the correction file: " + credited);
        Require(w.Project("ID_COPIED", true).Contains("Revised"), "The runtime read agrees.");
        w.WriteRaw("core/features.xml", baseline.Replace("Unchanged", "Updated companion"));
        Require(w.Project("ID_COPIED", true).Contains("Revised") && w.Project("ID_MARKED", true).Contains("Fixed")
            && w.Project("ID_UNRELATED", true).Contains("Updated companion"),
            "An unrelated upstream edit follows the correction contract without resurrecting the overridden incidental copy.");
    }

    /// <summary>
    /// Content that renames an element can leave a forwarding address so characters saved against
    /// the old id keep working. An alias is only honoured when it forwards a dead id to a live one:
    /// forwarding an id something still declares would let a book capture an identity it does not
    /// own, and forwarding to nothing would record a promise the catalog cannot keep.
    /// </summary>
    internal static void AliasesForwardOnlyDeadIds()
    {
        using var w = new Workspace();
        w.Write("core/a.xml", Element("ID_LIVE", "Live") + Element("ID_TARGET", "Target"));
        w.WriteRaw("core/aliases.xml", "<elements>"
            + "<alias id='ID_RETIRED' target='ID_TARGET' />"          // the one good case
            + "<alias id='ID_LIVE' target='ID_TARGET' />"             // still declared
            + "<alias id='ID_GONE' target='ID_MISSING' />"            // target declared nowhere
            + "<alias id='ID_SELF' target='ID_SELF' />"               // points at itself
            + "<alias id='ID_BLANK' />"                               // no target
            + "</elements>");
        var result = w.Import();

        Require(w.Query("SELECT target_aurora_id FROM content_element_aliases WHERE saved_aurora_id='ID_RETIRED'") == "ID_TARGET",
            "A dead id forwarding to a live one is kept.");
        Require(w.Query("SELECT COUNT(*) FROM content_element_aliases") == "1",
            "Only that one is kept: " + w.Query("SELECT group_concat(saved_aurora_id) FROM content_element_aliases"));
        Require(result.Skipped.Count(s => s.Kind == "alias") == 4,
            "Every rejected alias is reported rather than dropped silently.");
        Require(w.Query("SELECT COUNT(*) FROM elements WHERE aurora_id='ID_RETIRED'") == "0",
            "An alias is a forwarding address, not a declaration.");
        Require(w.Query("SELECT COUNT(*) FROM content_element_aliases WHERE origin='curated'") == "0",
            "A curated entry whose target nothing declares does not apply.");
    }

    /// <summary>
    /// Some ids only ever existed in saved characters - a misspelling, or a build of upstream content
    /// that is gone. Nothing can declare an alias from content that never had the id, so the library
    /// carries those itself. They are held to the same rule: applicable only when the target is here.
    /// </summary>
    internal static void CuratedAliasesApplyWhenTheirTargetExists()
    {
        const string laser = "ID_WOTC_DMG_PROFICIENCY_WEAPON_FUTURISTIC_FIREARMS_LASER_PISTOL";
        const string mistyped = "ID_WOTC_DMG_PROFICIENCY_WEAPON_FUTURISTIC_FIREARMS_LASTER_PISTOL";
        using (var without = new Workspace())
        {
            without.Write("core/a.xml", Element("ID_UNRELATED", "Unrelated"));
            without.Import();
            Require(without.Query("SELECT COUNT(*) FROM content_element_aliases") == "0",
                "Without the proficiency installed the curated entry stays out of the catalog.");
        }
        using var w = new Workspace();
        w.Write("core/a.xml", Element(laser, "Weapon Proficiency (Laser Pistol)"));
        w.Import();
        Require(w.Query("SELECT target_aurora_id FROM content_element_aliases WHERE saved_aurora_id='" + mistyped + "'") == laser,
            "The misspelling forwards to the proficiency the catalog actually spells.");
        Require(w.Query("SELECT origin FROM content_element_aliases WHERE saved_aurora_id='" + mistyped + "'") == "curated",
            "Its origin says the library supplied it, not the content.");
    }

    internal static void AggregateClassification()
    {
        using var w = new Workspace();
        string Source(string flag) => $"<element id='ID_DMG_SOURCE' name='DMG' type='Source' source='Core'><setters><set name='official'>true</set><set name='core'>{flag}</set></setters></element>";
        w.Write("aurora-sources/old/source.xml", Origin("aurorabuilder") + Source("true"));
        w.Write("aurora-sources/new/source.xml", Origin("AuroraLegacy") + Source("false"));
        w.Write("aurora-sources/old/oathbreaker.xml", Element("ID_OATH", "Oathbreaker").Replace("source='Test'", "source='DMG'"));
        w.Write("aurora-sources/third/source.xml", "<element id='ID_THIRD' name='Third' type='Source' source='Core'><setters><set name='third-party'>true</set></setters></element>");
        w.Write("aurora-sources/home/source.xml", "<element id='ID_HOME' name='Home' type='Source' source='Core'><setters><set name='homebrew'>true</set></setters></element>");
        w.Write("aurora-sources/missing/source.xml", Element("ID_MISSING_SOURCE", "Missing metadata"));
        w.Import();
        string Kind(string id) => w.Query($"SELECT cp.package_kind FROM elements e JOIN source_files f ON f.source_file_id=e.source_file_id JOIN content_packages cp ON cp.content_package_id=f.content_package_id WHERE e.aurora_id='{id}'");
        Require(Kind("ID_OATH") == "official" && Kind("ID_THIRD") == "third-party" && Kind("ID_HOME") == "homebrew", "Aggregate siblings do not contaminate publisher classifications.");
        Require(Kind("ID_MISSING_SOURCE") == "local", "Missing evidence does not invent a publisher or abort unaffected content.");
        w.Write("aurora-sources/bad/source.xml", "<element id='ID_BAD_FLAGS' name='Ambiguous' type='Source' source='Core'><setters><set name='official'>true</set><set name='homebrew'>true</set></setters></element>");
        var result = w.Import();
        Require(Kind("ID_BAD_FLAGS") == "local" && result.Skipped.Any(s => s.Kind == "classification"), "Skip mode reports contradictory publisher flags without dropping game content.");
        bool refused = false; try { w.Import(false); } catch (InvalidDataException) { refused = true; }
        Require(refused, "Strict mode still rejects explicit contradictory publisher flags.");
    }
}
