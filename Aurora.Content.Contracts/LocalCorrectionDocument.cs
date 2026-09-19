#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Builder.Data.Files;

public sealed record LocalCorrection(string Key, string Operation, string TargetId,
    string? ReplacementId, string? OriginalFingerprint, string State = "review-pending",
    string? Group = null, string? Reason = null);

public sealed record LocalCorrectionEvaluation(string SourcePath, string BaselineXml,
    string LocalXml, string UpstreamXml, string EffectiveXml,
    IReadOnlyList<LocalCorrection> Corrections, IReadOnlyList<string> ReviewReasons,
    bool CanRetire, IReadOnlyList<string> SuppressedIds);

/// <summary>File-level correction intent. XML is durable; database rows are a mirror.</summary>
public static class LocalCorrectionDocument
{
    public const string Namespace = "urn:aurora-lights:corrections:1";
    private static readonly XNamespace Ns = Namespace;

    public static XDocument Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "elements")
            throw new InvalidDataException("Correction content must have an unnamespaced elements root.");
        return document;
    }

    public static bool HasMetadata(string xml) => Parse(xml).Root!.Elements()
        .Any(e => e.Name.LocalName == "corrections");

    // Conservative structural comparison: attribute order is immaterial; text is not.
    public static string Fingerprint(XElement element)
    {
        object Shape(XElement e) => new object[] { e.Name.ToString(),
            e.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
                .Select(a => new[] { a.Name.ToString(), a.Value }).ToArray(),
            e.Nodes().Where(n => n is XElement || n is XText)
                .Select(n => n is XElement child ? Shape(child) : (object)((XText)n).Value).ToArray() };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Shape(element)))));
    }

    public static string Create(string localXml, string baselineXml, string sourcePath,
        IEnumerable<LocalCorrection> corrections)
    {
        var document = Parse(localXml);
        if (HasMetadata(localXml)) throw new InvalidDataException("File already contains correction metadata.");
        Parse(baselineXml);
        var section = new XElement(Ns + "corrections", new XAttribute(XNamespace.Xmlns + "al", Namespace),
            new XAttribute("version", "1"), new XAttribute("source-path", sourcePath),
            new XElement(Ns + "baseline", new XAttribute("encoding", "escaped-xml"), baselineXml));
        foreach (var correction in corrections)
            section.Add(new XElement(Ns + "correction", new XAttribute("key", correction.Key),
                new XAttribute("operation", correction.Operation), new XAttribute("target-id", correction.TargetId),
                new XAttribute("state", correction.State),
                correction.ReplacementId == null ? null : new XAttribute("replacement-id", correction.ReplacementId),
                correction.OriginalFingerprint == null ? null : new XAttribute("original-fingerprint", correction.OriginalFingerprint),
                correction.Group == null ? null : new XAttribute("group", correction.Group),
                correction.Reason == null ? null : new XElement(Ns + "reason", correction.Reason)));
        document.Root!.Add(section);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string ResolveSourcePath(string contentRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Correction source-path must be relative to the content root.");
        string root = Path.GetFullPath(contentRoot);
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (rel == ".." || rel.StartsWith("../", StringComparison.Ordinal) ||
            rel.StartsWith("user/", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(rel) ||
            !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Correction source must be an authoritative XML file inside its content root, outside user.");
        // Do not let a symlink/junction turn a relative origin into an external file.
        for (var current = new FileInfo(path) as FileSystemInfo; current != null && current.FullName != root;
             current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Correction origins cannot traverse symbolic links or junctions.");
        return path;
    }

    public static LocalCorrectionEvaluation Evaluate(string localXml, string upstreamXml)
    {
        var local = Parse(localXml);
        var sections = local.Root!.Elements().Where(e => e.Name.LocalName == "corrections").ToList();
        if (sections.Count != 1 || sections[0].Name != Ns + "corrections" || (string?)sections[0].Attribute("version") != "1")
            throw new InvalidDataException("Unknown or ambiguous correction metadata; preserve the file for review.");
        var section = sections[0];
        string Required(XElement e, string key) => (string?)e.Attribute(key) is { Length: > 0 } value ? value
            : throw new InvalidDataException($"Correction metadata is missing {key}.");
        string source = Required(section, "source-path");
        if (section.Elements().Any(e => e.Name != Ns + "baseline" && e.Name != Ns + "correction"))
            throw new InvalidDataException("Unknown correction section content.");
        var baselines = section.Elements(Ns + "baseline").ToList();
        if (baselines.Count != 1 || (string?)baselines[0].Attribute("encoding") != "escaped-xml" || baselines[0].HasElements)
            throw new InvalidDataException("Correction baseline must be one escaped-xml text payload.");
        string baselineXml = baselines[0].Value;
        var baseline = Parse(baselineXml);
        var upstream = Parse(upstreamXml);
        string? Url(XDocument d) => (string?)d.Root?.Element("info")?.Element("update")?.Element("file")?.Attribute("url");
        if (!string.Equals(Url(baseline), Url(upstream), StringComparison.Ordinal))
            throw new InvalidDataException("Authoritative update URL changed; review the correction's origin first.");
        string? Revision(XDocument d) => (string?)d.Root?.Element("info")?.Element("update")?.Attribute("version");
        if (Version.TryParse(Revision(baseline), out var originalVersion) &&
            Version.TryParse(Revision(upstream), out var incomingVersion) && incomingVersion < originalVersion)
            throw new InvalidDataException("Authoritative file version is older than the correction baseline; review the origin first.");
        var corrections = section.Elements(Ns + "correction").Select(e => new LocalCorrection(
            Required(e, "key"), Required(e, "operation"), Required(e, "target-id"),
            (string?)e.Attribute("replacement-id"), (string?)e.Attribute("original-fingerprint"),
            Required(e, "state"), (string?)e.Attribute("group"), (string?)e.Element(Ns + "reason"))).ToList();
        if (corrections.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != corrections.Count ||
            corrections.Any(c => c.Operation is not ("replace" or "rename" or "remove" or "add") ||
                                 c.State is not ("review-pending" or "accepted-upstream")))
            throw new InvalidDataException("Duplicate correction key or unsupported correction operation/state.");
        if (corrections.Where(c => c.Group != null).GroupBy(c => c.Group).Any(g => g.Select(c => c.State).Distinct().Count() != 1))
            throw new InvalidDataException("Related corrections must be reviewed together.");
        var effective = new XDocument(upstream);
        var reviews = new List<string>();
        if (bool.TryParse((string?)local.Root.Attribute("ignore"), out bool ignored) && ignored)
            return new(source, baselineXml, localXml, upstreamXml, upstreamXml, corrections,
                new[] { "Disabled local correction file" }, false, Array.Empty<string>());
        var coveredLocal = new HashSet<XElement>();
        var coveredBaseline = new HashSet<XElement>();
        static string Id(XElement e) => (string?)e.Attribute("id") ?? "";
        static List<XElement> Matches(XDocument d, string id) => d.Root!.Elements("element").Where(e => Id(e) == id).ToList();
        foreach (var correction in corrections)
        {
            string localId = correction.Operation == "rename"
                ? correction.ReplacementId ?? throw new InvalidDataException("Rename requires replacement-id.") : correction.TargetId;
            if (correction.Operation == "rename" && localId == correction.TargetId)
                throw new InvalidDataException("Rename must change the ID.");
            var previous = Matches(baseline, correction.TargetId);
            XElement? original = null;
            if (correction.Operation != "add")
            {
                var selected = previous.Where(e => correction.OriginalFingerprint == null || Fingerprint(e) == correction.OriginalFingerprint).ToList();
                if (selected.Count != 1) throw new InvalidDataException($"Ambiguous original declaration for {correction.Key}; supply its fingerprint.");
                original = selected[0];
                if (!coveredBaseline.Add(original)) throw new InvalidDataException("Multiple corrections target the same original declaration.");
            }
            XElement? replacement = null;
            if (correction.Operation != "remove")
            {
                var matches = Matches(local, localId);
                if (matches.Count != 1) throw new InvalidDataException($"Correction {correction.Key} needs exactly one local definition of {localId}.");
                replacement = matches[0];
                if (!coveredLocal.Add(replacement)) throw new InvalidDataException("Multiple corrections target the same local declaration.");
            }
            if (correction.State == "accepted-upstream") continue;
            var incoming = Matches(effective, correction.TargetId);
            var exact = original == null ? new List<XElement>() : incoming.Where(e => Fingerprint(e) == Fingerprint(original)).ToList();
            if (exact.Count == 1) exact[0].Remove();
            else if (correction.Operation != "add" && previous.Count == 1 && incoming.Count == 1)
                incoming[0].Remove();
            else if (incoming.Count > 0 && correction.Operation != "add" &&
                     incoming.Any(e => !previous.Where(p => p != original).Any(p => Fingerprint(p) == Fingerprint(e))))
                throw new InvalidDataException($"Upstream collision changed ambiguously for {correction.Key}.");
            if (replacement != null)
            {
                var existing = Matches(effective, localId);
                if (existing.Any(e => Fingerprint(e) != Fingerprint(replacement)))
                {
                    if (correction.Operation == "add") throw new InvalidDataException($"Upstream now claims local addition {localId}.");
                    if (localId != correction.TargetId) throw new InvalidDataException($"Rename destination {localId} is occupied.");
                }
                existing.ForEach(e => e.Remove());
                effective.Root!.Add(new XElement(replacement));
            }
            bool incorporated = correction.Operation == "remove"
                ? original != null && !Matches(upstream, correction.TargetId).Any(e => Fingerprint(e) == Fingerprint(original))
                : Matches(upstream, localId).Any(e => Fingerprint(e) == Fingerprint(replacement!)) &&
                  (correction.Operation != "rename" || !Matches(upstream, correction.TargetId).Any(e => Fingerprint(e) == Fingerprint(original!)));
            reviews.Add(correction.Key + (incorporated ? ": incorporated; review before clearing" : ": pinned"));
        }
        // Full local files also contain incidental copies. Follow upstream only when
        // the original baseline proves the local declaration was not edited.
        foreach (var element in local.Root.Elements("element").Where(e => !coveredLocal.Contains(e)))
        {
            if (baseline.Root!.Elements("element").Any(e => !coveredBaseline.Contains(e) && Fingerprint(e) == Fingerprint(element))) continue;
            if (Matches(upstream, Id(element)).Any(e => Fingerprint(e) == Fingerprint(element))) continue;
            Matches(effective, Id(element)).ForEach(e => e.Remove());
            effective.Root!.Add(new XElement(element));
            reviews.Add(Id(element) + ": unclassified local edit/addition");
        }
        foreach (var missing in baseline.Root!.Elements("element").Where(e => !coveredBaseline.Contains(e)))
        {
            if (local.Root.Elements("element").Any(e => Id(e) == Id(missing))) continue;
            if (!Matches(upstream, Id(missing)).Any()) continue;
            Matches(effective, Id(missing)).ForEach(e => e.Remove());
            reviews.Add(Id(missing) + ": unclassified local removal");
        }
        // Preserve local append/other operational changes as a whole; do not infer a merge.
        List<XElement> Extras(XDocument d) => d.Root!.Elements().Where(e => e.Name != "element" && e.Name != "info" && e.Name != Ns + "corrections").ToList();
        if (!Extras(local).Select(Fingerprint).SequenceEqual(Extras(baseline).Select(Fingerprint)))
        {
            Extras(effective).ForEach(e => e.Remove());
            effective.Root!.Add(Extras(local).Select(e => new XElement(e)));
            if (!Extras(local).Select(Fingerprint).SequenceEqual(Extras(upstream).Select(Fingerprint)))
                reviews.Add("Unclassified local append/operational edits");
        }
        foreach (var attribute in local.Root.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if ((string?)baseline.Root!.Attribute(attribute.Name) == attribute.Value) continue;
            effective.Root!.SetAttributeValue(attribute.Name, attribute.Value);
            if ((string?)upstream.Root!.Attribute(attribute.Name) != attribute.Value)
                reviews.Add("Unclassified local root attribute: " + attribute.Name);
        }
        foreach (var removed in baseline.Root!.Attributes().Where(a => !a.IsNamespaceDeclaration && local.Root.Attribute(a.Name) == null))
        {
            effective.Root!.Attribute(removed.Name)?.Remove();
            if (upstream.Root!.Attribute(removed.Name) != null) reviews.Add("Unclassified removed root attribute: " + removed.Name);
        }
        if (effective.Root!.Elements("element").GroupBy(Id).Any(g => g.Select(Fingerprint).Distinct().Count() > 1))
            throw new InvalidDataException("Effective corrected file still contains conflicting definitions.");
        string[] Content(XDocument d) => d.Root!.Elements().Where(e => e.Name != "info" && e.Name != Ns + "corrections")
            .Select(Fingerprint).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        bool canRetire = corrections.All(c => c.State == "accepted-upstream") &&
            reviews.Count == 0 && Content(effective).SequenceEqual(Content(upstream));
        var effectiveIds = effective.Root.Elements("element").Select(Id).ToHashSet(StringComparer.Ordinal);
        string[] suppressed = baseline.Root.Elements("element").Concat(upstream.Root!.Elements("element"))
            .Select(Id).Where(id => !effectiveIds.Contains(id)).Distinct().ToArray();
        return new(source, baselineXml, localXml, upstreamXml, effective.ToString(SaveOptions.DisableFormatting),
            corrections, reviews, canRetire, suppressed);
    }

    public static LocalCorrectionEvaluation? FromFile(string filePath, string contentRoot)
    {
        string xml = File.ReadAllText(filePath);
        if (!HasMetadata(xml)) return null;
        string relative = Path.GetRelativePath(contentRoot, filePath).Replace('\\', '/');
        if (!relative.StartsWith("user/local/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Correction metadata is only active inside user/local.");
        var section = Parse(xml).Root!.Elements().Single(e => e.Name.LocalName == "corrections");
        string origin = ResolveSourcePath(contentRoot, (string?)section.Attribute("source-path") ?? "");
        return Evaluate(xml, File.ReadAllText(origin));
    }

    public static string? FindContentRoot(string filePath)
    {
        var directory = new FileInfo(filePath).Directory;
        while (directory?.Parent?.Parent != null)
        {
            if (directory.Name.Equals("local", StringComparison.OrdinalIgnoreCase) && directory.Parent.Name.Equals("user", StringComparison.OrdinalIgnoreCase))
                return directory.Parent.Parent.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    public static LocalCorrectionEvaluation? ForRuntime(string filePath)
    {
        string? root = FindContentRoot(filePath);
        return root == null ? null : FromFile(filePath, root);
    }

    public static string FileFingerprint(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Managed corrections must never become a successful, partially corrected runtime load.</summary>
    public static void RethrowManagedRuntimeFailure(string filePath, Exception failure)
    {
        if (FindContentRoot(filePath) == null) return;
        string xml = File.ReadAllText(filePath);
        bool managed;
        try { managed = HasMetadata(xml); }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            // A broken legacy file remains skippable. Broken correction markup must
            // still fail closed even when it cannot be parsed to discover its section.
            managed = xml.Contains(Namespace, StringComparison.Ordinal) ||
                xml.Contains(":corrections", StringComparison.Ordinal) ||
                xml.Contains("<corrections", StringComparison.Ordinal);
        }
        if (managed)
            throw new InvalidDataException("Local correction could not be loaded completely; do not substitute uncorrected content.", failure);
    }

    public static bool IsSuppressedFromFile(string id, string? elementFilePath,
        string authoritativeFilePath, IReadOnlyList<string> suppressedIds)
    {
        // A repaired ID may legitimately survive in a different publication/file.
        // Unknown provenance is not permission to remove that other definition.
        if (string.IsNullOrWhiteSpace(elementFilePath) || !suppressedIds.Contains(id)) return false;
        return string.Equals(Path.GetFullPath(elementFilePath), Path.GetFullPath(authoritativeFilePath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>Explicit review action. Callers must supply hashes of the versions they reviewed.</summary>
    public static void AcceptUpstream(string filePath, string reviewedLocalHash, string reviewedUpstreamHash,
        IReadOnlyCollection<string> correctionKeys)
    {
        string root = FindContentRoot(filePath) ?? throw new InvalidDataException("Review requires a user/local correction file.");
        if (FileFingerprint(filePath) != reviewedLocalHash) throw new IOException("Local correction changed since review.");
        var evaluation = FromFile(filePath, root) ?? throw new InvalidDataException("No correction metadata found.");
        string source = ResolveSourcePath(root, evaluation.SourcePath);
        if (FileFingerprint(source) != reviewedUpstreamHash) throw new IOException("Upstream content changed since review.");
        if (correctionKeys.Count == 0 || correctionKeys.Any(key => !evaluation.Corrections.Any(c => c.Key == key)))
            throw new InvalidDataException("Select existing correction keys to review.");
        var document = Parse(evaluation.LocalXml);
        foreach (var node in document.Root!.Element(Ns + "corrections")!.Elements(Ns + "correction"))
            if (correctionKeys.Contains((string)node.Attribute("key")!)) node.SetAttributeValue("state", "accepted-upstream");
        string updated = document.ToString(SaveOptions.DisableFormatting);
        Evaluate(updated, evaluation.UpstreamXml); // Includes linked-group and effective-content validation.
        string temporary = filePath + ".review-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, updated);
            if (FileFingerprint(filePath) != reviewedLocalHash || FileFingerprint(source) != reviewedUpstreamHash)
                throw new IOException("Content changed while accepting the review.");
            File.Move(temporary, filePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
