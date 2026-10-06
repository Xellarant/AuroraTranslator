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

namespace Aurora.Content.Contracts;

public sealed record LocalCorrection(string Key, string Operation, string TargetId,
    string? ReplacementId, string? OriginalFingerprint, string State = "review-pending",
    string? Group = null, string? Reason = null)
{
    public string? ApprovalFingerprint { get; init; }
    public bool IsActive => State is "review-pending" or "approved-local";
}

public sealed record LocalCorrectionEvaluation(string SourcePath, string BaselineXml,
    string LocalXml, string UpstreamXml, string EffectiveXml,
    IReadOnlyList<LocalCorrection> Corrections, IReadOnlyList<string> ReviewReasons,
    bool CanRetire, IReadOnlyList<string> SuppressedIds)
{
    /// <summary>Corrections incorporated in the current upstream XML, independently of their review state.</summary>
    public IReadOnlyList<string> IncorporatedKeys { get; init; } = Array.Empty<string>();
}

/// <summary>File-level correction intent. XML is durable; database rows are a mirror.</summary>
public static class LocalCorrectionDocument
{
    public const string Namespace = "urn:aurora-lights:corrections:1";
    private static readonly XNamespace Ns = Namespace;
    private const string ApprovalAlgorithm = "aurora-local-approval-v1";

    /// <param name="origin">The file the content came from, named in errors so a bad file can be found.</param>
    public static XDocument Parse(string xml, string? origin = null)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "elements")
        {
            // A namespace on the root is the usual cause: <elements xmlns="..."> is a different
            // element name to XML, so nothing inside it would be recognised as Aurora content.
            string where = string.IsNullOrWhiteSpace(origin) ? string.Empty : $" in {origin}";
            string found = document.Root is null ? "no root element" : $"found '{document.Root.Name}'";
            throw new InvalidDataException(
                $"Content must have an unnamespaced elements root{where} ({found}).");
        }
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
                correction.ApprovalFingerprint == null ? null : new XAttribute("approval-fingerprint", correction.ApprovalFingerprint),
                correction.Group == null ? null : new XAttribute("group", correction.Group),
                correction.Reason == null ? null : new XElement(Ns + "reason", correction.Reason)));
        document.Root!.Add(section);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Replaces definitions owned by existing corrections without changing their origins or identities.
    /// Selected corrections and their linked peers in this document return to review-pending.
    /// This pure operation does not inspect group members in other files or write/import content.
    /// </summary>
    /// <param name="replacementsByCorrectionKey">Existing, case-sensitive correction keys mapped to a single element XML document.</param>
    public static LocalCorrectionEvaluation ReplaceDefinitions(string localXml, string upstreamXml,
        IReadOnlyDictionary<string, string> replacementsByCorrectionKey)
    {
        ArgumentNullException.ThrowIfNull(replacementsByCorrectionKey);
        if (replacementsByCorrectionKey.Count == 0)
            throw new InvalidDataException("Select at least one existing correction to amend.");
        var local = Parse(localXml);
        var (section, source, baselineXml) = ReadMetadata(local);
        // Approval can become stale through a manual edit or a newly added group member.
        // Normalize that review state without requiring the old effective content to compose.
        var corrections = ValidateApprovals(local, Parse(baselineXml), Parse(upstreamXml), section, source,
            ReadCorrections(section, validateGroups: false), new List<string>());
        ValidateGroupStates(corrections);
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var replacement in replacementsByCorrectionKey)
        {
            var correction = corrections.SingleOrDefault(c => string.Equals(c.Key, replacement.Key, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Unknown correction key '{replacement.Key}'. Select an existing correction to amend.");
            if (!selectedKeys.Add(correction.Key))
                throw new InvalidDataException($"Correction '{correction.Key}' was selected more than once.");
            if (correction.Operation == "remove")
                throw new InvalidDataException($"Correction '{correction.Key}' removes a definition and has no local definition to replace.");
            string localId = correction.Operation == "rename"
                ? correction.ReplacementId ?? throw new InvalidDataException("Rename requires replacement-id.")
                : correction.TargetId;
            var matches = local.Root!.Elements("element").Where(e => (string?)e.Attribute("id") == localId).ToList();
            if (matches.Count != 1)
                throw new InvalidDataException($"Correction {correction.Key} needs exactly one local definition of {localId}.");
            using var reader = XmlReader.Create(new StringReader(replacement.Value), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            var element = document.Root;
            if (element == null || element.Name != "element" || new[] { "id", "name", "type" }
                .Any(attribute => string.IsNullOrWhiteSpace((string?)element.Attribute(attribute))))
                throw new InvalidDataException($"Replacement for '{correction.Key}' must be one unnamespaced element with a nonempty id, name, and type.");
            if (element.DescendantsAndSelf().Any(e => e.Name.LocalName == "corrections" ||
                e.Name.NamespaceName.StartsWith("urn:aurora-lights:corrections:", StringComparison.Ordinal) ||
                e.Attributes().Any(a => a.Name.NamespaceName.StartsWith("urn:aurora-lights:corrections:", StringComparison.Ordinal))))
                throw new InvalidDataException($"Replacement for '{correction.Key}' cannot contain correction metadata.");
            if ((string?)element.Attribute("id") != localId)
                throw new InvalidDataException($"Replacement for '{correction.Key}' must keep its existing local ID '{localId}'.");
            matches[0].ReplaceWith(new XElement(element));
            if (correction.Group != null) groups.Add(correction.Group);
        }
        foreach (var correction in section.Elements(Ns + "correction"))
            if (selectedKeys.Contains((string)correction.Attribute("key")!) ||
                ((string?)correction.Attribute("group") is { } group && groups.Contains(group)))
            {
                correction.SetAttributeValue("state", "review-pending");
                correction.Attribute("approval-fingerprint")?.Remove();
            }
        // Validate the amended state, so an upstream collision can be repaired by this operation.
        return Evaluate(local.ToString(SaveOptions.DisableFormatting), upstreamXml);
    }

    /// <summary>
    /// Approves the selected local corrections while keeping them active and protected. Every member
    /// of a selected group in this document must be selected. Hosts must coordinate other files.
    /// Approval is bound to the relevant declarations and intent, not unrelated edits in their files.
    /// </summary>
    public static LocalCorrectionEvaluation ApproveLocal(string localXml, string upstreamXml,
        IReadOnlyCollection<string> correctionKeys)
    {
        var evaluation = Evaluate(localXml, upstreamXml);
        var selected = SelectCompleteGroups(evaluation.Corrections, correctionKeys);
        var local = Parse(localXml);
        var (section, source, baselineXml) = ReadMetadata(local);
        foreach (var node in section.Elements(Ns + "correction").Where(e => selected.Contains(Required(e, "key"))))
        {
            node.SetAttributeValue("state", "approved-local");
            node.Attribute("approval-fingerprint")?.Remove();
        }
        var baseline = Parse(baselineXml);
        var upstream = Parse(upstreamXml);
        foreach (var unit in ApprovalUnits(ReadCorrections(section, validateGroups: false)).Where(g => selected.Contains(g[0].Key)))
        {
            string fingerprint = ApprovalFingerprint(local, baseline, upstream, section, source, unit);
            foreach (var node in section.Elements(Ns + "correction").Where(e => unit.Any(c => c.Key == Required(e, "key"))))
                node.SetAttributeValue("approval-fingerprint", fingerprint);
        }
        return Evaluate(local.ToString(SaveOptions.DisableFormatting), upstreamXml);
    }

    /// <summary>
    /// Records explicit acceptance of upstream for complete same-document groups. This does not
    /// authorize automatic acceptance; hosts decide eligibility and coordinate any other files.
    /// </summary>
    public static LocalCorrectionEvaluation AcceptUpstream(string localXml, string upstreamXml,
        IReadOnlyCollection<string> correctionKeys)
    {
        var evaluation = Evaluate(localXml, upstreamXml);
        var selected = SelectCompleteGroups(evaluation.Corrections, correctionKeys);
        var local = Parse(localXml);
        var (section, _, _) = ReadMetadata(local);
        foreach (var node in section.Elements(Ns + "correction").Where(e => selected.Contains(Required(e, "key"))))
        {
            node.SetAttributeValue("state", "accepted-upstream");
            node.Attribute("approval-fingerprint")?.Remove();
        }
        return Evaluate(local.ToString(SaveOptions.DisableFormatting), upstreamXml);
    }

    private static HashSet<string> SelectCompleteGroups(IReadOnlyList<LocalCorrection> corrections,
        IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0 || selected.Count != keys.Count ||
            selected.Any(key => !corrections.Any(c => c.Key == key)))
            throw new InvalidDataException("Select unique existing correction keys to review.");
        foreach (var group in corrections.Where(c => c.Group != null).GroupBy(c => c.Group, StringComparer.Ordinal))
            if (group.Any(c => selected.Contains(c.Key)) && group.Any(c => !selected.Contains(c.Key)))
                throw new InvalidDataException($"Related corrections in group '{group.Key}' must be reviewed together. Select every member in this file.");
        return selected;
    }

    private static string Required(XElement element, string key) => (string?)element.Attribute(key) is { Length: > 0 } value
        ? value : throw new InvalidDataException($"Correction metadata is missing {key}.");

    private static (XElement Section, string SourcePath, string BaselineXml) ReadMetadata(XDocument local)
    {
        var sections = local.Root!.Elements().Where(e => e.Name.LocalName == "corrections").ToList();
        if (sections.Count != 1 || sections[0].Name != Ns + "corrections" || (string?)sections[0].Attribute("version") != "1")
            throw new InvalidDataException("Unknown or ambiguous correction metadata; preserve the file for review.");
        var section = sections[0];
        string source = Required(section, "source-path");
        if (section.Elements().Any(e => e.Name != Ns + "baseline" && e.Name != Ns + "correction"))
            throw new InvalidDataException("Unknown correction section content.");
        var baselines = section.Elements(Ns + "baseline").ToList();
        if (baselines.Count != 1 || (string?)baselines[0].Attribute("encoding") != "escaped-xml" || baselines[0].HasElements)
            throw new InvalidDataException("Correction baseline must be one escaped-xml text payload.");
        return (section, source, baselines[0].Value);
    }

    private static List<LocalCorrection> ReadCorrections(XElement section, bool validateGroups = true)
    {
        var corrections = section.Elements(Ns + "correction").Select(e => new LocalCorrection(
            Required(e, "key"), Required(e, "operation"), Required(e, "target-id"),
            (string?)e.Attribute("replacement-id"), (string?)e.Attribute("original-fingerprint"),
            Required(e, "state"), (string?)e.Attribute("group"), (string?)e.Element(Ns + "reason"))
            { ApprovalFingerprint = (string?)e.Attribute("approval-fingerprint") }).ToList();
        if (corrections.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != corrections.Count ||
            corrections.Any(c => c.Operation is not ("replace" or "rename" or "remove" or "add") ||
                                 c.State is not ("review-pending" or "approved-local" or "accepted-upstream")))
            throw new InvalidDataException("Duplicate correction key or unsupported correction operation/state.");
        if (validateGroups) ValidateGroupStates(corrections);
        return corrections;
    }

    private static void ValidateGroupStates(IReadOnlyList<LocalCorrection> corrections)
    {
        if (corrections.Where(c => c.Group != null).GroupBy(c => c.Group).Any(g => g.Select(c => c.State).Distinct().Count() != 1))
            throw new InvalidDataException("Related corrections must be reviewed together.");
    }

    private static IEnumerable<LocalCorrection[]> ApprovalUnits(IEnumerable<LocalCorrection> corrections) =>
        corrections.GroupBy(c => (IsGroup: c.Group != null, Key: c.Group ?? c.Key)).Select(g => g.ToArray());

    private static string Id(XElement element) => (string?)element.Attribute("id") ?? "";
    private static List<XElement> Matches(XDocument document, string id) =>
        document.Root!.Elements("element").Where(e => Id(e) == id).ToList();
    private static string? UpdateUrl(XDocument document) =>
        (string?)document.Root?.Element("info")?.Element("update")?.Element("file")?.Attribute("url");

    private static string ApprovalFingerprint(XDocument local, XDocument baseline, XDocument upstream,
        XElement section, string source, IReadOnlyList<LocalCorrection> unit)
    {
        object?[] RootContext(XDocument document) => new object?[]
        {
            Fingerprint(new XElement("root", document.Root!.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .Select(a => new XAttribute(a)))), UpdateUrl(document)
        };
        string[] Definitions(XDocument document, string id) => Matches(document, id).Select(Fingerprint).ToArray();
        object[] Member(LocalCorrection correction)
        {
            var intent = new XElement(section.Elements(Ns + "correction").Single(e => Required(e, "key") == correction.Key));
            intent.Attribute("state")?.Remove();
            intent.Attribute("approval-fingerprint")?.Remove();
            string localId = correction.Operation == "rename" ? correction.ReplacementId ?? "" : correction.TargetId;
            return new object[]
            {
                Fingerprint(intent),
                correction.Operation == "add" ? Array.Empty<string>() : Matches(baseline, correction.TargetId)
                    .Where(e => correction.OriginalFingerprint == null || Fingerprint(e) == correction.OriginalFingerprint)
                    .Select(Fingerprint).ToArray(),
                Definitions(local, localId), Definitions(upstream, correction.TargetId),
                correction.Operation == "rename" ? Definitions(upstream, localId) : Array.Empty<string>()
            };
        }
        // Group membership and member order are part of the approved intent. Empty arrays bind
        // absence explicitly, including remove operations and a previously unclaimed add/rename ID.
        object?[] payload = { ApprovalAlgorithm, source, RootContext(baseline), RootContext(local), RootContext(upstream),
            unit.Select(Member).ToArray() };
        return ApprovalAlgorithm + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
    }

    private static List<LocalCorrection> ValidateApprovals(XDocument local, XDocument baseline, XDocument upstream,
        XElement section, string source, List<LocalCorrection> corrections, List<string> reviews)
    {
        var reopened = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in ApprovalUnits(corrections).Where(g => g.Any(c => c.State == "approved-local")))
        {
            string expected = ApprovalFingerprint(local, baseline, upstream, section, source, unit);
            if (unit.All(c => c.State == "approved-local" && c.ApprovalFingerprint == expected)) continue;
            foreach (var correction in unit) reopened.Add(correction.Key);
            string subject = unit[0].Group == null ? $"Correction '{unit[0].Key}'" : $"Correction group '{unit[0].Group}'";
            reviews.Add(subject + ": local approval is missing or its relevant content, intent, or group membership changed. Review and approve the local correction again; protection remains active.");
        }
        return corrections.Select(c => reopened.Contains(c.Key) ? c with { State = "review-pending" } : c).ToList();
    }

    private static bool IsIncorporated(LocalCorrection correction, XDocument local, XDocument baseline, XDocument upstream)
    {
        var previous = Matches(baseline, correction.TargetId);
        var originals = previous
            .Where(e => correction.OriginalFingerprint == null || Fingerprint(e) == correction.OriginalFingerprint).ToList();
        XElement? original = originals.Count == 1 ? originals[0] : null;
        if (correction.Operation != "add" && original == null) return false;
        bool OriginalWasRemoved()
        {
            // A rename/removal may deliberately select one declaration from a same-ID collision.
            // Unchanged baseline peers may survive, but a newly changed declaration is not proof
            // that upstream incorporated removal of this identity's selected definition.
            var remaining = previous.Where(e => !ReferenceEquals(e, original)).Select(Fingerprint).ToList();
            foreach (var incoming in Matches(upstream, correction.TargetId))
            {
                int match = remaining.FindIndex(fingerprint => fingerprint == Fingerprint(incoming));
                if (match < 0) return false;
                remaining.RemoveAt(match);
            }
            return true;
        }
        if (correction.Operation == "remove")
            return OriginalWasRemoved();
        string localId = correction.Operation == "rename" ? correction.ReplacementId ?? "" : correction.TargetId;
        var replacements = Matches(local, localId);
        var incomingReplacements = Matches(upstream, localId);
        return replacements.Count == 1 && incomingReplacements.Count > 0
            && incomingReplacements.All(e => Fingerprint(e) == Fingerprint(replacements[0]))
            && (correction.Operation != "rename" || OriginalWasRemoved());
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
        var (section, source, baselineXml) = ReadMetadata(local);
        var baseline = Parse(baselineXml);
        var upstream = Parse(upstreamXml);
        if (!string.Equals(UpdateUrl(baseline), UpdateUrl(upstream), StringComparison.Ordinal))
            throw new InvalidDataException("Authoritative update URL changed; review the correction's origin first.");
        string? Revision(XDocument d) => (string?)d.Root?.Element("info")?.Element("update")?.Attribute("version");
        if (Version.TryParse(Revision(baseline), out var originalVersion) &&
            Version.TryParse(Revision(upstream), out var incomingVersion) && incomingVersion < originalVersion)
            throw new InvalidDataException("Authoritative file version is older than the correction baseline; review the origin first.");
        var effective = new XDocument(upstream);
        var reviews = new List<string>();
        var corrections = ValidateApprovals(local, baseline, upstream, section, source,
            ReadCorrections(section, validateGroups: false), reviews);
        ValidateGroupStates(corrections);
        var incorporated = corrections.Where(c => IsIncorporated(c, local, baseline, upstream))
            .Select(c => c.Key).ToList().AsReadOnly();
        if (bool.TryParse((string?)local.Root!.Attribute("ignore"), out bool ignored) && ignored)
        {
            reviews.Add("Disabled local correction file");
            return new(source, baselineXml, localXml, upstreamXml, upstreamXml, corrections,
                reviews, false, Array.Empty<string>()) { IncorporatedKeys = incorporated };
        }
        var coveredLocal = new HashSet<XElement>();
        var coveredBaseline = new HashSet<XElement>();
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
            if (correction.State == "review-pending")
                reviews.Add(correction.Key + (incorporated.Contains(correction.Key) ? ": incorporated; review before clearing" : ": pinned"));
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
            corrections, reviews, canRetire, suppressed) { IncorporatedKeys = incorporated };
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
        string updated = AcceptUpstream(evaluation.LocalXml, evaluation.UpstreamXml, correctionKeys).LocalXml;
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
