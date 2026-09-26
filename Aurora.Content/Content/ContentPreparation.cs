#nullable enable
using Aurora.Content.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml.Linq;

namespace Aurora.Content.Preparation;

/// <summary>Owns correction decisions and finalized XML. No database writes or acceptance here.</summary>
internal sealed class ContentPreparation : IDisposable
{
    internal sealed record FileState(string Root, string Relative, string Path, string Hash);
    internal sealed record ManagedFile(FileState File, LocalCorrectionEvaluation Evaluation);
    internal sealed record Declaration(string Id, string Path, string Hash, int Ordinal, string Fingerprint, string Xml, bool Recovered = false);
    internal sealed record AppendOperation(FileState File, int Ordinal, string TargetId, string Xml, string Status, string? Diagnostic);
    internal sealed record FinalizedElement(string Id, string Path, string BaseXml, string EffectiveXml);
    private readonly string work = Path.Combine(Path.GetTempPath(), "translator-preparation-" + Guid.NewGuid().ToString("N"));
    internal List<FileState> Files { get; } = [];
    internal List<ManagedFile> Managed { get; } = [];
    internal List<string> StagedRoots { get; } = [];
    private readonly Dictionary<string, string> stages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Declaration> declarations = [];
    internal IReadOnlyList<Declaration> Declarations => declarations;
    internal List<AppendOperation> Appends { get; } = [];
    internal List<FinalizedElement> Finalized { get; } = [];
    internal List<Declaration> UnavailableDeclarations { get; } = [];
    internal List<Declaration> RejectedDeclarations { get; } = [];
    internal sealed record Supplier(string Path, bool IsAppend = false);
    internal sealed record PreviousDefinition(string Path, string Xml, IReadOnlyList<Supplier> Suppliers);
    internal sealed record DefinitionResolution(Declaration Selected, string Kind, string? PreviousPath, string Xml, IReadOnlyList<Supplier> Suppliers);
    internal List<DefinitionResolution> Resolutions { get; } = [];
    private readonly HashSet<string> retainedIds = new(StringComparer.Ordinal);
    private Func<string, PreviousDefinition?>? previousDefinition;
    private Func<IEnumerable<string>, IReadOnlySet<string>>? unreadableSupplierIds;
    private IReadOnlySet<string> protectedInputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> UnavailableIds { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> UnavailableReasons { get; } = new(StringComparer.Ordinal);
    /// <summary>Unavailable identities and omitted files/operations, with the reason for each.</summary>
    internal List<ContentImportSkip> Skipped { get; } = [];
    private readonly bool skipUnusable;
    private readonly bool quarantineConflicts;
    private readonly IReadOnlySet<string> previouslyUnavailableIds;
    private readonly HashSet<string> discarded = new(StringComparer.OrdinalIgnoreCase);

    private ContentPreparation(bool skipUnusable, bool quarantineConflicts, IReadOnlySet<string>? previouslyUnavailableIds)
    {
        this.skipUnusable = skipUnusable;
        this.quarantineConflicts = quarantineConflicts;
        this.previouslyUnavailableIds = previouslyUnavailableIds ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <param name="skipUnusable">
    /// Leaves a file that cannot be used out of the import instead of refusing the whole import,
    /// recording why in <see cref="Skipped"/>. Off by default: an import that says it succeeded
    /// has read every file it was given.
    /// </param>
    internal static ContentPreparation Prepare(IReadOnlyList<string> roots, CancellationToken cancellation = default,
        ImportProgressReporter? progress = null, bool skipUnusable = false,
        bool quarantineConflicts = false, IReadOnlySet<string>? previouslyUnavailableIds = null,
        Func<string, PreviousDefinition?>? previousDefinition = null,
        Func<IEnumerable<string>, IReadOnlySet<string>>? unreadableSupplierIds = null,
        IReadOnlySet<string>? protectedInputPaths = null)
    {
        var prepared = new ContentPreparation(skipUnusable, quarantineConflicts, previouslyUnavailableIds);
        prepared.previousDefinition = previousDefinition;
        prepared.unreadableSupplierIds = unreadableSupplierIds;
        if (protectedInputPaths != null) prepared.protectedInputPaths = protectedInputPaths;
        try
        {
            prepared.Capture(roots, cancellation, progress);
            prepared.Evaluate(cancellation);
            prepared.FinalizeDeclarations();
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    // How the fault about to be thrown should be reported, read by the handler that skips the file.
    private (string Kind, string? RelatedPath)? fault;

    /// <summary>
    /// A fault one file is answerable for, such as an unreadable declaration.
    /// </summary>
    private InvalidDataException Fault(string kind, string detail, string? relatedPath = null)
    {
        fault = (kind, relatedPath);
        return new InvalidDataException(detail);
    }

    private bool CanSkip(Exception ex) =>
        skipUnusable && (ex is InvalidDataException || ex is System.Xml.XmlException || ex is IOException);

    private void RecordSkip(FileState file, string kind, string detail, string? relatedPath = null) =>
        Skipped.Add(new ContentImportSkip(file.Path, file.Relative, kind, detail, relatedPath));

    /// <summary>
    /// Takes a file out of this import and keeps the reason. The staged copy is removed so nothing
    /// downstream reads it; the file the user wrote is never touched, and the next import reads it
    /// again, so a fix is picked up and an unfixed file is reported again.
    /// </summary>
    private void Discard(FileState file, string kind, string detail, string? relatedPath = null)
    {
        RecordSkip(file, kind, detail, relatedPath);
        if (!discarded.Add(file.Path)) return;
        string staged = Path.Combine(Stage(file), file.Relative);
        if (File.Exists(staged)) File.Delete(staged);
    }

    private void Capture(IReadOnlyList<string> roots, CancellationToken cancellation, ImportProgressReporter? progress)
    {
        foreach (var inputRoot in roots)
        {
            string root = Path.GetFullPath(inputRoot);
            string stage = Path.Combine(work, StagedRoots.Count.ToString(), new DirectoryInfo(root).Name);
            StagedRoots.Add(stage);
            if (!stages.TryAdd(root, stage)) throw new InvalidDataException($"Repeated content root: {root}");
            Directory.CreateDirectory(stage);
            var paths = Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).ToList();
            int captured = 0;
            foreach (var path in paths)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Report(ContentImportPhase.Preparing, captured++, paths.Count, Path.GetRelativePath(root, path));
                // Reject links before capture as well as during origin resolution.
                for (FileSystemInfo? current = new FileInfo(path); current != null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
                {
                    if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException($"Content input traverses a symbolic link/junction: {path}");
                    if (current.FullName == root) break;
                }
                string relative = Path.GetRelativePath(root, path);
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch (Exception ex) when (skipUnusable && (ex is IOException or UnauthorizedAccessException))
                {
                    if (protectedInputPaths.Contains(path))
                        throw new InvalidDataException($"Cannot read protected correction input {path}; repair access before activation. {ex.Message}", ex);
                    var unavailable = new FileState(root, relative, path, ContentInputFingerprint.Unreadable);
                    Files.Add(unavailable);
                    Discard(unavailable, "unreadable", $"Cannot capture {path}: {ex.Message}");
                    continue;
                }
                Files.Add(new(root, relative, path, Convert.ToHexString(SHA256.HashData(bytes))));
                string destination = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            }
        }
    }

    private string Stage(FileState file) => stages[file.Root];

    private void Evaluate(CancellationToken cancellation)
    {
        // Evaluate captured bytes, never a mixture of live file revisions.
        foreach (var file in Files)
        {
            cancellation.ThrowIfCancellationRequested();
            if (discarded.Contains(file.Path)) continue;
            if (!file.Relative.Replace('\\', '/').StartsWith("user/local/", StringComparison.OrdinalIgnoreCase)) continue;
            string stagedPath = Path.Combine(Stage(file), file.Relative);
            XDocument local;
            try
            {
                using var reader = System.Xml.XmlReader.Create(new StringReader(File.ReadAllText(stagedPath)),
                    new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                local = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException ex)
            {
                // A truncated document can hide a correction section after the damaged XML.
                // Do not infer absence of protected intent from a substring search.
                throw new InvalidDataException($"Cannot determine correction intent in {file.Path}: repair the malformed local XML before activation. {ex.Message}", ex);
            }
            bool marked = local.Root?.Name.LocalName == "corrections" ||
                local.Root?.Elements().Any(e => e.Name.LocalName == "corrections") == true ||
                local.Descendants().Any(e => e.Name.NamespaceName.StartsWith("urn:aurora-lights:corrections:", StringComparison.Ordinal));
            // Local files are not automatically corrections. Let ordinary content use the
            // normal unreadable-file policy in FinalizeDeclarations.
            if (!marked) continue;
            try
            {
                var evaluation = LocalCorrectionDocument.FromFile(stagedPath, Stage(file))
                    ?? throw new InvalidDataException("Correction intent was found without a supported correction section.");
                Managed.Add(new(file, evaluation));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is System.Xml.XmlException || ex is IOException)
            {
                string detail = $"Correction preparation failed for {file.Path}: {ex.Message}";
                throw new InvalidDataException(detail, ex);
            }
        }
        var overlaps = Managed.GroupBy(m => LocalCorrectionDocument.ResolveSourcePath(m.File.Root, m.Evaluation.SourcePath), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        foreach (var overlapping in overlaps)
        {
            string detail = $"Multiple managed files target {overlapping.Key}: {string.Join(", ", overlapping.Select(m => m.File.Path))}. Consolidate or review them first.";
            throw new InvalidDataException(detail);
        }
        var partials = Managed.SelectMany(m => m.Evaluation.Corrections).Where(c => c.Group != null).GroupBy(c => c.Group).Where(g => g.Select(c => c.State).Distinct().Count() > 1).ToList();
        foreach (var partial in partials)
        {
            string detail = $"Related corrections in group '{partial.Key}' must be reviewed together.";
            throw new InvalidDataException(detail);
        }
        foreach (var entry in Managed.ToList())
        {
            try
            {
                string stage = Stage(entry.File);
                var effective = LocalCorrectionDocument.Parse(entry.Evaluation.EffectiveXml);
                var local = LocalCorrectionDocument.Parse(entry.Evaluation.LocalXml);
                local.Root!.Elements().Where(e => e.Name != "info").Remove();
                // Additions belong to the local supplier. Replacements/renames retain
                // the corrected source's attribution. The v1 evaluator remains intact.
                var additions = entry.Evaluation.Corrections.Where(c => c.Operation == "add" && c.State == "review-pending")
                    .Select(c => c.TargetId).ToHashSet(StringComparer.Ordinal);
                var upstreamIds = LocalCorrectionDocument.Parse(entry.Evaluation.UpstreamXml).Root!.Elements("element")
                    .Select(e => (string?)e.Attribute("id")).ToHashSet(StringComparer.Ordinal);
                foreach (var element in effective.Root!.Elements("element").Where(e => additions.Contains((string?)e.Attribute("id") ?? "") && !upstreamIds.Contains((string?)e.Attribute("id"))).ToArray())
                {
                    element.Remove();
                    local.Root.Add(element);
                }
                File.WriteAllText(LocalCorrectionDocument.ResolveSourcePath(stage, entry.Evaluation.SourcePath), effective.ToString(SaveOptions.DisableFormatting));
                File.WriteAllText(Path.Combine(stage, entry.File.Relative), local.ToString(SaveOptions.DisableFormatting));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is System.Xml.XmlException || ex is IOException)
            {
                throw new InvalidDataException($"Correction staging failed for {entry.File.Path}: {ex.Message}", ex);
            }
        }
    }

    private void FinalizeDeclarations()
    {
        var seen = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        var documents = new Dictionary<FileState, XDocument>();
        var targets = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var collected = new List<(FileState File, XElement Element, Declaration Declaration)>();
        var managedPaths = Managed.SelectMany(m => new[] { m.File.Path,
            LocalCorrectionDocument.ResolveSourcePath(m.File.Root, m.Evaluation.SourcePath) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Canonical representatives and all links are independent of app preferences.
        foreach (var file in Files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            if (discarded.Contains(file.Path)) continue;
            // Nothing a file declares counts until the file has been read through, so a file that
            // turns out to be unusable leaves no half-read remains in the catalog behind it.
            var fileDeclarations = new List<(FileState File, XElement Element, Declaration Declaration)>();
            fault = null;
            XDocument xml;
            bool misplacedCorrection = false;
            try
            {
                string stagedPath = Path.Combine(Stage(file), file.Relative);
                string text = File.ReadAllText(stagedPath);
                bool outsideLocal = !file.Relative.Replace('\\', '/').StartsWith("user/local/", StringComparison.OrdinalIgnoreCase);
                // Preserve explicit intent even when malformed XML prevents a complete parse.
                // After a successful parse, use actual elements so unused namespaces and prose
                // examples do not turn an ordinary file into a correction.
                misplacedCorrection = outsideLocal &&
                    (text.Contains("urn:aurora-lights:corrections:", StringComparison.Ordinal) ||
                     text.Contains(":corrections", StringComparison.Ordinal) || text.Contains("<corrections", StringComparison.Ordinal));
                try
                {
                    using var reader = System.Xml.XmlReader.Create(new StringReader(text),
                        new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                    xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                }
                catch (System.Xml.XmlException ex) when (misplacedCorrection)
                {
                    throw new InvalidDataException($"Correction metadata in {file.Path} is malformed and must be repaired under user/local before importing.", ex);
                }
                misplacedCorrection = outsideLocal &&
                    (xml.Root?.Name.LocalName == "corrections" ||
                     xml.Root?.Elements().Any(e => e.Name.LocalName == "corrections") == true ||
                     xml.Descendants().Any(e => e.Name.NamespaceName.StartsWith("urn:aurora-lights:corrections:", StringComparison.Ordinal)));
                if (misplacedCorrection)
                    throw new InvalidDataException($"Correction metadata in {file.Path} must be placed under user/local before importing.");
                if (xml.Root?.Name != "elements")
                {
                    string found = xml.Root is null ? "no root element" : $"found '{xml.Root.Name}'";
                    throw new InvalidDataException($"Content must have an unnamespaced elements root in {file.Path} ({found}).");
                }
                bool ignored = bool.TryParse((string?)xml.Root!.Attribute("ignore"), out bool flag) && flag;
                if (ignored) xml.Root.Elements().Where(e => e.Name != "info").Remove();
                int ordinal = 0;
                foreach (var element in xml.Root.Elements("element").ToList())
                {
                    string id = (string?)element.Attribute("id") ?? "";
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace((string?)element.Attribute("type")))
                        throw Fault("unreadable", $"Missing element id/type in {file.Path}, declaration {ordinal}.");
                    try { AuroraCatalogBuilder.ValidateDeclaration(element); }
                    catch (InvalidDataException ex)
                    { throw Fault("unreadable", $"Invalid declaration '{id}' in {file.Path}, declaration {ordinal}: {ex.Message}"); }
                    var declaration = new Declaration(id, file.Path, file.Hash, ordinal++, LocalCorrectionDocument.Fingerprint(element), element.ToString(SaveOptions.DisableFormatting));
                    fileDeclarations.Add((file, element, declaration));
                }
            }
            catch (Exception ex) when (CanSkip(ex) && !managedPaths.Contains(file.Path) && !misplacedCorrection)
            {
                Discard(file, fault?.Kind ?? "unreadable", ex.Message, fault?.RelatedPath);
                continue;
            }
            documents.Add(file, xml);
            collected.AddRange(fileDeclarations);
        }

        // Inspect every supplier before resolving revisions or choosing a reported fallback.
        var protectedIds = Managed.SelectMany(m => m.Evaluation.Corrections)
            .SelectMany(c => new[] { c.TargetId, c.ReplacementId }).Where(id => id != null)
            .Select(id => id!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unreadableIds = skipUnusable && discarded.Count > 0
            ? unreadableSupplierIds?.Invoke(discarded) ?? new HashSet<string>() : new HashSet<string>();
        // A skipped file must not erase the only working copy of its definitions.
        // Recover from the prepared catalog, not from incomplete current XML.
        var presentIds = collected.Select(d => d.Declaration.Id.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string id in unreadableIds.Where(id => !presentIds.Contains(id)))
        {
            var previous = previousDefinition?.Invoke(id);
            if (previous == null) continue;
            var file = Files.FirstOrDefault(f => discarded.Contains(f.Path) &&
                previous.Suppliers.Any(s => !s.IsAppend && string.Equals(s.Path, f.Path, StringComparison.OrdinalIgnoreCase)));
            if (file == null) continue;
            if (protectedIds.Contains(id))
                throw new InvalidDataException($"Corrected definition '{id}' has an unreadable supplier; repair it before activation.");
            if (!documents.TryGetValue(file, out var recovered))
                documents.Add(file, recovered = new XDocument(new XElement("elements")));
            var element = XElement.Parse(previous.Xml, LoadOptions.PreserveWhitespace);
            var declaration = new Declaration(id, file.Path, file.Hash, recovered.Root!.Elements("element").Count(),
                LocalCorrectionDocument.Fingerprint(element), previous.Xml, Recovered: true);
            recovered.Root.Add(element);
            declarations.Add(declaration);
            seen.Add(id, declaration);
            targets.Add(id, element);
            retainedIds.Add(id);
            Resolutions.Add(new(declaration, "retained", previous.Path, previous.Xml, previous.Suppliers));
            RecordSkip(file, "definition-collision", $"Kept the last-known working definition '{id}' because its suppliers are unreadable.", previous.Path);
        }
        foreach (var group in collected.GroupBy(d => d.Declaration.Id.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            // Ordered as Legacy leaves the id: the declaration it ends up owning the id with
            // comes first, because a later file re-declaring an id replaces the earlier element.
            var entries = group
                .OrderByDescending(d => d.File.Relative, LegacyLoadOrder.Comparer)
                .ThenByDescending(d => d.Declaration.Ordinal)
                .ThenByDescending(d => d.File.Path, StringComparer.Ordinal).ToList();
            var ids = entries.Select(d => d.Declaration.Id).Distinct(StringComparer.Ordinal).ToList();
            bool conflict = ids.Count > 1 || entries.Select(d => d.Declaration.Fingerprint).Distinct().Skip(1).Any() || unreadableIds.Contains(group.Key);
            if (!conflict)
            {
                var first = entries[0];
                seen.Add(first.Declaration.Id, first.Declaration);
                targets.Add(first.Declaration.Id, first.Element);
                // Winner first, matching the order above: an element is attributed to the file
                // whose declaration leads this list, and that attribution is what source
                // filtering keys off, so identical copies still have to name the right book.
                declarations.AddRange(entries.Select(d => d.Declaration));
                foreach (var duplicate in entries.Skip(1)) duplicate.Element.Remove();
                continue;
            }

            string suppliers = string.Join("; ", entries.Select(d => $"{d.File.Path} declaration {d.Declaration.Ordinal} ({d.Declaration.Fingerprint})"));
            string detail = $"duplicate-element-id: {string.Join(", ", ids.Select(id => $"'{id}'"))} has conflicting definitions: {suppliers}.";
            if (protectedIds.Contains(group.Key))
                throw new InvalidDataException(detail + " An explicit correction targets this identity; resolve the conflict before activation. The existing database was preserved.");

            // Compare exact authored IDs only. Case/padding aliases still require identity
            // repair; never manufacture a renamed ID or redirect its references here.
            if (ids.Count == 1)
            {
                // Already in Legacy's order, winner first; the successor rule below may drop
                // candidates from it, so work on a copy.
                var candidates = entries.ToList();
                bool HasOrigin(FileState file, string owner) => HasRepositoryOrigin(documents[file], owner);
                var older = candidates.Where(e => HasOrigin(e.File, "aurorabuilder")).ToList();
                bool successor = older.Count > 0 && candidates.Any(e => HasOrigin(e.File, "AuroraLegacy"));
                if (successor) candidates.RemoveAll(e => older.Contains(e));
                bool authoritative = successor && !unreadableIds.Contains(group.Key) &&
                    candidates.Select(e => e.Declaration.Fingerprint).Distinct().Count() == 1;
                if (authoritative || skipUnusable)
                {
                    // Retention is here to stop a broken supplier erasing a working definition,
                    // not to settle a disagreement between readable ones. Consulting it for every
                    // collision pins whichever definition was imported first, so a book's override
                    // could never take effect once the database had seen the original.
                    var previous = authoritative || !unreadableIds.Contains(group.Key)
                        ? null : previousDefinition?.Invoke(ids[0]);
                    var selected = candidates.FirstOrDefault(e => previous != null && e.File.Path == previous.Path);
                    if (selected.File == null) selected = candidates[0];
                    var target = selected.Element;
                    string xml = selected.Declaration.Xml;
                    if (previous != null)
                    {
                        target = XElement.Parse(previous.Xml, LoadOptions.PreserveWhitespace);
                        selected.Element.ReplaceWith(target);
                        xml = previous.Xml;
                        retainedIds.Add(ids[0]);
                    }
                    seen.Add(ids[0], selected.Declaration with { Xml = xml, Fingerprint = LocalCorrectionDocument.Fingerprint(target) });
                    targets.Add(ids[0], target);
                    declarations.Add(selected.Declaration);
                    string kind = authoritative ? "upstream-successor" : previous != null ? "retained" : "provisional";
                    var suppliersToRemember = entries.Select(e => new Supplier(e.File.Path))
                        .Concat(previous?.Suppliers.Where(s => discarded.Contains(s.Path)) ?? []).Distinct().ToArray();
                    Resolutions.Add(new(selected.Declaration, kind, previous?.Path, xml, suppliersToRemember));
                    string decision = authoritative ? "Used the AuroraLegacy successor definition."
                        : previous != null ? $"Kept the last-known working definition from {previous.Path}."
                        : $"Provisionally selected {selected.File.Relative}, declaration {selected.Declaration.Ordinal}, by stable path/declaration order.";
                    foreach (var rejected in entries.Where(e => e.Declaration != selected.Declaration))
                    {
                        if (previous == null && rejected.Declaration.Fingerprint == selected.Declaration.Fingerprint)
                        {
                            declarations.Add(rejected.Declaration);
                            rejected.Element.Remove();
                            continue; // Harmless identical copies consolidate silently, retaining provenance.
                        }
                        RejectedDeclarations.Add(rejected.Declaration);
                        rejected.Element.Remove();
                        RecordSkip(rejected.File, authoritative ? "superseded-definition" : "definition-collision",
                            detail + " " + decision + (authoritative ? "" : " Review the skipped alternatives to resolve this collision."), selected.File.Path);
                    }
                    if (previous != null)
                        RecordSkip(selected.File, "definition-collision", detail + " " + decision + " The current candidate was also held for review.", previous.Path);
                    else if (entries.Count == 1 && unreadableIds.Contains(group.Key))
                        RecordSkip(selected.File, "definition-collision", detail + " A former supplier is unreadable. " + decision);
                    continue;
                }
            }
            if (!skipUnusable && !quarantineConflicts && ids.Any(id => !previouslyUnavailableIds.Contains(id)))
                throw new InvalidDataException(detail + " Resolve the conflict before refreshing; the existing database was preserved and no winner was selected.");

            // Include trimmed aliases because consumers trim declaration IDs. This masks
            // availability only; it never rewrites authored identities or references.
            foreach (string id in ids.Concat(ids.Select(id => id.Trim())).Distinct(StringComparer.Ordinal))
            {
                UnavailableIds.Add(id);
                UnavailableReasons.Add(id, detail + " This definition is unavailable until the conflict is resolved and the database refreshed.");
                RecordSkip(entries[0].File, "definition-conflict", UnavailableReasons[id], entries.Skip(1).FirstOrDefault().File?.Path);
            }
            UnavailableDeclarations.AddRange(entries.Select(d => d.Declaration));
            foreach (var entry in entries) entry.Element.Remove();
        }

        // All base/corrected declarations exist before extensions are interpreted.
        foreach (var (file, xml) in documents)
        {
            int ordinal = 0;
            foreach (var append in xml.Root!.Elements("append").ToArray())
            {
                string id = (string?)append.Attribute("id") ?? "";
                string raw = append.ToString(SaveOptions.DisableFormatting);
                string? diagnostic = null;
                if (retainedIds.Contains(id))
                {
                    diagnostic = $"append-target-retained: {file.Path}, append {ordinal}, target '{id}' keeps its last-known effective definition, including earlier extensions.";
                    RecordSkip(file, "append", diagnostic);
                    Appends.Add(new(file, ordinal++, id, raw, "skipped", diagnostic));
                    append.Remove();
                    continue;
                }
                if (UnavailableIds.Contains(id) || UnavailableIds.Any(unavailable => string.Equals(unavailable.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    diagnostic = $"append-target-unavailable: {file.Path}, append {ordinal}, target '{id}' has conflicting definitions. The operation is retained without applying it.";
                    Appends.Add(new(file, ordinal++, id, raw, "unavailable-target", diagnostic));
                    append.Remove();
                    continue;
                }
                try
                {
                    if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"append-missing-id: {file.Path}, append {ordinal}.");
                    if (targets.TryGetValue(id, out var target))
                    {
                        try
                        {
                            var merged = ContentAppendComposer.Apply(target, append);
                            AuroraCatalogBuilder.ValidateDeclaration(merged);
                            target.ReplaceWith(merged);
                            targets[id] = merged;
                        }
                        catch (InvalidDataException ex)
                        { throw new InvalidDataException($"append-conflict: {file.Path}, append {ordinal}, target '{id}': {ex.Message}", ex); }
                    }
                    else
                    {
                        // Preserve unbound operations for consumers with intrinsic/runtime
                        // definitions and for authoring review; never infer a replacement ID.
                        try
                        {
                            var preview = ContentAppendComposer.Apply(new XElement("element", new XAttribute("id", id),
                                new XAttribute("name", "Unresolved append target"),
                                new XAttribute("type", (string?)append.Attribute("type") ?? "Class Feature")), append);
                            AuroraCatalogBuilder.ValidateDeclaration(preview);
                        }
                        catch (InvalidDataException ex)
                        { throw new InvalidDataException($"append-conflict: {file.Path}, append {ordinal}, target '{id}': {ex.Message}", ex); }
                        diagnostic = $"append-target-unresolved: {file.Path}, append {ordinal}, target '{id}'. Supply the exact definition or correct this reference; the operation is retained without applying it.";
                    }
                }
                catch (InvalidDataException ex) when (skipUnusable)
                {
                    // One operation this file cannot perform. The rest of the file still stands and
                    // the target keeps whatever it had before this append was written.
                    RecordSkip(file, "append", ex.Message);
                    Appends.Add(new(file, ordinal++, id, raw, "skipped", ex.Message));
                    append.Remove();
                    continue;
                }
                Appends.Add(new(file, ordinal++, id, raw, diagnostic == null ? "applied" : "unresolved-target", diagnostic));
                append.Remove();
            }
        }
        foreach (var (id, target) in targets)
            Finalized.Add(new(id, seen[id].Path, seen[id].Xml, target.ToString(SaveOptions.DisableFormatting)));
        foreach (var (file, xml) in documents)
        {
            string destination = Path.Combine(Stage(file), file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, xml.ToString(SaveOptions.DisableFormatting));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }

    private static bool HasRepositoryOrigin(XDocument document, string owner)
    {
        string? url = (string?)document.Root?.Element("info")?.Element("update")?.Element("file")?.Attribute("url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "raw.githubusercontent.com") return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 && parts[0].Equals(owner, StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("elements", StringComparison.OrdinalIgnoreCase);
    }
}
