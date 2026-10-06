#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Aurora.Content.Contracts;

namespace Aurora.Content;

/// <summary>The exact local and authoritative bytes reviewed before a correction edit.</summary>
public sealed record ContentCorrectionEditSnapshot(string FilePath, string SourcePath,
    string LocalHash, string UpstreamHash, string LocalXml, string UpstreamXml);

/// <summary>A complete same-file review unit and the actions currently available for it.</summary>
public sealed record ContentCorrectionReviewGroup(string? Group, IReadOnlyList<string> CorrectionKeys,
    IReadOnlyList<LocalCorrection> Corrections, bool CanApproveLocal, bool CanAcceptUpstream,
    IReadOnlyList<string> BlockingReasons);

public sealed record ContentCorrectionReviewDetails(LocalCorrectionEvaluation Evaluation,
    IReadOnlyList<ContentCorrectionReviewGroup> Groups);

/// <summary>
/// Reviews and edits existing local corrections. Does not import content, retire files,
/// or modify the embedded original baseline.
/// Group peer checks cover the edited file's content root; hosts with linked groups across
/// multiple roots must coordinate those edits separately.
/// </summary>
public static class ContentCorrectionEditor
{
    private static readonly XNamespace Ns = LocalCorrectionDocument.Namespace;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Captures XML and hashes for review. The original effective content need not evaluate
    /// successfully: replacing a definition can repair a collision with a newer upstream file.
    /// </summary>
    public static ContentCorrectionEditSnapshot Read(string filePath)
    {
        string path = Path.GetFullPath(filePath);
        string root = CheckLocalPath(path);
        var local = ReadBytes(path);
        var section = CorrectionSection(local.Xml, path);
        string source = LocalCorrectionDocument.ResolveSourcePath(root, (string?)section.Attribute("source-path") ?? "");
        var upstream = ReadBytes(source);
        var review = new ContentCorrectionEditSnapshot(path, source, local.Hash, upstream.Hash, local.Xml, upstream.Xml);
        CheckHashes(review);
        return review;
    }

    /// <summary>
    /// Returns amended local XML and its evaluated effective XML without writing. Dictionary keys
    /// are existing correction keys; values are complete, unnamespaced element XML with unchanged IDs.
    /// Accepted entries and their same-file group peers return to review-pending.
    /// </summary>
    public static LocalCorrectionEvaluation PreviewReplacement(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyDictionary<string, string> replacementsByCorrectionKey)
    {
        ArgumentNullException.ThrowIfNull(replacementsByCorrectionKey);
        var replacements = replacementsByCorrectionKey.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var current = ReadReviewed(reviewed);
        var draft = Prepare(current, replacements);
        CheckGroups(current, draft, replacements.Keys);
        CheckHashes(current);
        return draft;
    }

    /// <summary>
    /// Recomputes and validates the proposed edit against the reviewed hashes, then replaces only
    /// the local XML file. The host must refresh its database separately after saving.
    /// Changes detected before replacement refuse the save and leave the existing file in place.
    /// </summary>
    public static LocalCorrectionEvaluation Replace(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyDictionary<string, string> replacementsByCorrectionKey)
    {
        ArgumentNullException.ThrowIfNull(replacementsByCorrectionKey);
        var replacements = replacementsByCorrectionKey.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var current = ReadReviewed(reviewed);
        var draft = Prepare(current, replacements);
        var groupFiles = CheckGroups(current, draft, replacements.Keys);
        Save(current, draft, replacements.Keys, groupFiles, requireSameFile: false);
        return draft;
    }

    /// <summary>
    /// Reads same-file review groups and action eligibility from the current reviewed bytes.
    /// Structural contract errors throw; disabled, typed-invalid, and external group issues
    /// are returned as blocking reasons. No content or database is changed.
    /// </summary>
    public static ContentCorrectionReviewDetails ReadReview(ContentCorrectionEditSnapshot reviewed)
    {
        var current = ReadReviewed(reviewed);
        var evaluation = LocalCorrectionDocument.Evaluate(current.LocalXml, current.UpstreamXml);
        var common = new List<string>();
        try { ValidateReviewContent(evaluation); }
        catch (InvalidDataException ex) { common.Add(ex.Message); }
        var groups = new List<ContentCorrectionReviewGroup>();
        foreach (var group in evaluation.Corrections.GroupBy(c => (c.Group, Key: c.Group == null ? c.Key : null)))
        {
            var members = group.ToArray();
            var keys = members.Select(c => c.Key).ToArray();
            var reasons = new List<string>(common);
            try { CheckGroups(current, evaluation, keys, requireSameFile: true); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            { reasons.Add(ex.Message); }
            bool valid = reasons.Count == 0;
            bool approve = valid;
            if (valid)
            {
                try { ValidateEffective(LocalCorrectionDocument.ApproveLocal(current.LocalXml, current.UpstreamXml, keys)); }
                catch (InvalidDataException ex)
                {
                    approve = false;
                    reasons.Add("Local approval is unavailable: " + ex.Message);
                }
            }
            bool incorporated = keys.All(evaluation.IncorporatedKeys.Contains);
            if (!incorporated)
                reasons.Add("The current authoritative file does not incorporate every correction in this group. Keep the local correction or review the source first.");
            bool accept = valid && incorporated;
            if (accept)
            {
                try { ValidateEffective(LocalCorrectionDocument.AcceptUpstream(current.LocalXml, current.UpstreamXml, keys)); }
                catch (InvalidDataException ex)
                {
                    accept = false;
                    reasons.Add("Upstream acceptance is unavailable: " + ex.Message);
                }
            }
            groups.Add(new(group.Key.Group, keys, members, approve, accept, reasons));
        }
        CheckHashes(current);
        return new(evaluation, groups);
    }

    /// <summary>Previews acknowledgement of complete same-file groups, keeping local content active.</summary>
    public static LocalCorrectionEvaluation PreviewApproveLocal(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyCollection<string> correctionKeys) => Decide(reviewed, correctionKeys, accept: false, save: false);

    /// <summary>Saves acknowledgement tied to the reviewed definitions. A database refresh remains separate.</summary>
    public static LocalCorrectionEvaluation ApproveLocal(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyCollection<string> correctionKeys) => Decide(reviewed, correctionKeys, accept: false, save: true);

    /// <summary>Previews releasing complete groups only when the current upstream XML incorporates them.</summary>
    public static LocalCorrectionEvaluation PreviewAcceptUpstream(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyCollection<string> correctionKeys) => Decide(reviewed, correctionKeys, accept: true, save: false);

    /// <summary>
    /// Saves explicit upstream acceptance for incorporated same-file groups. The file is retained;
    /// successful import must separately validate, activate, and authorize retirement.
    /// </summary>
    public static LocalCorrectionEvaluation AcceptUpstream(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyCollection<string> correctionKeys) => Decide(reviewed, correctionKeys, accept: true, save: true);

    private static LocalCorrectionEvaluation Decide(ContentCorrectionEditSnapshot reviewed,
        IReadOnlyCollection<string> correctionKeys, bool accept, bool save)
    {
        ArgumentNullException.ThrowIfNull(correctionKeys);
        var keys = correctionKeys.ToArray();
        var current = ReadReviewed(reviewed);
        var evaluation = LocalCorrectionDocument.Evaluate(current.LocalXml, current.UpstreamXml);
        ValidateReviewContent(evaluation);
        // The pure operation validates exact keys, duplicates, and complete same-file groups.
        var draft = accept
            ? LocalCorrectionDocument.AcceptUpstream(current.LocalXml, current.UpstreamXml, keys)
            : LocalCorrectionDocument.ApproveLocal(current.LocalXml, current.UpstreamXml, keys);
        if (accept && keys.Any(key => !evaluation.IncorporatedKeys.Contains(key)))
            throw new InvalidDataException("The current authoritative file must incorporate every selected correction before accepting upstream. Keep the local correction or review the source first.");
        ValidateEffective(draft);
        var groupFiles = CheckGroups(current, draft, keys, requireSameFile: true);
        CheckHashes(current);
        if (save) Save(current, draft, keys, groupFiles, requireSameFile: true);
        return draft;
    }

    private static void ValidateReviewContent(LocalCorrectionEvaluation evaluation)
    {
        var local = LocalCorrectionDocument.Parse(evaluation.LocalXml);
        if (bool.TryParse((string?)local.Root!.Attribute("ignore"), out bool ignored) && ignored)
            throw new InvalidDataException("Enable the local correction file before reviewing or accepting its corrections.");
        foreach (var correction in evaluation.Corrections.Where(c => c.Operation != "remove"))
        {
            string id = correction.Operation == "rename" ? correction.ReplacementId! : correction.TargetId;
            Validate(local.Root.Elements("element").Single(e => (string?)e.Attribute("id") == id));
        }
        ValidateEffective(evaluation);
    }

    private static void ValidateEffective(LocalCorrectionEvaluation evaluation)
    {
        foreach (var element in LocalCorrectionDocument.Parse(evaluation.EffectiveXml).Root!.Elements("element"))
            Validate(element);
    }

    private static void Save(ContentCorrectionEditSnapshot current, LocalCorrectionEvaluation draft,
        IEnumerable<string> keys, Dictionary<string, string> groupFiles, bool requireSameFile)
    {
        string temporary = current.FilePath + ".edit-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, draft.LocalXml);
            // Re-read from disk rather than trusting the public snapshot or a previous preview.
            _ = ReadReviewed(current);
            var latestGroupFiles = CheckGroups(current, draft, keys, requireSameFile);
            if (groupFiles.Count != latestGroupFiles.Count || groupFiles.Any(p =>
                !latestGroupFiles.TryGetValue(p.Key, out var hash) || hash != p.Value))
                throw new IOException("Correction group files changed while saving. Read and review the correction again.");
            CheckHashes(current);
            File.Move(temporary, current.FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static LocalCorrectionEvaluation Prepare(ContentCorrectionEditSnapshot current,
        IReadOnlyDictionary<string, string> replacements)
    {
        var draft = LocalCorrectionDocument.ReplaceDefinitions(current.LocalXml, current.UpstreamXml, replacements);
        // Reuse the importer's typed declaration validation instead of inventing editor-only rules.
        // Check selected local definitions even when the whole correction file is disabled.
        var local = LocalCorrectionDocument.Parse(draft.LocalXml);
        foreach (var correction in draft.Corrections.Where(c => replacements.ContainsKey(c.Key)))
        {
            string id = correction.Operation == "rename" ? correction.ReplacementId! : correction.TargetId;
            Validate(local.Root!.Elements("element").Single(e => (string?)e.Attribute("id") == id));
        }
        foreach (var element in LocalCorrectionDocument.Parse(draft.EffectiveXml).Root!.Elements("element"))
            Validate(element);
        return draft;
    }

    private static void Validate(XElement element)
    {
        try { AuroraCatalogBuilder.ValidateDeclaration(element); }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException)
        { throw new InvalidDataException($"Correction would produce an invalid definition '{(string?)element.Attribute("id")}': {ex.Message}", ex); }
    }

    private static ContentCorrectionEditSnapshot ReadReviewed(ContentCorrectionEditSnapshot reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var current = Read(reviewed.FilePath);
        if (!Paths.Equals(current.SourcePath, reviewed.SourcePath) || current.LocalHash != reviewed.LocalHash
            || current.UpstreamHash != reviewed.UpstreamHash)
            throw new IOException("Local correction or upstream content changed since review. Read and review the correction again.");
        return current;
    }

    private static void CheckHashes(ContentCorrectionEditSnapshot review)
    {
        if (LocalCorrectionDocument.FileFingerprint(review.FilePath) != review.LocalHash
            || LocalCorrectionDocument.FileFingerprint(review.SourcePath) != review.UpstreamHash)
            throw new IOException("Local correction or upstream content changed during review. Read and review the correction again.");
    }

    private static Dictionary<string, string> CheckGroups(ContentCorrectionEditSnapshot current,
        LocalCorrectionEvaluation draft, IEnumerable<string> keys, bool requireSameFile = false)
    {
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        var groups = draft.Corrections.Where(c => selected.Contains(c.Key) && c.Group != null)
            .Select(c => c.Group!).ToHashSet(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(Paths);
        if (groups.Count == 0) return files;
        string root = CheckLocalPath(current.FilePath);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "user", "local"), "*.xml", SearchOption.AllDirectories))
        {
            if (Paths.Equals(Path.GetFullPath(path), current.FilePath)) continue;
            CheckLocalPath(path);
            var input = ReadBytes(path);
            files.Add(Path.GetFullPath(path), input.Hash);
            try
            {
                var document = LocalCorrectionDocument.Parse(input.Xml, path);
                if (!document.Root!.Elements().Any(e => e.Name.LocalName == "corrections")) continue;
                var section = CorrectionSection(input.Xml, path);
                foreach (var node in section.Elements(Ns + "correction"))
                {
                    string? group = (string?)node.Attribute("group");
                    if (group != null && groups.Contains(group) && requireSameFile)
                        throw new InvalidDataException($"Correction group '{group}' also contains corrections in '{path}'. Coordinated approval and acceptance across files are not supported; no correction was saved.");
                    if (group != null && groups.Contains(group) && (string?)node.Attribute("state") != "review-pending")
                        throw new InvalidDataException($"Correction group '{group}' also needs reopening in '{path}'. Coordinated edits across files are not supported; no correction was saved.");
                }
            }
            catch (XmlException ex)
            { throw new InvalidDataException($"Cannot verify correction groups in '{path}'. Repair its XML before editing this grouped correction.", ex); }
        }
        return files;
    }

    private static XElement CorrectionSection(string xml, string path)
    {
        var sections = LocalCorrectionDocument.Parse(xml, path).Root!.Elements()
            .Where(e => e.Name.LocalName == "corrections").ToArray();
        if (sections.Length != 1 || sections[0].Name != Ns + "corrections" || (string?)sections[0].Attribute("version") != "1")
            throw new InvalidDataException($"Expected one supported correction section in '{path}'.");
        return sections[0];
    }

    private static string CheckLocalPath(string path)
    {
        string root = LocalCorrectionDocument.FindContentRoot(path)
            ?? throw new InvalidDataException("Correction editing requires an existing user/local correction XML file.");
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Correction editing requires a correction XML file.");
        for (FileSystemInfo? entry = new FileInfo(path); entry != null;
             entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
        {
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Correction editing cannot traverse symbolic links or junctions.");
            if (Paths.Equals(entry.FullName, root)) break;
        }
        return root;
    }

    private static (string Xml, string Hash) ReadBytes(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (reader.ReadToEnd(), Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
