#nullable enable
using Builder.Data.Files;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AuroraTranslator.Content;

public sealed record LocalAliasPlan(string ProtectedXml, IReadOnlyDictionary<string, string> Aliases);

/// <summary>Explicit authoring operation for retaining a local variant beside its source definition.</summary>
public static class ProtectedLocalAliases
{
    public static LocalAliasPlan Create(string upstreamXml, string localXml, string sourcePath, string sourceLabel)
    {
        if (!Regex.IsMatch(sourceLabel, "^[A-Z0-9_]+$")) throw new ArgumentException("Use an explicit uppercase source label.");
        if (LocalCorrectionDocument.HasMetadata(localXml)) throw new InvalidDataException("Review existing correction metadata before creating local aliases.");
        var baseline = LocalCorrectionDocument.Parse(upstreamXml);
        var local = LocalCorrectionDocument.Parse(localXml);
        if (local.Root!.HasAttributes || local.Root.Elements().Any(e => e.Name != "info" && e.Name != "element"))
            throw new InvalidDataException("Local alias authoring requires declaration-only XML. Review root flags and append/other operations separately so none are discarded.");
        var originals = baseline.Root!.Elements("element").GroupBy(Id).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var supplied = local.Root!.Elements("element").ToArray();
        if (supplied.GroupBy(Id).Any(g => g.Count() > 1)) throw new InvalidDataException("Consolidate local within-file duplicates before creating variants.");
        var aliases = supplied.Where(e => originals.TryGetValue(Id(e), out var candidates) &&
                !candidates.Any(c => LocalCorrectionDocument.Fingerprint(c) == LocalCorrectionDocument.Fingerprint(e)))
            .ToDictionary(Id, e => Id(e) + "_LOCAL_" + sourceLabel, StringComparer.Ordinal);
        // An otherwise unchanged local parent that refers to a local variant needs
        // its own identity too. Never rewrite the authoritative parent's references.
        while (true)
        {
            var additional = supplied.Where(e => !aliases.ContainsKey(Id(e)) && originals.ContainsKey(Id(e)) &&
                !XNode.DeepEquals(e, Rewrite(e, aliases))).Select(Id).ToArray();
            if (additional.Length == 0) break;
            foreach (string id in additional) aliases.Add(id, id + "_LOCAL_" + sourceLabel);
        }
        var allIds = originals.Keys.Concat(supplied.Select(Id)).ToHashSet(StringComparer.Ordinal);
        if (aliases.Values.Any(allIds.Contains)) throw new InvalidDataException("A source-qualified alias is already occupied; review its existing definition.");
        var result = new XDocument(baseline);
        var corrections = new List<LocalCorrection>();
        foreach (var element in supplied)
        {
            string oldId = Id(element);
            if (!aliases.ContainsKey(oldId) && originals.ContainsKey(oldId)) continue;
            var variant = Rewrite(element, aliases);
            string newId = Id(variant);
            result.Root!.Add(variant);
            corrections.Add(new(newId, "add", newId, null, LocalCorrectionDocument.Fingerprint(element),
                Group: "local-variants-" + sourceLabel,
                Reason: $"User-authorized distinct local variant from {sourceLabel}: {oldId} -> {newId}. Original publication: {sourcePath}. Preserve the authoritative ID and local gameplay independently; exact references within this local document follow its variants. Review remains pending; this does not accept an upstream change."));
        }
        if (corrections.Count == 0) throw new InvalidDataException("No distinct local variants require protection.");
        string xml = LocalCorrectionDocument.Create(result.ToString(SaveOptions.DisableFormatting), upstreamXml, sourcePath, corrections);
        LocalCorrectionDocument.Evaluate(xml, upstreamXml);
        return new(xml, aliases);
    }

    private static string Id(XElement e) => (string?)e.Attribute("id") ?? throw new InvalidDataException("Missing declaration ID.");
    private static XElement Rewrite(XElement original, IReadOnlyDictionary<string, string> aliases)
    {
        var copy = new XElement(original);
        if (aliases.Count == 0) return copy;
        var expression = new Regex(@"(?<![A-Za-z0-9_])(?:" + string.Join("|", aliases.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?![A-Za-z0-9_])");
        string Replace(string text) => expression.Replace(text, m => aliases[m.Value]);
        foreach (var a in copy.DescendantsAndSelf().Attributes().Where(a => !a.IsNamespaceDeclaration)) a.Value = Replace(a.Value);
        foreach (var text in copy.DescendantNodes().OfType<XText>()) text.Value = Replace(text.Value);
        return copy;
    }
}
