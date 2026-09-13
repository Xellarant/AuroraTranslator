#nullable enable
using Builder.Data.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Xml.Linq;

namespace AuroraTranslator.Content;

/// <summary>Owns correction decisions and finalized XML. No database writes or acceptance here.</summary>
internal sealed class ContentPreparation : IDisposable
{
    internal sealed record FileState(string Root, string Relative, string Path, string Hash);
    internal sealed record ManagedFile(FileState File, LocalCorrectionEvaluation Evaluation);
    internal sealed record Declaration(string Id, string Path, string Hash, int Ordinal, string Fingerprint, string Xml);
    private readonly string work = Path.Combine(Path.GetTempPath(), "translator-preparation-" + Guid.NewGuid().ToString("N"));
    internal List<FileState> Files { get; } = [];
    internal List<ManagedFile> Managed { get; } = [];
    internal List<string> StagedRoots { get; } = [];
    private readonly Dictionary<string, string> stages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Declaration> declarations = [];
    internal IReadOnlyList<Declaration> Declarations => declarations;

    internal static ContentPreparation Prepare(IReadOnlyList<string> roots, CancellationToken cancellation = default,
        IReadOnlySet<string>? disabledPackages = null)
    {
        var prepared = new ContentPreparation();
        try
        {
            prepared.Capture(roots, cancellation);
            prepared.Evaluate(cancellation);
            prepared.FinalizeDeclarations(disabledPackages);
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }

    private void Capture(IReadOnlyList<string> roots, CancellationToken cancellation)
    {
        foreach (var inputRoot in roots)
        {
            string root = Path.GetFullPath(inputRoot);
            string stage = Path.Combine(work, StagedRoots.Count.ToString(), new DirectoryInfo(root).Name);
            StagedRoots.Add(stage);
            if (!stages.TryAdd(root, stage)) throw new InvalidDataException($"Repeated content root: {root}");
            Directory.CreateDirectory(stage);
            foreach (var path in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                cancellation.ThrowIfCancellationRequested();
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
            File.WriteAllText(LocalCorrectionDocument.ResolveSourcePath(stage, entry.Evaluation.SourcePath), entry.Evaluation.EffectiveXml);
            var local = LocalCorrectionDocument.Parse(entry.Evaluation.LocalXml);
            local.Root!.Elements().Where(e => e.Name != "info").Remove();
            File.WriteAllText(Path.Combine(stage, entry.File.Relative), local.ToString(SaveOptions.DisableFormatting));
        }
    }

    private void FinalizeDeclarations(IReadOnlySet<string>? disabledPackages)
    {
        var seen = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Identical content can use any enabled supplier, but a disabled package
        // must not hide an identical declaration supplied by an enabled package.
        foreach (var file in Files.OrderBy(f => disabledPackages?.Contains(AuroraSqliteImporter.GetContentPackageKey(f.Relative)) == true))
        {
            string stagedPath = Path.Combine(Stage(file), file.Relative);
            var xml = LocalCorrectionDocument.Parse(File.ReadAllText(stagedPath));
            bool ignored = bool.TryParse((string?)xml.Root!.Attribute("ignore"), out bool flag) && flag;
            if (ignored) xml.Root.Elements().Where(e => e.Name != "info").Remove();
            // The existing Translator catalog has no document-level append executor.
            // Refuse these inputs instead of silently discarding correction effects.
            if (xml.Root.Elements().Any(e => e.Name == "append"))
                throw new InvalidDataException($"Unsupported document-level append in {file.Path}; preserve the file and import through an append-capable preparation path.");
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
                else seen.Add(id, declaration);
            }
            File.WriteAllText(stagedPath, xml.ToString(SaveOptions.DisableFormatting));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
    }
}
