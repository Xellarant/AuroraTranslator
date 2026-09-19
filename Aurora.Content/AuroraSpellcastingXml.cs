using AuroraTranslator.Models;
using System.Linq;
using System.Xml.Linq;

namespace AuroraTranslator;

internal static class AuroraSpellcastingXml
{
    internal static Spellcasting Parse(XElement element)
    {
        var entries = element.Elements()
            .Where(child => child.Name == "list" || child.Name == "extend")
            .Select((child, index) => new SpellcastingEntry
            {
                kind = child.Name.LocalName,
                ordinal = index + 1,
                text = child.Value,
                known = Boolean(child, "known") ?? false,
                rawXml = child.ToString(SaveOptions.DisableFormatting)
            }).ToList();

        return new Spellcasting
        {
            name = (string)element.Attribute("name"),
            ability = (string)element.Attribute("ability"),
            prepare = Boolean(element, "prepare"),
            allowReplace = Boolean(element, "allowReplace"),
            extend = Boolean(element, "extend") ?? false,
            all = Boolean(element, "all") ?? false,
            rawXml = element.ToString(SaveOptions.DisableFormatting),
            entries = entries,
            // WPF assigns the last <list> as the initial expression. Keep all children above.
            list = Text(entries.LastOrDefault(entry => entry.kind == "list")?.text),
            extendList = Text(string.Join(",", entries.Where(entry => entry.kind == "extend").Select(entry => entry.text)))
        };
    }

    private static bool? Boolean(XElement element, string name)
        => bool.TryParse((string)element.Attribute(name), out bool value) ? value : null;

    private static AuroraTextCollection Text(string value)
        => value == null ? null : new AuroraTextCollection { raw = value };
}
