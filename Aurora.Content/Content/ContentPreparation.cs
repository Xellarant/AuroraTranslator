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

    internal static ContentPreparation Prepare(IReadOnlyList<string> roots, CancellationToken cancellation = default,
        ImportProgressReporter? progress = null)
    {
        var prepared = new ContentPreparation();
        try
        {
            prepared.Capture(roots, cancellation, progress);
            prepared.Evaluate(cancellation);
            prepared.FinalizeDeclarations();
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
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
            { throw new InvalidDataException($"Correction preparation failed for {file.Path}: {ex.Message}", ex); }
        }
        var overlapping = Managed.GroupBy(m => LocalCorrectionDocument.ResolveSourcePath(m.File.Root, m.Evaluation.SourcePath), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (overlapping != null)
            throw new InvalidDataException($"Multiple managed files target {overlapping.Key}: {string.Join(", ", overlapping.Select(m => m.File.Path))}. Consolidate or review them first.");
        var partial = Managed.SelectMany(m => m.Evaluation.Corrections).Where(c => c.Group != null).GroupBy(c => c.Group).FirstOrDefault(g => g.Select(c => c.State).Distinct().Count() > 1);
        if (partial != null) throw new InvalidDataException($"Related corrections in group '{partial.Key}' must be reviewed together.");
        foreach (var entry in Managed)
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
    }

    private void FinalizeDeclarations()
    {
        var seen = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var documents = new Dictionary<FileState, XDocument>();
        var targets = new Dictionary<string, XElement>(StringComparer.Ordinal);
        // Canonical representatives and all links are independent of app preferences.
        foreach (var file in Files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            string stagedPath = Path.Combine(Stage(file), file.Relative);
            var xml = LocalCorrectionDocument.Parse(File.ReadAllText(stagedPath));
            documents.Add(file, xml);
            bool ignored = bool.TryParse((string?)xml.Root!.Attribute("ignore"), out bool flag) && flag;
            if (ignored) xml.Root.Elements().Where(e => e.Name != "info").Remove();
            int ordinal = 0;
            foreach (var element in xml.Root.Elements("element").ToList())
            {
                string id = (string?)element.Attribute("id") ?? "";
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace((string?)element.Attribute("type")))
                    throw new InvalidDataException($"Missing element id/type in {file.Path}, declaration {ordinal}.");
                if (spellings.TryGetValue(id.Trim(), out string? spelling) && spelling != id)
                    throw new InvalidDataException($"Identity spelling requires review: '{spelling}' and '{id}' in {file.Path}. No case/whitespace normalization was applied.");
                spellings[id.Trim()] = id;
                var declaration = new Declaration(id, file.Path, file.Hash, ordinal++, LocalCorrectionDocument.Fingerprint(element), element.ToString(SaveOptions.DisableFormatting));
                declarations.Add(declaration);
                if (seen.TryGetValue(id, out var previous))
                {
                    if (previous.Fingerprint != declaration.Fingerprint)
                        throw new InvalidDataException($"duplicate-element-id: '{id}' has conflicting definitions: {previous.Path} declaration {previous.Ordinal} ({previous.Fingerprint}) and {file.Path} declaration {declaration.Ordinal} ({declaration.Fingerprint}). Review these revisions and supply an explicit correction; no winner was selected.");
                    element.Remove();
                }
                else { seen.Add(id, declaration); targets.Add(id, element); }
            }
        }

        // All base/corrected declarations exist before extensions are interpreted.
        foreach (var (file, xml) in documents)
        {
            int ordinal = 0;
            foreach (var append in xml.Root!.Elements("append").ToArray())
            {
                string id = (string?)append.Attribute("id") ?? "";
                if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"append-missing-id: {file.Path}, append {ordinal}.");
                string raw = append.ToString(SaveOptions.DisableFormatting);
                string? diagnostic = null;
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
