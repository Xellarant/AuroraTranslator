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
    /// <summary>Files (and append operations) left out of the import, with the reason for each.</summary>
    internal List<ContentImportSkip> Skipped { get; } = [];
    private readonly bool skipUnusable;
    private readonly HashSet<string> discarded = new(StringComparer.OrdinalIgnoreCase);

    private ContentPreparation(bool skipUnusable) => this.skipUnusable = skipUnusable;

    /// <param name="skipUnusable">
    /// Leaves a file that cannot be used out of the import instead of refusing the whole import,
    /// recording why in <see cref="Skipped"/>. Off by default: an import that says it succeeded
    /// has read every file it was given.
    /// </param>
    internal static ContentPreparation Prepare(IReadOnlyList<string> roots, CancellationToken cancellation = default,
        ImportProgressReporter? progress = null, bool skipUnusable = false)
    {
        var prepared = new ContentPreparation(skipUnusable);
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
    /// A fault one file is answerable for: "unreadable" when the file cannot be interpreted,
    /// "conflict" when it redefines an element another file already declared differently.
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
            try
            {
                var evaluation = LocalCorrectionDocument.FromFile(Path.Combine(Stage(file), file.Relative), Stage(file));
                if (evaluation != null) Managed.Add(new(file, evaluation));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is System.Xml.XmlException || ex is IOException)
            {
                string detail = $"Correction preparation failed for {file.Path}: {ex.Message}";
                if (!skipUnusable) throw new InvalidDataException(detail, ex);
                Discard(file, "correction", detail);
            }
        }
        var overlaps = Managed.GroupBy(m => LocalCorrectionDocument.ResolveSourcePath(m.File.Root, m.Evaluation.SourcePath), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        foreach (var overlapping in overlaps)
        {
            string detail = $"Multiple managed files target {overlapping.Key}: {string.Join(", ", overlapping.Select(m => m.File.Path))}. Consolidate or review them first.";
            if (!skipUnusable) throw new InvalidDataException(detail);
            // No winner can be chosen between them, so none of them corrects anything this time.
            foreach (var entry in overlapping) { Discard(entry.File, "correction", detail); Managed.Remove(entry); }
        }
        var partials = Managed.SelectMany(m => m.Evaluation.Corrections).Where(c => c.Group != null).GroupBy(c => c.Group).Where(g => g.Select(c => c.State).Distinct().Count() > 1).ToList();
        foreach (var partial in partials)
        {
            string detail = $"Related corrections in group '{partial.Key}' must be reviewed together.";
            if (!skipUnusable) throw new InvalidDataException(detail);
            foreach (var entry in Managed.Where(m => m.Evaluation.Corrections.Any(c => c.Group == partial.Key)).ToList())
            { Discard(entry.File, "correction", detail); Managed.Remove(entry); }
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
          catch (Exception ex) when (CanSkip(ex))
          {
            Discard(entry.File, "correction", $"Correction staging failed for {entry.File.Path}: {ex.Message}");
            Managed.Remove(entry);
          }
        }
    }

    private void FinalizeDeclarations()
    {
        var seen = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var spellingOrigins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var documents = new Dictionary<FileState, XDocument>();
        var targets = new Dictionary<string, XElement>(StringComparer.Ordinal);
        // Canonical representatives and all links are independent of app preferences.
        foreach (var file in Files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            if (discarded.Contains(file.Path)) continue;
            // Nothing a file declares counts until the file has been read through, so a file that
            // turns out to be unusable leaves no half-read remains in the catalog behind it.
            int declarationMark = declarations.Count;
            var claimedIds = new List<string>();
            var claimedSpellings = new List<string>();
            fault = null;
            try
            {
                string stagedPath = Path.Combine(Stage(file), file.Relative);
                var xml = LocalCorrectionDocument.Parse(File.ReadAllText(stagedPath), file.Path);
                documents.Add(file, xml);
                bool ignored = bool.TryParse((string?)xml.Root!.Attribute("ignore"), out bool flag) && flag;
                if (ignored) xml.Root.Elements().Where(e => e.Name != "info").Remove();
                int ordinal = 0;
                foreach (var element in xml.Root.Elements("element").ToList())
                {
                    string id = (string?)element.Attribute("id") ?? "";
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace((string?)element.Attribute("type")))
                        throw Fault("unreadable", $"Missing element id/type in {file.Path}, declaration {ordinal}.");
                    if (spellings.TryGetValue(id.Trim(), out string? spelling) && spelling != id)
                        throw Fault("conflict", $"Identity spelling requires review: '{spelling}' and '{id}' in {file.Path}. No case/whitespace normalization was applied.", spellingOrigins.GetValueOrDefault(id.Trim()));
                    if (!spellings.ContainsKey(id.Trim()))
                    { claimedSpellings.Add(id.Trim()); spellingOrigins[id.Trim()] = file.Path; }
                    spellings[id.Trim()] = id;
                    var declaration = new Declaration(id, file.Path, file.Hash, ordinal++, LocalCorrectionDocument.Fingerprint(element), element.ToString(SaveOptions.DisableFormatting));
                    declarations.Add(declaration);
                    if (seen.TryGetValue(id, out var previous))
                    {
                        if (previous.Fingerprint != declaration.Fingerprint)
                            throw Fault("conflict", $"duplicate-element-id: '{id}' has conflicting definitions: {previous.Path} declaration {previous.Ordinal} ({previous.Fingerprint}) and {file.Path} declaration {declaration.Ordinal} ({declaration.Fingerprint}). Review these revisions and supply an explicit correction; no winner was selected.", previous.Path);
                        element.Remove();
                    }
                    else { seen.Add(id, declaration); targets.Add(id, element); claimedIds.Add(id); }
                }
            }
            catch (Exception ex) when (CanSkip(ex))
            {
                declarations.RemoveRange(declarationMark, declarations.Count - declarationMark);
                foreach (string id in claimedIds) { seen.Remove(id); targets.Remove(id); }
                foreach (string key in claimedSpellings) { spellings.Remove(key); spellingOrigins.Remove(key); }
                documents.Remove(file);
                Discard(file, fault?.Kind ?? "unreadable", ex.Message, fault?.RelatedPath);
            }
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
