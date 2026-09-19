#nullable enable
using System;
using System.Collections.Generic;

namespace AuroraTranslator.Content;

// Extracted unchanged from the Translator catalog reader for composition consumers.
public static class ContentText
{
    public static List<string> SplitTopLevel(string input, char separator)
    {
        var values = new List<string>();

        if (string.IsNullOrWhiteSpace(input))
            return values;

        int parenthesesDepth = 0;
        int bracketsDepth = 0;
        int bracesDepth = 0;
        var current = new System.Text.StringBuilder();

        foreach (char ch in input)
        {
            switch (ch)
            {
                case '(':
                    parenthesesDepth++;
                    break;
                case ')':
                    parenthesesDepth = Math.Max(0, parenthesesDepth - 1);
                    break;
                case '[':
                    bracketsDepth++;
                    break;
                case ']':
                    bracketsDepth = Math.Max(0, bracketsDepth - 1);
                    break;
                case '{':
                    bracesDepth++;
                    break;
                case '}':
                    bracesDepth = Math.Max(0, bracesDepth - 1);
                    break;
            }

            if (ch == separator
                && parenthesesDepth == 0
                && bracketsDepth == 0
                && bracesDepth == 0)
            {
                string candidate = current.ToString().Trim();

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    values.Add(candidate);
                }

                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        string finalCandidate = current.ToString().Trim();

        if (!string.IsNullOrWhiteSpace(finalCandidate))
        {
            values.Add(finalCandidate);
        }

        return values;
    }
}
