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
    internal sealed record Declaration(string Id, string Path, string Hash, int Ordinal, string Fingerprint, string Xml);
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
        bool quarantineConflicts = false, IReadOnlySet<string>? previouslyUnavailableIds = null)
    {
        var prepared = new ContentPreparation(skipUnusable, quarantineConflicts, previouslyUnavailableIds);
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
                byte[] bytes = File.ReadAllBytes(path);
                string relative = Path.GetRelativePath(root, path);
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

        // Inspect every supplier before selecting identical representatives. No path-order
        // winner is meaningful when definitions differ, even within one input file.
        var protectedIds = Managed.SelectMany(m => m.Evaluation.Corrections)
            .SelectMany(c => new[] { c.TargetId, c.ReplacementId }).Where(id => id != null)
            .Select(id => id!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in collected.GroupBy(d => d.Declaration.Id.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var entries = group.ToList();
            var ids = entries.Select(d => d.Declaration.Id).Distinct(StringComparer.Ordinal).ToList();
            bool conflict = ids.Count > 1 || entries.Select(d => d.Declaration.Fingerprint).Distinct().Skip(1).Any();
            if (!conflict)
            {
                var first = entries[0];
                seen.Add(first.Declaration.Id, first.Declaration);
                targets.Add(first.Declaration.Id, first.Element);
                declarations.AddRange(entries.Select(d => d.Declaration));
                foreach (var duplicate in entries.Skip(1)) duplicate.Element.Remove();
                continue;
            }

            string suppliers = string.Join("; ", entries.Select(d => $"{d.File.Path} declaration {d.Declaration.Ordinal} ({d.Declaration.Fingerprint})"));
            string detail = $"duplicate-element-id: {string.Join(", ", ids.Select(id => $"'{id}'"))} has conflicting definitions: {suppliers}.";
            if (protectedIds.Contains(group.Key))
                throw new InvalidDataException(detail + " An explicit correction targets this identity; resolve the conflict before activation. The existing database was preserved.");
            if (!quarantineConflicts && ids.Any(id => !previouslyUnavailableIds.Contains(id)))
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
                            ContentAppendComposer.Apply(new XElement("element", new XAttribute("id", id),
                                new XAttribute("type", (string?)append.Attribute("type") ?? "")), append);
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
            File.WriteAllText(Path.Combine(Stage(file), file.Relative), xml.ToString(SaveOptions.DisableFormatting));
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}
