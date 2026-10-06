using System.Xml.Linq;
using Aurora.Content.Contracts;

internal static class LocalApprovalContractTests
{
    private static readonly XNamespace Ns = LocalCorrectionDocument.Namespace;
    private const string Url = "https://example.invalid/approval.xml";

    private static string Definition(string id, string text) => new XElement("element",
        new XAttribute("id", id), new XAttribute("name", id), new XAttribute("type", "Feat"),
        new XElement("description", text)).ToString(SaveOptions.DisableFormatting);

    private static string Document(string version, params string[] definitions) => new XElement("elements",
        new XElement("info", new XElement("update", new XAttribute("version", version),
            new XElement("file", new XAttribute("url", Url)))), definitions.Select(XElement.Parse))
        .ToString(SaveOptions.DisableFormatting);

    private static XElement Element(XDocument document, string id) => document.Root!.Elements("element")
        .Single(e => (string?)e.Attribute("id") == id);
    private static XElement Metadata(XDocument document) => document.Root!.Element(Ns + "corrections")!;
    private static XElement Correction(XDocument document, string key) => Metadata(document)
        .Elements(Ns + "correction").Single(e => (string?)e.Attribute("key") == key);
    private static string Xml(XDocument document) => document.ToString(SaveOptions.DisableFormatting);
    private static string Fingerprint(string definition) => LocalCorrectionDocument.Fingerprint(XElement.Parse(definition));
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string description)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected rejection: " + description);
    }

    private static (string Local, string Upstream) Single()
    {
        string original = Definition("ID_FIREBALL", "Original fireball");
        string companion = Definition("ID_FLY", "Original fly");
        string baseline = Document("1.0", original, companion);
        return (LocalCorrectionDocument.Create(Document("1.0", Definition("ID_FIREBALL", "Local fireball"), companion),
            baseline, "core/approval.xml", [new("fireball", "replace", "ID_FIREBALL", null,
                Fingerprint(original), Reason: "Fix the definition")]), baseline);
    }

    internal static void ApprovedCorrectionsStayActiveWithoutRetirement()
    {
        var fixture = Single();
        var approved = LocalCorrectionDocument.ApproveLocal(fixture.Local, fixture.Upstream, ["fireball"]);
        var correction = approved.Corrections.Single();
        Require(correction.State == "approved-local" && correction.IsActive
            && correction.ApprovalFingerprint?.StartsWith("aurora-local-approval-v1:", StringComparison.Ordinal) == true,
            "Approval must be durable, versioned and active.");
        Require(!approved.CanRetire && approved.ReviewReasons.Count == 0 && approved.IncorporatedKeys.Count == 0,
            "Acknowledging a local fix clears its review request without accepting upstream or permitting retirement.");
        Require(Element(LocalCorrectionDocument.Parse(approved.EffectiveXml), "ID_FIREBALL").Value == "Local fireball",
            "Approved local content must remain effective.");
        var roundTrip = LocalCorrectionDocument.Evaluate(approved.LocalXml, fixture.Upstream);
        Require(roundTrip.Corrections.Single().State == "approved-local" && roundTrip.ReviewReasons.Count == 0,
            "Re-reading unchanged approved XML must retain approval.");
        var expected = LocalCorrectionDocument.Parse(fixture.Local);
        var actual = LocalCorrectionDocument.Parse(approved.LocalXml);
        Correction(actual, "fireball").Attribute("approval-fingerprint")!.Remove();
        Correction(actual, "fireball").SetAttributeValue("state", "review-pending");
        Require(XNode.DeepEquals(expected, actual) && approved.BaselineXml == fixture.Upstream,
            "Approval changes only review metadata, never baseline or local definitions.");
        // Adding approval metadata must not replace the existing positional constructor/deconstruction.
        var (key, operation, target, replacement, original, state, group, reason) = correction;
        Require(key == "fireball" && operation == "replace" && target == "ID_FIREBALL" && replacement == null
            && original != null && state == "approved-local" && group == null && reason == "Fix the definition",
            "Existing correction deconstruction remains available.");
    }

    internal static void UnrelatedChangesPreserveApproval()
    {
        var fixture = Single();
        var approved = LocalCorrectionDocument.ApproveLocal(fixture.Local, fixture.Upstream, ["fireball"]);
        var upstream = LocalCorrectionDocument.Parse(fixture.Upstream);
        Element(upstream, "ID_FLY").Element("description")!.Value = "New published fly";
        upstream.Root!.Element("info")!.Element("update")!.SetAttributeValue("version", "2.0");
        var evaluated = LocalCorrectionDocument.Evaluate(approved.LocalXml, Xml(upstream));
        Require(evaluated.Corrections.Single().State == "approved-local" && evaluated.ReviewReasons.Count == 0,
            "An unrelated declaration edit and a newer publication version must not invalidate Fireball approval.");
        Require(Element(LocalCorrectionDocument.Parse(evaluated.EffectiveXml), "ID_FLY").Value == "New published fly",
            "An unchanged companion still follows authoritative updates.");

        var local = LocalCorrectionDocument.Parse(approved.LocalXml);
        Element(local, "ID_FLY").Element("description")!.Value = "Unclassified local fly";
        local.Root!.Element("info")!.Element("update")!.SetAttributeValue("version", "3.0");
        var unrelatedLocal = LocalCorrectionDocument.Evaluate(Xml(local), Xml(upstream));
        Require(unrelatedLocal.Corrections.Single().State == "approved-local"
            && unrelatedLocal.ReviewReasons.Any(r => r.Contains("ID_FLY: unclassified", StringComparison.Ordinal)),
            "Unrelated local edits keep their own review diagnostic without revoking the approved correction.");
    }

    internal static void RelevantChangesReopenWithoutRewriting()
    {
        var fixture = Single();
        var approved = LocalCorrectionDocument.ApproveLocal(fixture.Local, fixture.Upstream, ["fireball"]);
        Action<XDocument, XDocument>[] changes =
        [
            (local, _) => Element(local, "ID_FIREBALL").Element("description")!.Value = "Changed local fix",
            (_, upstream) => Element(upstream, "ID_FIREBALL").Element("description")!.Value = "Changed upstream definition",
            (_, upstream) => Element(upstream, "ID_FIREBALL").Remove(),
            (local, _) => Correction(local, "fireball").Element(Ns + "reason")!.Value = "Changed intent",
            (local, _) => Correction(local, "fireball").SetAttributeValue("group", "new-group"),
            (local, _) => Metadata(local).SetAttributeValue("source-path", "core/other-origin.xml"),
            (local, _) => local.Root!.SetAttributeValue("review-flag", "changed"),
            (local, _) => local.Root!.Element("info")!.Element("update")!.Element("file")!.SetAttributeValue("url", "https://example.invalid/changed.xml"),
            (local, _) => Correction(local, "fireball").Attribute("approval-fingerprint")!.Remove(),
            (local, _) => Correction(local, "fireball").SetAttributeValue("approval-fingerprint", "future-algorithm:unverified")
        ];
        foreach (var change in changes)
        {
            var local = LocalCorrectionDocument.Parse(approved.LocalXml);
            var upstream = LocalCorrectionDocument.Parse(fixture.Upstream);
            change(local, upstream);
            string currentLocal = Xml(local);
            var result = LocalCorrectionDocument.Evaluate(currentLocal, Xml(upstream));
            Require(result.Corrections.Single().State == "review-pending" && result.Corrections.Single().IsActive
                && !result.CanRetire && result.ReviewReasons.Any(r => r.Contains("local approval", StringComparison.Ordinal)),
                "Changed relevant content or missing approval evidence must reopen review while preserving protection.");
            Require(result.LocalXml == currentLocal && Correction(LocalCorrectionDocument.Parse(result.LocalXml), "fireball")
                .Attribute("state")!.Value == "approved-local", "Evaluation must report stale approval without rewriting authored XML.");
        }
        var unknown = LocalCorrectionDocument.Parse(approved.LocalXml);
        Correction(unknown, "fireball").SetAttributeValue("state", "future-state");
        Reject(() => LocalCorrectionDocument.Evaluate(Xml(unknown), fixture.Upstream), "unknown correction state");
    }

    internal static void GroupsRequireCompleteApprovalAndReopenTogether()
    {
        string first = Definition("ID_FIRST", "Original first");
        string removed = Definition("ID_REMOVED", "Original removed");
        string baseline = Document("1.0", first, removed);
        string local = LocalCorrectionDocument.Create(Document("1.0", Definition("ID_FIRST", "Repaired first")),
            baseline, "core/group.xml",
            [new("first", "replace", "ID_FIRST", null, Fingerprint(first), Group: "linked"),
             new("remove", "remove", "ID_REMOVED", null, Fingerprint(removed), Group: "linked")]);
        foreach (string[] keys in new[] { Array.Empty<string>(), new[] { "first" }, new[] { "first", "first" },
            new[] { "first", "missing" }, new[] { "FIRST", "remove" } })
        {
            Reject(() => LocalCorrectionDocument.ApproveLocal(local, baseline, keys), "incomplete or invalid local approval selection");
            Reject(() => LocalCorrectionDocument.AcceptUpstream(local, baseline, keys), "incomplete or invalid upstream acceptance selection");
        }
        var approved = LocalCorrectionDocument.ApproveLocal(local, baseline, ["first", "remove"]);
        Require(approved.Corrections.All(c => c.State == "approved-local")
            && approved.Corrections.Select(c => c.ApprovalFingerprint).Distinct().Count() == 1,
            "Every linked member must share the same approval evidence.");
        var changed = LocalCorrectionDocument.Parse(approved.LocalXml);
        Element(changed, "ID_FIRST").Element("description")!.Value = "Changed member";
        var stale = LocalCorrectionDocument.Evaluate(Xml(changed), baseline);
        Require(stale.Corrections.All(c => c.State == "review-pending"), "One changed member reopens its entire group.");

        var expanded = LocalCorrectionDocument.Parse(approved.LocalXml);
        expanded.Root!.Add(XElement.Parse(Definition("ID_ADDED", "New group member")));
        Metadata(expanded).Add(new XElement(Ns + "correction", new XAttribute("key", "added"),
            new XAttribute("operation", "add"), new XAttribute("target-id", "ID_ADDED"),
            new XAttribute("state", "review-pending"), new XAttribute("group", "linked")));
        var expandedReview = LocalCorrectionDocument.Evaluate(Xml(expanded), baseline);
        Require(expandedReview.Corrections.Count == 3 && expandedReview.Corrections.All(c => c.State == "review-pending"),
            "A new group member cannot inherit approval; the whole expanded group needs review.");
        var amendedExpanded = LocalCorrectionDocument.ReplaceDefinitions(Xml(expanded), baseline,
            new Dictionary<string, string> { ["first"] = Definition("ID_FIRST", "Amended expanded group") });
        Require(amendedExpanded.Corrections.All(c => c.State == "review-pending" && c.ApprovalFingerprint == null),
            "A stale expanded group must remain amendable while all peers are reopened explicitly.");

        var replaced = LocalCorrectionDocument.ReplaceDefinitions(approved.LocalXml, baseline,
            new Dictionary<string, string> { ["first"] = Definition("ID_FIRST", "Amended approved member") });
        Require(replaced.Corrections.All(c => c.State == "review-pending" && c.ApprovalFingerprint == null)
            && Metadata(LocalCorrectionDocument.Parse(replaced.LocalXml)).Elements(Ns + "correction")
                .All(e => e.Attribute("approval-fingerprint") == null),
            "Amending an approved group explicitly clears every member's old approval stamp.");
    }

    internal static void RenameRemoveAndAddRemainProtectedUntilUpstreamAcceptance()
    {
        string renamed = Definition("ID_OLD", "Old name");
        string removed = Definition("ID_REMOVE", "Removed content");
        string replacement = Definition("ID_NEW", "Distinct renamed content");
        string addition = Definition("ID_ADD", "Local added content");
        string baseline = Document("1.0", renamed, removed);
        string local = LocalCorrectionDocument.Create(Document("1.0", replacement, addition), baseline, "core/operations.xml",
            [new("rename", "rename", "ID_OLD", "ID_NEW", Fingerprint(renamed)),
             new("remove", "remove", "ID_REMOVE", null, Fingerprint(removed)),
             new("add", "add", "ID_ADD", null, null)]);
        string[] keys = ["rename", "remove", "add"];
        var approved = LocalCorrectionDocument.ApproveLocal(local, baseline, keys);
        TestAssert.Sequence(["ID_NEW", "ID_ADD"], LocalCorrectionDocument.Parse(approved.EffectiveXml).Root!
            .Elements("element").Select(e => (string)e.Attribute("id")!).ToArray());
        Require(approved.Corrections.All(c => c.IsActive) && !approved.CanRetire && approved.ReviewReasons.Count == 0,
            "Every supported correction operation remains active after local approval.");
        string published = Document("2.0", replacement, addition);
        var incorporated = LocalCorrectionDocument.Evaluate(approved.LocalXml, published);
        TestAssert.Sequence(keys, incorporated.IncorporatedKeys);
        Require(incorporated.Corrections.All(c => c.State == "review-pending") && !incorporated.CanRetire,
            "Relevant upstream changes reopen review; matching content alone does not accept or retire anything.");
        var approvedAgain = LocalCorrectionDocument.ApproveLocal(approved.LocalXml, published, keys);
        Require(!approvedAgain.CanRetire && approvedAgain.Corrections.All(c => c.State == "approved-local"),
            "Keeping an incorporated correction locally remains a distinct choice from upstream acceptance.");
        var accepted = LocalCorrectionDocument.AcceptUpstream(approvedAgain.LocalXml, published, keys);
        Require(accepted.CanRetire && accepted.Corrections.All(c => c.State == "accepted-upstream"
            && !c.IsActive && c.ApprovalFingerprint == null), "Explicit upstream acceptance clears approval stamps and can allow retirement.");
        TestAssert.Sequence(keys, accepted.IncorporatedKeys);

        string occupied = Document("2.0", renamed, removed, Definition("ID_NEW", "Different published occupant"));
        Reject(() => LocalCorrectionDocument.Evaluate(approved.LocalXml, occupied),
            "approval masking an occupied rename destination");
        string claimed = Document("2.0", renamed, removed, Definition("ID_ADD", "Different published addition"));
        Reject(() => LocalCorrectionDocument.Evaluate(approved.LocalXml, claimed),
            "approval masking a conflicting upstream claim on a local addition");
    }

    internal static void IncorporationRequiresTheCompleteIntendedOutcome()
    {
        string original = Definition("ID_OLD", "Original content");
        string changed = Definition("ID_OLD", "Changed authoritative content");
        string replacement = Definition("ID_NEW", "Renamed local content");
        string baseline = Document("1.0", original);
        string removed = LocalCorrectionDocument.Create(Document("1.0"), baseline, "core/removal.xml",
            [new("remove", "remove", "ID_OLD", null, Fingerprint(original))]);
        Require(LocalCorrectionDocument.Evaluate(removed, Document("2.0", changed)).IncorporatedKeys.Count == 0,
            "A changed same-ID definition is not proof that an upstream removal was incorporated.");
        string renamed = LocalCorrectionDocument.Create(Document("1.0", replacement), baseline, "core/rename.xml",
            [new("rename", "rename", "ID_OLD", "ID_NEW", Fingerprint(original))]);
        Require(LocalCorrectionDocument.Evaluate(renamed, Document("2.0", changed, replacement)).IncorporatedKeys.Count == 0,
            "Publishing the rename destination while retaining a changed old ID does not complete the rename.");

        string peer = Definition("ID_OLD", "A distinct unchanged publication using the old ID");
        string duplicateBaseline = Document("1.0", original, peer);
        string staffRename = LocalCorrectionDocument.Create(Document("1.0", peer, replacement), duplicateBaseline, "core/staff.xml",
            [new("rename", "rename", "ID_OLD", "ID_NEW", Fingerprint(original))]);
        TestAssert.Sequence(["rename"], LocalCorrectionDocument.Evaluate(staffRename,
            Document("2.0", peer, replacement)).IncorporatedKeys);
        string staffRemove = LocalCorrectionDocument.Create(Document("1.0", peer), duplicateBaseline, "core/staff.xml",
            [new("remove", "remove", "ID_OLD", null, Fingerprint(original))]);
        TestAssert.Sequence(["remove"], LocalCorrectionDocument.Evaluate(staffRemove,
            Document("2.0", peer)).IncorporatedKeys);
        Require(LocalCorrectionDocument.Evaluate(staffRemove, Document("2.0", peer, peer)).IncorporatedKeys.Count == 0,
            "Surviving same-ID declarations must be a submultiset of unchanged baseline peers.");

        // The local intent composes successfully, but the incoming publication still has another
        // declaration at the replacement ID. One matching declaration cannot authorize clearing it.
        string replacementOriginal = Definition("ID_NEW", "Old publication at the new ID");
        foreach (string operation in new[] { "replace", "rename", "add" })
        {
            string originalXml;
            LocalCorrection[] corrections;
            if (operation == "replace")
            {
                originalXml = Document("1.0", replacementOriginal);
                corrections = [new("selected", "replace", "ID_NEW", null, Fingerprint(replacementOriginal))];
            }
            else
            {
                originalXml = operation == "rename" ? Document("1.0", original, replacementOriginal)
                    : Document("1.0", replacementOriginal);
                corrections = [new("remove-old-destination", "remove", "ID_NEW", null, Fingerprint(replacementOriginal)),
                    new("selected", operation, operation == "rename" ? "ID_OLD" : "ID_NEW",
                        operation == "rename" ? "ID_NEW" : null, operation == "rename" ? Fingerprint(original) : null)];
            }
            string local = LocalCorrectionDocument.Create(Document("1.0", replacement), originalXml,
                "core/replacement-collision.xml", corrections);
            var evaluated = LocalCorrectionDocument.Evaluate(local, Document("2.0", replacementOriginal, replacement));
            Require(!evaluated.IncorporatedKeys.Contains("selected"),
                $"An upstream {operation} requires every declaration at its destination to match the local definition.");
            Require(Element(LocalCorrectionDocument.Parse(evaluated.EffectiveXml), "ID_NEW").Value == "Renamed local content",
                "The correction remains usable while its incoming collision still needs review.");
        }
    }
}
