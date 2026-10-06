using System.Xml;
using System.Xml.Linq;
using Aurora.Content.Contracts;

internal static class CorrectionReplacementTests
{
    private static readonly XNamespace MetadataNamespace = LocalCorrectionDocument.Namespace;

    private static string Definition(string id, string name, string description) =>
        new XElement("element", new XAttribute("id", id), new XAttribute("name", name),
            new XAttribute("type", "Feat"), new XAttribute("source", "Fixture"),
            new XElement("description", new XElement("p", description))).ToString(SaveOptions.DisableFormatting);

    private static string Document(params string[] definitions) =>
        new XElement("elements", definitions.Select(XElement.Parse)).ToString(SaveOptions.DisableFormatting);

    private static string Fingerprint(string definition) => LocalCorrectionDocument.Fingerprint(XElement.Parse(definition));

    private static XElement Element(string xml, string id) => LocalCorrectionDocument.Parse(xml).Root!
        .Elements("element").Single(e => (string?)e.Attribute("id") == id);

    private static XElement Metadata(string xml) => LocalCorrectionDocument.Parse(xml).Root!.Element(MetadataNamespace + "corrections")!;

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Reject(Action action, string context)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception($"Expected correction validation to reject {context}.");
    }

    private sealed record Fixture(string Local, string Baseline, string Upstream);

    private static Fixture CreateFixture()
    {
        string original = Definition("ID_REPLACE", "Original", "Original description");
        string renamed = Definition("ID_RENAME", "Rename original", "Rename original description");
        string removed = Definition("ID_REMOVE", "Removed", "Removed description");
        string companion = Definition("ID_COMPANION", "Companion", "Companion baseline");
        string baseline = Document(original, renamed, removed, companion);
        string local = LocalCorrectionDocument.Create(Document(
            Definition("ID_REPLACE", "Local replace", "Local replace description"),
            Definition("ID_RENAMED", "Local rename", "Local rename description"),
            Definition("ID_ADDED", "Local add", "Local add description"), companion), baseline, "core/fixture.xml",
            [
                new("replace-key", "replace", "ID_REPLACE", null, Fingerprint(original), Reason: "Repair existing definition"),
                new("rename-key", "rename", "ID_RENAME", "ID_RENAMED", Fingerprint(renamed), Reason: "Keep the distinct identity"),
                new("add-key", "add", "ID_ADDED", null, null, Reason: "Add a local definition"),
                new("remove-key", "remove", "ID_REMOVE", null, Fingerprint(removed), Reason: "Suppress obsolete definition")
            ]);
        return new(local, baseline, Document(original, renamed, removed,
            Definition("ID_COMPANION", "Updated companion", "Authoritative companion update")));
    }

    internal static void ReplaceRenameAndAddPreserveIntent()
    {
        var fixture = CreateFixture();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["replace-key"] = Definition("ID_REPLACE", "Amended replace", "New replace description"),
            ["rename-key"] = Definition("ID_RENAMED", "Amended rename", "New rename description"),
            ["add-key"] = Definition("ID_ADDED", "Amended add", "New add description")
        };
        var result = LocalCorrectionDocument.ReplaceDefinitions(fixture.Local, fixture.Upstream, replacements);
        foreach (var (key, definition) in replacements)
        {
            string id = (string)XElement.Parse(definition).Attribute("id")!;
            Require(XNode.DeepEquals(XElement.Parse(definition), Element(result.LocalXml, id)),
                $"{key} must replace the complete local definition, including its description.");
            Require(XNode.DeepEquals(XElement.Parse(definition), Element(result.EffectiveXml, id)),
                $"{key} must use the amended definition in effective content.");
        }
        Require(result.BaselineXml == fixture.Baseline && result.SourcePath == "core/fixture.xml"
            && result.UpstreamXml == fixture.Upstream && XNode.DeepEquals(Metadata(fixture.Local), Metadata(result.LocalXml)),
            "Replacing content must preserve the embedded baseline, origin, fingerprints, operations and reasons.");
        Require(Element(result.LocalXml, "ID_COMPANION").Element("description")!.Value == "Companion baseline",
            "An unrelated incidental local copy must not be rewritten by this operation.");
        Require(Element(result.EffectiveXml, "ID_COMPANION").Element("description")!.Value == "Authoritative companion update",
            "Unchanged companion definitions must continue following current authoritative XML.");
        TestAssert.Sequence(["ID_RENAME", "ID_REMOVE"], result.SuppressedIds);
        Require(!result.CanRetire && result.Corrections.All(c => c.State == "review-pending"),
            "Amended corrections must remain protected for review.");
    }

    internal static void SelectedGroupsReopenTogether()
    {
        string original = Definition("ID_SELECTED", "Original", "Original");
        string removed = Definition("ID_GROUP_PEER", "Peer", "Peer");
        string other = Definition("ID_UNTOUCHED", "Other", "Other original");
        string selected = Definition("ID_SELECTED", "Accepted selected", "Accepted selected");
        string acceptedOther = Definition("ID_UNTOUCHED", "Accepted other", "Accepted other");
        string acceptedAdd = Definition("ID_UNTOUCHED_ADD", "Accepted add", "Accepted add");
        string baseline = Document(original, removed, other);
        string upstream = Document(selected, acceptedOther, acceptedAdd);
        string local = LocalCorrectionDocument.Create(upstream, baseline, "core/groups.xml",
            [
                new("selected", "replace", "ID_SELECTED", null, Fingerprint(original), "accepted-upstream", "selected-group", "First repair"),
                new("peer", "remove", "ID_GROUP_PEER", null, Fingerprint(removed), "accepted-upstream", "selected-group", "Linked removal"),
                new("untouched", "replace", "ID_UNTOUCHED", null, Fingerprint(other), "accepted-upstream", "other-group", "Other repair"),
                new("untouched-add", "add", "ID_UNTOUCHED_ADD", null, null, "accepted-upstream", "other-group", "Other addition")
            ]);
        Require(LocalCorrectionDocument.Evaluate(local, upstream).CanRetire, "The fixture starts fully accepted and incorporated.");
        var result = LocalCorrectionDocument.ReplaceDefinitions(local, upstream,
            new Dictionary<string, string> { ["selected"] = Definition("ID_SELECTED", "New review", "New local edit") });
        Require(result.Corrections.Where(c => c.Group == "selected-group").All(c => c.State == "review-pending"),
            "Changing one accepted group member must reopen every peer, including a removal.");
        Require(result.Corrections.Where(c => c.Group == "other-group").All(c => c.State == "accepted-upstream"),
            "Unselected groups must retain acceptance.");
        var expectedMetadata = new XElement(Metadata(local));
        foreach (var correction in expectedMetadata.Elements(MetadataNamespace + "correction")
            .Where(c => (string?)c.Attribute("group") == "selected-group"))
            correction.SetAttributeValue("state", "review-pending");
        Require(XNode.DeepEquals(expectedMetadata, Metadata(result.LocalXml)),
            "Reopening review must change only the selected group's state metadata.");
        Require(!result.CanRetire && Element(result.EffectiveXml, "ID_SELECTED").Element("description")!.Value == "New local edit",
            "A newly edited definition cannot stay accepted or retire automatically.");
        Require(XNode.DeepEquals(Element(local, "ID_UNTOUCHED"), Element(result.LocalXml, "ID_UNTOUCHED")),
            "Unselected definitions must remain intact.");
    }

    internal static void InvalidRequestsAreRejected()
    {
        var fixture = CreateFixture();
        void Replace(IReadOnlyDictionary<string, string> replacements) =>
            LocalCorrectionDocument.ReplaceDefinitions(fixture.Local, fixture.Upstream, replacements);
        string valid = Definition("ID_REPLACE", "Valid", "Valid description");
        Reject(() => Replace(new Dictionary<string, string>()), "an empty replacement batch");
        foreach (string key in new[] { "", " ", "unknown-key", "REPLACE-KEY" })
            Reject(() => Replace(new Dictionary<string, string> { [key] = valid }), $"unknown or empty exact key '{key}'");
        Reject(() => Replace(new Dictionary<string, string> { ["remove-key"] = Definition("ID_REMOVE", "Cannot replace removal", "Text") }),
            "a remove operation selected for definition replacement");
        Reject(() => Replace(new Dictionary<string, string> { ["replace-key"] = Definition("ID_DIFFERENT", "Changed identity", "Text") }),
            "a changed replace identity");
        Reject(() => Replace(new Dictionary<string, string> { ["rename-key"] = Definition("ID_RENAME", "Old identity", "Text") }),
            "a rename replacement reverting to the original target ID");
        Reject(() => Replace(new Dictionary<string, string> { ["add-key"] = Definition("ID_DIFFERENT", "Changed add", "Text") }),
            "a changed addition identity");
        var missing = LocalCorrectionDocument.Parse(fixture.Local);
        missing.Root!.Elements("element").Single(e => (string?)e.Attribute("id") == "ID_REPLACE").Remove();
        Reject(() => LocalCorrectionDocument.ReplaceDefinitions(missing.ToString(SaveOptions.DisableFormatting), fixture.Upstream,
            new Dictionary<string, string> { ["replace-key"] = valid }), "a missing existing local definition");
        var ambiguous = LocalCorrectionDocument.Parse(fixture.Local);
        ambiguous.Root!.Add(new XElement(Element(fixture.Local, "ID_REPLACE")));
        Reject(() => LocalCorrectionDocument.ReplaceDefinitions(ambiguous.ToString(SaveOptions.DisableFormatting), fixture.Upstream,
            new Dictionary<string, string> { ["replace-key"] = valid }), "ambiguous existing local definitions");

        foreach (string invalid in new[]
        {
            "<elements/>", "<element xmlns='urn:unexpected' id='ID_REPLACE' name='Name' type='Feat'/>",
            "<element name='Name' type='Feat'/>", "<element id='ID_REPLACE' type='Feat'/>",
            "<element id='ID_REPLACE' name='Name'/>", "<element id='ID_REPLACE' name=' ' type='Feat'/>",
            $"<element id='ID_REPLACE' name='Name' type='Feat'><al:corrections xmlns:al='{LocalCorrectionDocument.Namespace}'/></element>",
            $"<element xmlns:al='{LocalCorrectionDocument.Namespace}' al:state='accepted-upstream' id='ID_REPLACE' name='Name' type='Feat'/>"
        })
            Reject(() => Replace(new Dictionary<string, string> { ["replace-key"] = invalid }), "an invalid declaration payload");
        foreach (string malformed in new[] { "", "<element", valid + valid })
        {
            try { Replace(new Dictionary<string, string> { ["replace-key"] = malformed }); }
            catch (XmlException) { continue; }
            throw new Exception("Malformed replacement XML must be rejected by the XML parser.");
        }
        Reject(() => Replace(new Dictionary<string, string>
        {
            ["replace-key"] = valid, ["rename-key"] = Definition("ID_CHANGED", "Invalid second replacement", "Text")
        }), "a batch containing an invalid second replacement");
        Require(LocalCorrectionDocument.Evaluate(fixture.Local, fixture.Upstream).LocalXml == fixture.Local,
            "Rejected pure operations leave the original correction document available unchanged.");
    }

    internal static void ClaimedAdditionCanBeAmended()
    {
        string companion = Definition("ID_COMPANION", "Companion", "Original companion");
        string oldAddition = Definition("ID_ADDED", "Local addition", "Old local content");
        string published = Definition("ID_ADDED", "Published addition", "New authoritative content");
        string baseline = Document(companion);
        string local = LocalCorrectionDocument.Create(Document(companion, oldAddition), baseline, "core/addition.xml",
            [new("addition", "add", "ID_ADDED", null, null, Reason: "Originally local content")]);
        string upstream = Document(companion, published);
        Reject(() => LocalCorrectionDocument.Evaluate(local, upstream), "the original conflicting local addition");
        var result = LocalCorrectionDocument.ReplaceDefinitions(local, upstream,
            new Dictionary<string, string> { ["addition"] = published });
        Require(XNode.DeepEquals(XElement.Parse(published), Element(result.LocalXml, "ID_ADDED"))
            && XNode.DeepEquals(XElement.Parse(published), Element(result.EffectiveXml, "ID_ADDED")),
            "A replacement matching the newly claimed authoritative ID must repair the addition.");
        Require(result.BaselineXml == baseline && XNode.DeepEquals(Metadata(local), Metadata(result.LocalXml)),
            "Repairing a collision must not manufacture a newer baseline or different original intent.");
        Require(result.ReviewReasons.Contains("addition: incorporated; review before clearing")
            && result.Corrections.Single().State == "review-pending" && !result.CanRetire,
            "Matching upstream content still requires review; it cannot automatically accept or retire the correction.");
    }
}
