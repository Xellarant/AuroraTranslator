#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AuroraTranslator.Content;

/// <summary>Pure, ID-directed extension of a prepared definition. Inputs remain unchanged.</summary>
public static class ContentAppendComposer
{
    public static XElement Apply(XElement definition, XElement append)
    {
        if (append.Name != "append" || (string?)append.Attribute("id") != (string?)definition.Attribute("id"))
            throw new InvalidDataException("Append requires an exact target Aurora ID.");
        if (append.Attributes().Any(a => a.Name != "id" && a.Name != "type") ||
            append.Elements().Any(e => e.Name != "supports" && e.Name != "rules" && e.Name != "description" && e.Name != "setters" && e.Name != "spellcasting"))
            throw new InvalidDataException("Unsupported append attribute/container; preserve and review this operation.");
        if (append.Attribute("type") is { Value.Length: > 0 } type &&
            !string.Equals(type.Value, (string?)definition.Attribute("type"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Append type does not match its target definition.");
        var result = new XElement(definition);
        foreach (var child in append.Elements())
        {
            if ((child.Name == "supports" && (child.HasAttributes || child.HasElements)) ||
                (child.Name == "rules" && (child.HasAttributes || child.Elements().Any(e => e.Name != "grant" && e.Name != "select" && e.Name != "stat"))) ||
                (child.Name == "setters" && (child.HasAttributes || child.Elements().Any(e => e.Name != "set" || string.IsNullOrWhiteSpace((string?)e.Attribute("name"))))))
                throw new InvalidDataException($"Unsupported append {child.Name} structure; record an explicit supported correction.");
            var container = result.Element(child.Name);
            if (child.Name == "supports")
            {
                // Same token union as Legacy's AppendElements. Use Translator's
                // balanced delimiter reader so nested expressions stay intact.
                var tokens = ContentText.SplitTopLevel(container?.Value ?? "", ',')
                    .Concat(ContentText.SplitTopLevel(child.Value, ',')).Distinct(StringComparer.Ordinal);
                if (container == null) result.Add(new XElement("supports", string.Join(", ", tokens)));
                else container.Value = string.Join(", ", tokens);
            }
            else if (child.Name == "spellcasting")
            {
                if (container != null)
                    throw new InvalidDataException("Append cannot replace existing spellcasting; record an explicit correction.");
                result.Add(new XElement(child));
            }
            else if (container == null) result.Add(new XElement(child));
            else if (child.Name == "setters")
            {
                foreach (var setter in child.Elements())
                {
                    var existing = container.Elements().Where(e => (string?)e.Attribute("name") == (string?)setter.Attribute("name")).ToArray();
                    if (existing.Any(e => !XNode.DeepEquals(e, setter)))
                        throw new InvalidDataException($"Append setter '{(string?)setter.Attribute("name")}' conflicts with an existing value.");
                    if (existing.Length == 0) container.Add(new XElement(setter));
                }
            }
            else
            {
                if (child.Attributes().Any(a => (string?)container.Attribute(a.Name) != a.Value))
                    throw new InvalidDataException($"Append {child.Name} attributes conflict with the target container.");
                // Rules retain authored multiplicity and order. Description fragments
                // are combined in one container rather than hidden behind a second one.
                container.Add(child.Nodes().Select(Clone));
            }
        }
        return result;
    }

    private static XNode Clone(XNode node) => node switch
    {
        XElement e => new XElement(e), XCData c => new XCData(c.Value), XText t => new XText(t.Value),
        XComment c => new XComment(c.Value), _ => throw new InvalidDataException("Unsupported append XML node.")
    };
}
