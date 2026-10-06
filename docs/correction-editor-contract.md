# Reviewing and replacing local corrections

`Aurora.Content.ContentCorrectionEditor` exposes a small read, preview, and save
workflow in 0.11.0. It can replace the **local gameplay definition** owned by an
existing correction, approve keeping it locally, or explicitly accept an
incorporated upstream version. The correction's original source and identity
remain intact. Reflections can expose these actions after adopting the package;
this repository does not add its UI.

```csharp
using Aurora.Content;

var reviewed = ContentCorrectionEditor.Read(localCorrectionPath);
var replacements = new Dictionary<string, string>
{
    // The key is the correction metadata key, not the Aurora element ID.
    ["repair-grant"] = """
        <element id="ID_FEATURE" name="Feature" type="Class Feature" source="Example">
          <description><p>The revised description.</p></description>
          <rules><grant type="Class Feature" id="ID_CORRECT_TARGET" /></rules>
        </element>
        """
};

var preview = ContentCorrectionEditor.PreviewReplacement(reviewed, replacements);
// Show preview.LocalXml and preview.EffectiveXml for review.
// The host decides when the user has approved saving this same request.
var saved = ContentCorrectionEditor.Replace(reviewed, replacements);

// Saving edits the local XML only. Refresh SQLite separately when appropriate.
await ContentImport.ImportAsync(contentRoot, databasePath);
```

The editor captures local and authoritative XML together with hashes of their
bytes. Preview and save read the files again and refuse a stale review. Save
recomputes the proposal from the real inputs; changing XML fields in a public
review object does not change what gets saved. Save validates before staging a
temporary sibling file, checks the inputs again, and replaces the local file.
It performs optimistic change detection, not a transaction spanning arbitrary
external XML editors or several files. An exception means the host must report
the failure rather than show the edit as saved.

## Supported changes

- Replace one or several complete definitions in an existing managed file under
  `user/local`, addressed by exact, case-sensitive correction keys.
- Existing `replace`, `rename`, and `add` corrections are supported. For a rename,
  the supplied element keeps the current replacement ID. For the others it keeps
  the current target ID. The payload is one unnamespaced `<element>`, not a whole
  `<elements>` file or an `<append>`.
- Descriptions, rules, supports, and other definition content can change directly.
  This replaces the whole definition; omitted content is not automatically merged
  back in. For example, replacing grant A with grant B leaves only B in that
  definition, whereas appending B would have kept both grants.
- The embedded original baseline, original fingerprints, correction keys,
  operation, target/replacement IDs, group, and reason remain unchanged.
  Unselected local elements and operational nodes remain in place. Unchanged
  companion definitions continue following upstream through the existing evaluator.
- Changed corrections automatically return to `review-pending`, including every
  linked member of the same group in that file, and their approval stamps are cleared.
  An untouched group keeps its state.
  A matching installed definition does not automatically accept the revised fix.
- Group members in other local files may remain `review-pending`. If another
  member needs reopening from `approved-local` or `accepted-upstream`, the editor reports its group
  and path and refuses the save. It does not perform multi-file state changes.
  Grouped saves also check the other local XML files for changes while staging.
  These peer checks cover the edited file's content root. Hosts combining several
  roots must coordinate cross-root groups themselves; this single-root wrapper
  does not establish that those external groups are consistent.
- Disabled correction files stay disabled. Selected definitions are still checked
  for valid typed content before saving.

Both preview and save use the existing correction evaluator and importer
declaration parser. This checks correction consistency and typed XML; it does not
prove character eligibility, resolve all links, or validate the entire catalog.
The normal import still validates its candidate database before activation. If
that later import fails, the saved XML remains available for repair and the
previous database remains intact.

## Approval and grouped acceptance

```csharp
var snapshot = ContentCorrectionEditor.Read(localCorrectionPath);
var review = ContentCorrectionEditor.ReadReview(snapshot);
var group = review.Groups.Single(g => g.Group == "parent-grant-repair");

// Present all group members and review.Evaluation to the user before either action.
if (group.CanApproveLocal)
{
    var preview = ContentCorrectionEditor.PreviewApproveLocal(snapshot, group.CorrectionKeys);
    // After the user's explicit approval:
    var saved = ContentCorrectionEditor.ApproveLocal(snapshot, group.CorrectionKeys);
}
// Alternatively, when group.CanAcceptUpstream and the user chooses upstream:
// var preview = ContentCorrectionEditor.PreviewAcceptUpstream(snapshot, group.CorrectionKeys);
// var saved = ContentCorrectionEditor.AcceptUpstream(snapshot, group.CorrectionKeys);
// Refresh the database separately after either save.
```

`Read` returns `ContentCorrectionEditSnapshot`, containing the exact file paths,
XML, and byte hashes. `ReadReview` returns `ContentCorrectionReviewDetails` with
the evaluated three-way content and `ContentCorrectionReviewGroup` entries.
Each entry has `Group`, `CorrectionKeys`, `Corrections`, `CanApproveLocal`,
`CanAcceptUpstream`, and `BlockingReasons`. Ungrouped corrections are individual
review units with a null `Group`. Structural correction errors throw; disabled
files, invalid typed definitions, and cross-file group issues appear as blocking
reasons when the contract itself can evaluate. An unincorporated group can still
approve its local content; its acceptance reason explains why upstream cannot
be selected through this wrapper.

| State | Effective content | Review/retirement behavior |
| --- | --- | --- |
| `review-pending` | Local correction remains active | Needs review; protected from retirement |
| `approved-local` | Local correction remains active | Acknowledged relevant revision; protected from retirement |
| `accepted-upstream` | Upstream supplies that correction | Retirement requires all corrections accepted, redundancy, and successful import |

Local approval stores `approval-fingerprint` in XML. Its versioned structural
fingerprint covers correction metadata, the selected original baseline, local
definition, relevant upstream definitions (including rename destination and
absence/order of same-ID declarations), root attributes, update URLs, source path,
and same-file group membership. The existing positional constructors and
`Deconstruct` methods remain unchanged; the stamp is an additional init property
on `LocalCorrection`, alongside computed `IsActive`.

Changing a relevant definition, intent, or group membership invalidates approval
for the entire same-file group. Evaluation returns those corrections as
`review-pending` with an actionable reason while keeping protection active.
Evaluation/import do not silently rewrite the XML stamp. The next explicit
approval records a fresh stamp; replacement clears it. Changes to unrelated IDs,
comments, or file version metadata do not invalidate approval. Existing origin
checks still reject a changed authoritative URL or a version older than the baseline.
Structural fingerprints remain conservative about whitespace inside relevant XML.
Approval acknowledges declaration-level corrections, not later append composition,
whole-catalog health, or character outcomes. Unclassified local edits and operations
continue to have their own diagnostics even when classified corrections are approved.

Every selected group must include all its same-file keys. Approval and acceptance
refuse any linked member in another file in the current content root, regardless
of that member's state. A multi-root host must establish that no external group
members exist before using these single-root actions. Coordinated multi-file
transactions remain a follow-up.

The high-level acceptance workflow requires every selected key in the evaluation's
`IncorporatedKeys`; it then records the user's explicit acceptance. Matching
installed files alone never cause automatic acceptance, and local approval never
authorizes retirement. Verified-download evidence and automatic acceptance are
outside this API. The preexisting low-level explicit acceptance primitive still
allows a host to deliberately relinquish a correction; its policy is not broadened
into automatic acceptance.

SQLite continues mirroring durable XML. Valid approvals have file status
`approved-local`; pending corrections or other review diagnostics yield
`review-required`. Stale approval is mirrored as pending while stored `local_xml`
retains the original stamp for inspection. Approved additions retain local/homebrew
ownership in both stored and runtime projections. Schema stays **1**, preparation
contract stays **2**, and data advances **18 -> 19** to guard the changed review
semantics. Refresh existing databases with `ContentImport.ImportAsync`; do not
change metadata manually. Older packages reject the unknown XML approval state.

## Deliberate boundaries

Replacement cannot change an operation or Aurora ID, amend a `remove` operation
into a replacement, create a new correction entry, rebase the original baseline,
restore an already retired file, or edit an unmarked ordinary XML file. Missing or
ambiguous original/local declarations require explicit repair. Approval/acceptance
requires an enabled file. Replacement preserves a disabled file's state.

No Lights UI or CLI command is added. A consuming application owns presentation,
user decisions, and the separate database refresh. Saving XML does not activate
or retire content. Candidate import validation still preserves the previous
database on failure; the saved XML remains available for repair.

For hosts that already own their lifecycle, `LocalCorrectionDocument` exposes pure
`ReplaceDefinitions(localXml, upstreamXml, replacementsByCorrectionKey)`,
`ApproveLocal(localXml, upstreamXml, correctionKeys)`, and
`AcceptUpstream(localXml, upstreamXml, correctionKeys)`. They return
`LocalCorrectionEvaluation`, including updated `LocalXml`, `EffectiveXml`, states,
review reasons, and `IncorporatedKeys`. They perform no I/O, typed importer
validation, or cross-file discovery. The host supplies those safeguards; the
high-level editor does so for a single content root. Replacement can repair an
original state that fails effective evaluation, such as a local addition newly
claimed upstream.

## Verification

Release verification and immutable package provenance are recorded in
[the 0.11.0 release record](package-release-0.11.0.md). Tests use disposable XML
and databases, covering replacement provenance, approval invalidation, grouped
review, source ownership, stale inputs, save/import boundaries, and retirement.
No installed content, production database, or consumer UI is part of these tests.
