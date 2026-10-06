using System.Security.Cryptography;
using System.Xml.Linq;
using Aurora.Content.Contracts;
using Microsoft.Data.Sqlite;

internal static class LocalApprovalEditorTests
{
    private const string First = "<element id='ID_APPROVAL_FIRST' name='Original first' type='Class Feature' source='Test'><description>First description</description></element>";
    private const string Second = "<element id='ID_APPROVAL_SECOND' name='Original second' type='Class Feature' source='Test'><description>Second description</description></element>";
    private const string Baseline = "<elements>" + First + Second + "</elements>";
    private static readonly string[] Keys = ["first-fix", "second-fix"];
    private const string Group = "approval-pair";

    private sealed class Fixture : IDisposable
    {
        private readonly TestWorkspace workspace = TestWorkspace.Create();
        internal string Root => Path.Combine(workspace.DirectoryPath, "content");
        internal string Database => workspace.DatabasePath;
        internal string Local => Path.Combine(Root, "user", "local", "fix.xml");
        internal string Source => Path.Combine(Root, "core", "source.xml");
        internal string Protected => Baseline.Replace("Original first", "Protected first").Replace("Original second", "Protected second");
        internal Fixture(bool incorporated = false)
        {
            Write("core/source.xml", incorporated ? Protected : Baseline);
            Write("user/local/fix.xml", LocalCorrectionDocument.Create(Protected, Baseline, "core/source.xml",
                [
                    new("first-fix", "replace", "ID_APPROVAL_FIRST", null, LocalCorrectionDocument.Fingerprint(XElement.Parse(First)),
                        Group: Group, Reason: "Keep first correction"),
                    new("second-fix", "replace", "ID_APPROVAL_SECOND", null, LocalCorrectionDocument.Fingerprint(XElement.Parse(Second)),
                        Group: Group, Reason: "Keep second correction")
                ]));
        }
        internal string Write(string relative, string xml)
        {
            string path = Path.GetFullPath(Path.Combine(Root, relative));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, xml);
            return path;
        }
        internal string Peer(string state)
        {
            const string peer = "<elements><element id='ID_APPROVAL_PEER' name='Peer' type='Class Feature' source='Test'/></elements>";
            Write("core/peer.xml", peer);
            string correction = LocalCorrectionDocument.Create(peer, peer, "core/peer.xml",
                [new("peer-fix", "replace", "ID_APPROVAL_PEER", null, null,
                    state == "approved-local" ? "review-pending" : state, Group)]);
            if (state == "approved-local")
                correction = LocalCorrectionDocument.ApproveLocal(correction, peer, ["peer-fix"]).LocalXml;
            return Write("user/local/peer.xml", correction);
        }
        internal void Import() => ContentImport.ImportAsync(Root, Database).GetAwaiter().GetResult();
        public void Dispose() { SqliteConnection.ClearAllPools(); workspace.Dispose(); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static string Hash(string path)
    {
        SqliteConnection.ClearAllPools();
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static Dictionary<string, string> Capture(Fixture fixture) =>
        Directory.GetFiles(fixture.Root, "*.xml", SearchOption.AllDirectories).ToDictionary(path => path, Hash);

    private static void Unchanged(Fixture fixture, IReadOnlyDictionary<string, string> hashes)
    {
        Require(hashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value),
            "Review refusal or preview must preserve every input file.");
        Require(Directory.GetFiles(fixture.Root, "*.edit-*", SearchOption.AllDirectories).Length == 0,
            "No temporary correction edit may remain after preview, save or refusal.");
    }

    private static void Refuse(Action action, string context)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or IOException) { return; }
        throw new Exception("Expected correction editor to refuse " + context + ".");
    }

    private static Dictionary<string, string> Amendment() => new()
    {
        ["first-fix"] = First.Replace("Original first", "Amended first").Replace("First description", "Amended description")
    };

    internal static void GroupApprovalAmendmentAndAcceptanceLifecycle()
    {
        using var fixture = new Fixture();
        fixture.Import();
        string database = Hash(fixture.Database);
        var snapshot = ContentCorrectionEditor.Read(fixture.Local);
        var review = ContentCorrectionEditor.ReadReview(snapshot);
        var group = review.Groups.Single(g => g.Group == Group);
        TestAssert.Sequence(Keys, group.CorrectionKeys);
        Require(group.Corrections.Count == 2 && group.CanApproveLocal && !group.CanAcceptUpstream,
            "A complete pending group can approve its local corrections while unincorporated upstream acceptance stays unavailable.");
        var hashes = Capture(fixture);
        var preview = ContentCorrectionEditor.PreviewApproveLocal(snapshot, Keys);
        Require(preview.Corrections.All(c => c.State == "approved-local" && c.IsActive && !string.IsNullOrEmpty(c.ApprovalFingerprint))
            && !preview.CanRetire, "Local approval must keep every group correction active and protected.");
        Unchanged(fixture, hashes);
        Require(Hash(fixture.Database) == database, "Approval preview must not refresh the database.");
        var approved = ContentCorrectionEditor.ApproveLocal(snapshot, Keys);
        Require(approved.LocalXml == preview.LocalXml && File.ReadAllText(fixture.Local) == approved.LocalXml
            && File.Exists(fixture.Local) && Hash(fixture.Source) == hashes[fixture.Source]
            && Hash(fixture.Database) == database, "Approval saves only the reviewed local XML and leaves import separate.");
        var approvedReview = ContentCorrectionEditor.ReadReview(ContentCorrectionEditor.Read(fixture.Local));
        Require(approvedReview.Evaluation.Corrections.All(c => c.State == "approved-local" && c.IsActive),
            "Typed review exposes approved-local as an active state.");
        fixture.Import();
        Require(File.Exists(fixture.Local), "Successful import must not retire approved local corrections.");

        var amended = ContentCorrectionEditor.Replace(ContentCorrectionEditor.Read(fixture.Local), Amendment());
        Require(amended.Corrections.All(c => c.State == "review-pending" && c.ApprovalFingerprint == null),
            "Amending an approved correction must reopen its entire group and clear stale approval stamps.");
        Require(amended.EffectiveXml.Contains("Amended description", StringComparison.Ordinal), "Amendment keeps its full description.");

        fixture.Write("core/source.xml", amended.EffectiveXml);
        snapshot = ContentCorrectionEditor.Read(fixture.Local);
        review = ContentCorrectionEditor.ReadReview(snapshot);
        Require(review.Groups.Single(g => g.Group == Group).CanAcceptUpstream,
            "Acceptance becomes available only after every selected correction is incorporated upstream.");
        hashes = Capture(fixture);
        database = Hash(fixture.Database);
        var acceptedPreview = ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys);
        Require(acceptedPreview.Corrections.All(c => c.State == "accepted-upstream" && !c.IsActive && c.ApprovalFingerprint == null)
            && acceptedPreview.CanRetire, "Upstream acceptance clears local approval and permits later retirement.");
        Unchanged(fixture, hashes);
        var accepted = ContentCorrectionEditor.AcceptUpstream(snapshot, Keys);
        Require(accepted.LocalXml == acceptedPreview.LocalXml && File.Exists(fixture.Local)
            && Hash(fixture.Database) == database && Hash(fixture.Source) == hashes[fixture.Source],
            "Saving acceptance neither retires the file nor activates a database itself.");
        fixture.Import();
        Require(!File.Exists(fixture.Local), "An incorporated, accepted correction retires only after successful import.");
    }

    internal static void ChangedSnapshotsRejectApprovalAndAcceptance()
    {
        foreach (bool changeLocal in new[] { true, false })
        {
            using var fixture = new Fixture(incorporated: true);
            var snapshot = ContentCorrectionEditor.Read(fixture.Local);
            ContentCorrectionEditor.PreviewApproveLocal(snapshot, Keys);
            ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys);
            File.AppendAllText(changeLocal ? fixture.Local : fixture.Source, "\n<!-- changed after preview -->");
            var hashes = Capture(fixture);
            Refuse(() => ContentCorrectionEditor.PreviewApproveLocal(snapshot, Keys), "stale approval preview");
            Refuse(() => ContentCorrectionEditor.ApproveLocal(snapshot, Keys), "stale approval save");
            Refuse(() => ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys), "stale acceptance preview");
            Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, Keys), "stale acceptance save");
            Unchanged(fixture, hashes);
            Require(!File.Exists(fixture.Database), "Review actions must not create a database.");
        }
    }

    internal static void PartialGroupsAndUnincorporatedAcceptanceAreRefused()
    {
        using var fixture = new Fixture();
        var snapshot = ContentCorrectionEditor.Read(fixture.Local);
        var hashes = Capture(fixture);
        Refuse(() => ContentCorrectionEditor.PreviewApproveLocal(snapshot, [Keys[0]]), "partial group approval preview");
        Refuse(() => ContentCorrectionEditor.ApproveLocal(snapshot, [Keys[0]]), "partial group approval save");
        Refuse(() => ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys), "unincorporated acceptance preview");
        Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, Keys), "unincorporated acceptance save");
        Unchanged(fixture, hashes);
        fixture.Write("core/source.xml", fixture.Protected);
        snapshot = ContentCorrectionEditor.Read(fixture.Local);
        hashes = Capture(fixture);
        Refuse(() => ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, [Keys[0]]), "partial incorporated group preview");
        Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, [Keys[0]]), "partial incorporated group save");
        Unchanged(fixture, hashes);
    }

    internal static void ExternalGroupPeersBlockReviewActions()
    {
        foreach (string peerState in new[] { "review-pending", "approved-local", "accepted-upstream" })
        {
            using var fixture = new Fixture(incorporated: true);
            string peer = fixture.Peer(peerState);
            var snapshot = ContentCorrectionEditor.Read(fixture.Local);
            var group = ContentCorrectionEditor.ReadReview(snapshot).Groups.Single(g => g.Group == Group);
            Require(!group.CanApproveLocal && !group.CanAcceptUpstream && group.BlockingReasons.Count > 0,
                $"An external {peerState} group peer needs a coordinated executor before either review action.");
            var hashes = Capture(fixture);
            Refuse(() => ContentCorrectionEditor.PreviewApproveLocal(snapshot, Keys), "cross-file group approval preview");
            Refuse(() => ContentCorrectionEditor.ApproveLocal(snapshot, Keys), "cross-file group approval save");
            Refuse(() => ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys), "cross-file group acceptance preview");
            Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, Keys), "cross-file group acceptance save");
            Unchanged(fixture, hashes);
            if (peerState == "review-pending")
            {
                var amended = ContentCorrectionEditor.Replace(snapshot, Amendment());
                Require(amended.Corrections.All(c => c.State == "review-pending") && Hash(peer) == hashes[peer],
                    "Existing local replacement remains available when cross-file peers are already pending.");
            }
        }
    }

    internal static void GroupAppearingAfterPreviewStopsSave()
    {
        foreach (bool approve in new[] { true, false })
        {
            using var fixture = new Fixture(incorporated: true);
            var snapshot = ContentCorrectionEditor.Read(fixture.Local);
            if (approve) ContentCorrectionEditor.PreviewApproveLocal(snapshot, Keys);
            else ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys);
            fixture.Peer("review-pending");
            var hashes = Capture(fixture);
            if (approve) Refuse(() => ContentCorrectionEditor.ApproveLocal(snapshot, Keys), "a newly discovered external peer during approval");
            else Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, Keys), "a newly discovered external peer during acceptance");
            Unchanged(fixture, hashes);
        }
    }

    internal static void DisabledAndInvalidDefinitionsDisableReviewActions()
    {
        foreach (bool disabled in new[] { true, false })
        {
            using var fixture = new Fixture(incorporated: true);
            var local = LocalCorrectionDocument.Parse(File.ReadAllText(fixture.Local));
            if (disabled) local.Root!.SetAttributeValue("ignore", "true");
            else local.Root!.Elements("element").First().Add(new XElement("compendium", new XAttribute("display", "perhaps")));
            File.WriteAllText(fixture.Local, local.ToString(SaveOptions.DisableFormatting));
            var snapshot = ContentCorrectionEditor.Read(fixture.Local);
            var group = ContentCorrectionEditor.ReadReview(snapshot).Groups.Single(g => g.Group == Group);
            Require(!group.CanApproveLocal && !group.CanAcceptUpstream && group.BlockingReasons.Count > 0,
                "Disabled files and typed-invalid corrected definitions must report unavailable review actions.");
            var hashes = Capture(fixture);
            Refuse(() => ContentCorrectionEditor.PreviewApproveLocal(snapshot, Keys), "invalid or disabled approval preview");
            Refuse(() => ContentCorrectionEditor.ApproveLocal(snapshot, Keys), "invalid or disabled approval save");
            Refuse(() => ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, Keys), "invalid or disabled acceptance preview");
            Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, Keys), "invalid or disabled acceptance save");
            Unchanged(fixture, hashes);
        }
    }

    internal static void EligibilityValidatesProposedReviewState()
    {
        using (var fixture = new Fixture())
        {
            const string baseline = "<elements/>";
            const string localAddition = "<elements><element id='ID_REVIEW_ADDITION' name='Old local addition' type='Class Feature' source='Test'/></elements>";
            string upstream = localAddition.Replace("Old local addition", "New authoritative definition");
            fixture.Write("core/source.xml", upstream);
            fixture.Write("user/local/fix.xml", LocalCorrectionDocument.Create(localAddition, baseline, "core/source.xml",
                [new("addition", "add", "ID_REVIEW_ADDITION", null, null, "accepted-upstream")]));
            var snapshot = ContentCorrectionEditor.Read(fixture.Local);
            var review = ContentCorrectionEditor.ReadReview(snapshot);
            Require(review.Evaluation.Corrections.Single().State == "accepted-upstream"
                && !review.Groups.Single().CanApproveLocal && review.Groups.Single().BlockingReasons.Count > 0,
                "An inactive accepted addition cannot offer approval when reactivation would collide with its upstream ID.");
            var hashes = Capture(fixture);
            Refuse(() => ContentCorrectionEditor.PreviewApproveLocal(snapshot, ["addition"]), "reactivating a conflicting accepted addition");
            Refuse(() => ContentCorrectionEditor.ApproveLocal(snapshot, ["addition"]), "saving a conflicting accepted addition approval");
            Unchanged(fixture, hashes);
        }
        using (var fixture = new Fixture())
        {
            string baseline = "<elements>" + First + "</elements>";
            string fixedFirst = First.Replace("Original first", "Protected first");
            string local = "<elements>" + fixedFirst + "</elements>";
            fixture.Write("core/source.xml", "<elements>" + First + fixedFirst + "</elements>");
            fixture.Write("user/local/fix.xml", LocalCorrectionDocument.Create(local, baseline, "core/source.xml",
                [new("first-fix", "replace", "ID_APPROVAL_FIRST", null, LocalCorrectionDocument.Fingerprint(XElement.Parse(First)))]));
            var snapshot = ContentCorrectionEditor.Read(fixture.Local);
            var review = ContentCorrectionEditor.ReadReview(snapshot);
            Require(!review.Groups.Single().CanAcceptUpstream && review.Groups.Single().BlockingReasons.Count > 0,
                "An upstream file retaining conflicting old and corrected definitions has not incorporated the entire replacement.");
            Require(LocalCorrectionDocument.Parse(review.Evaluation.EffectiveXml).Root!.Elements("element").Count() == 1,
                "The active replacement still resolves the authored duplicate while acceptance remains unavailable.");
            var hashes = Capture(fixture);
            Refuse(() => ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, ["first-fix"]), "accepting conflicting upstream declarations");
            Refuse(() => ContentCorrectionEditor.AcceptUpstream(snapshot, ["first-fix"]), "saving acceptance of conflicting upstream declarations");
            Unchanged(fixture, hashes);
        }
    }
}
