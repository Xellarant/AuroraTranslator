using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace _5eApiTranslator.Models
{
    public class AuroraTextCollection : IEnumerable<string>
    {
        public string raw { get; set; }
        public List<string> values { get; set; } = new();

        public int Count => values?.Count ?? 0;

        public string this[int index] => values[index];

        public void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        public void AddRange(IEnumerable<string> newValues)
        {
            if (newValues == null)
                return;

            foreach (var value in newValues.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                values.Add(value.Trim());
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class AuroraSetterEntry
    {
        public string name { get; set; }
        public string value { get; set; }
        public Dictionary<string, string> attributes { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public string GetAttribute(string attributeName)
        {
            return attributes.TryGetValue(attributeName, out var value)
                ? value
                : null;
        }
    }
}
