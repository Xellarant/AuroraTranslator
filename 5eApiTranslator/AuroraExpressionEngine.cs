using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AuroraTranslator
{
    internal sealed class AuroraExpressionParseResult
    {
        public string Status { get; init; }
        public string ErrorText { get; init; }
        public AuroraExpressionNode RootNode { get; init; }
    }

    internal sealed class AuroraExpressionNode
    {
        public string Kind { get; init; }
        public string ValueType { get; init; }
        public string ValueText { get; init; }
        public List<AuroraExpressionNode> Children { get; } = new();
    }

    internal sealed class AuroraExpressionEvaluationContext
    {
        public HashSet<string> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, decimal> NumericValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ScalarValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> MacroValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static AuroraExpressionEvaluationContext Empty { get; } = new();

        public static AuroraExpressionEvaluationContext Load(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                return new AuroraExpressionEvaluationContext();

            string json = File.ReadAllText(jsonPath);
            var document = JsonSerializer.Deserialize<ExpressionContextDocument>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ExpressionContextDocument();

            var context = new AuroraExpressionEvaluationContext();

            foreach (var token in document.Tokens ?? Enumerable.Empty<string>())
            {
                context.AddToken(token);
            }

            foreach (var pair in document.NumericValues ?? new Dictionary<string, decimal>())
            {
                context.AddNumericValue(pair.Key, pair.Value);
            }

            foreach (var pair in document.ScalarValues ?? new Dictionary<string, string>())
            {
                context.AddScalarValue(pair.Key, pair.Value);
            }

            foreach (var pair in document.MacroValues ?? new Dictionary<string, List<string>>())
            {
                context.AddMacroValues(pair.Key, pair.Value);
            }

            return context;
        }

        public void AddToken(string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
                Tokens.Add(token.Trim());
        }

        public void AddNumericValue(string key, decimal value)
        {
            if (!string.IsNullOrWhiteSpace(key))
                NumericValues[key.Trim()] = value;
        }

        public void AddScalarValue(string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(key) && value != null)
                ScalarValues[key.Trim()] = value.Trim();
        }

        public void AddMacroValues(string macroName, IEnumerable<string> values)
        {
            if (string.IsNullOrWhiteSpace(macroName))
                return;

            if (!MacroValues.TryGetValue(macroName.Trim(), out var tokens))
            {
                tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                MacroValues[macroName.Trim()] = tokens;
            }

            foreach (var value in values ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                    tokens.Add(value.Trim());
            }
        }

        public bool MatchesToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            return Tokens.Contains(token.Trim());
        }

        public bool MatchesMacro(string macroText)
        {
            if (string.IsNullOrWhiteSpace(macroText))
                return false;

            if (MacroValues.TryGetValue(macroText.Trim(), out var macroTokens))
                return macroTokens.Any(Tokens.Contains);

            return Tokens.Contains(macroText.Trim());
        }

        public bool EvaluateBracket(string bracketText)
        {
            if (string.IsNullOrWhiteSpace(bracketText))
                return false;

            string text = bracketText.Trim();
            if (!text.StartsWith("[", StringComparison.Ordinal) || !text.EndsWith("]", StringComparison.Ordinal))
                return MatchesToken(text);

            string inner = text[1..^1].Trim();
            if (string.IsNullOrWhiteSpace(inner))
                return false;

            string[] parts = inner.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                return MatchesToken(inner) || MatchesToken(text);

            string key = string.Join(":", parts.Take(parts.Length - 1));
            string expected = parts[^1];

            if (decimal.TryParse(expected, out decimal threshold))
            {
                if (NumericValues.TryGetValue(key, out decimal actual))
                    return actual >= threshold;

                if (ScalarValues.TryGetValue(key, out string scalarValue)
                    && decimal.TryParse(scalarValue, out actual))
                {
                    return actual >= threshold;
                }

                return MatchesToken(inner) || MatchesToken(text);
            }

            if (ScalarValues.TryGetValue(key, out string actualValue))
                return string.Equals(actualValue, expected, StringComparison.OrdinalIgnoreCase);

            return MatchesToken(inner)
                || MatchesToken($"{key}:{expected}")
                || MatchesToken(text);
        }

        private sealed class ExpressionContextDocument
        {
            public List<string> Tokens { get; set; }
            public Dictionary<string, decimal> NumericValues { get; set; }
            public Dictionary<string, string> ScalarValues { get; set; }
            public Dictionary<string, List<string>> MacroValues { get; set; }
        }
    }

    internal static class AuroraExpressionEngine
    {
        public static AuroraExpressionParseResult Parse(string rawText)
        {
            string expressionText = rawText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expressionText))
            {
                return new AuroraExpressionParseResult
                {
                    Status = "failed",
                    ErrorText = "Expression text was empty.",
                    RootNode = CreateFallbackValueNode(rawText)
                };
            }

            try
            {
                List<ExpressionToken> tokens = Tokenize(expressionText);
                if (tokens.Count == 0)
                {
                    return new AuroraExpressionParseResult
                    {
                        Status = "failed",
                        ErrorText = "Expression text produced no tokens.",
                        RootNode = CreateFallbackValueNode(rawText)
                    };
                }

                int index = 0;
                AuroraExpressionNode rootNode = ParseOr(tokens, ref index);
                if (index != tokens.Count)
                    throw new InvalidOperationException($"Unexpected token '{tokens[index].Text}'.");

                return new AuroraExpressionParseResult
                {
                    Status = "parsed",
                    RootNode = rootNode
                };
            }
            catch (Exception ex)
            {
                return new AuroraExpressionParseResult
                {
                    Status = "failed",
                    ErrorText = ex.Message,
                    RootNode = CreateFallbackValueNode(rawText)
                };
            }
        }

        public static bool Evaluate(string rawText, AuroraExpressionEvaluationContext context)
        {
            AuroraExpressionParseResult parseResult = Parse(rawText);
            return Evaluate(parseResult.RootNode, context);
        }

        public static bool Evaluate(AuroraExpressionNode node, AuroraExpressionEvaluationContext context)
        {
            context ??= AuroraExpressionEvaluationContext.Empty;

            if (node == null)
                return false;

            return node.Kind switch
            {
                "and" => node.Children.All(child => Evaluate(child, context)),
                "or" => node.Children.Any(child => Evaluate(child, context)),
                "not" => node.Children.Count == 0 || !Evaluate(node.Children[0], context),
                "value" => EvaluateValue(node, context),
                _ => false
            };
        }

        private static bool EvaluateValue(AuroraExpressionNode node, AuroraExpressionEvaluationContext context)
        {
            return node.ValueType switch
            {
                "macro" => context.MatchesMacro(node.ValueText),
                "bracket" => context.EvaluateBracket(node.ValueText),
                _ => context.MatchesToken(node.ValueText)
            };
        }

        private static List<ExpressionToken> Tokenize(string input)
        {
            var tokens = new List<ExpressionToken>();
            int index = 0;

            while (index < input.Length)
            {
                char ch = input[index];

                if (char.IsWhiteSpace(ch))
                {
                    index++;
                    continue;
                }

                if (StartsWith(input, index, "&&"))
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.And, "&&"));
                    index += 2;
                    continue;
                }

                if (StartsWith(input, index, "||"))
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Or, "||"));
                    index += 2;
                    continue;
                }

                if (ch == ',')
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.And, ","));
                    index++;
                    continue;
                }

                if (ch == '|')
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Or, "|"));
                    index++;
                    continue;
                }

                if (ch == '!')
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Not, "!"));
                    index++;
                    continue;
                }

                if (ch == '(')
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.LeftParen, "("));
                    index++;
                    continue;
                }

                if (ch == ')')
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.RightParen, ")"));
                    index++;
                    continue;
                }

                var current = new StringBuilder();
                int squareDepth = 0;
                int curlyDepth = 0;
                int macroDepth = 0;

                while (index < input.Length)
                {
                    if (StartsWith(input, index, "$("))
                    {
                        current.Append("$(");
                        macroDepth++;
                        index += 2;
                        continue;
                    }

                    ch = input[index];

                    if (macroDepth > 0)
                    {
                        current.Append(ch);
                        if (ch == '(')
                            macroDepth++;
                        else if (ch == ')')
                            macroDepth--;

                        index++;
                        continue;
                    }

                    if (ch == '[')
                    {
                        squareDepth++;
                        current.Append(ch);
                        index++;
                        continue;
                    }

                    if (ch == ']')
                    {
                        squareDepth = Math.Max(0, squareDepth - 1);
                        current.Append(ch);
                        index++;
                        continue;
                    }

                    if (ch == '{')
                    {
                        curlyDepth++;
                        current.Append(ch);
                        index++;
                        continue;
                    }

                    if (ch == '}')
                    {
                        curlyDepth = Math.Max(0, curlyDepth - 1);
                        current.Append(ch);
                        index++;
                        continue;
                    }

                    if (squareDepth == 0
                        && curlyDepth == 0
                        && (ch == '(' || ch == ')' || ch == ',' || ch == '!' || ch == '|'
                            || StartsWith(input, index, "&&") || StartsWith(input, index, "||")))
                    {
                        break;
                    }

                    current.Append(ch);
                    index++;
                }

                string value = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Value, value));
            }

            return tokens;
        }

        private static AuroraExpressionNode ParseOr(IReadOnlyList<ExpressionToken> tokens, ref int index)
        {
            AuroraExpressionNode left = ParseAnd(tokens, ref index);

            while (index < tokens.Count && tokens[index].Kind == ExpressionTokenKind.Or)
            {
                index++;
                AuroraExpressionNode right = ParseAnd(tokens, ref index);
                left = Combine("or", left, right);
            }

            return left;
        }

        private static AuroraExpressionNode ParseAnd(IReadOnlyList<ExpressionToken> tokens, ref int index)
        {
            AuroraExpressionNode left = ParseUnary(tokens, ref index);

            while (index < tokens.Count && tokens[index].Kind == ExpressionTokenKind.And)
            {
                index++;
                AuroraExpressionNode right = ParseUnary(tokens, ref index);
                left = Combine("and", left, right);
            }

            return left;
        }

        private static AuroraExpressionNode ParseUnary(IReadOnlyList<ExpressionToken> tokens, ref int index)
        {
            if (index < tokens.Count && tokens[index].Kind == ExpressionTokenKind.Not)
            {
                index++;
                var node = new AuroraExpressionNode { Kind = "not" };
                node.Children.Add(ParseUnary(tokens, ref index));
                return node;
            }

            return ParsePrimary(tokens, ref index);
        }

        private static AuroraExpressionNode ParsePrimary(IReadOnlyList<ExpressionToken> tokens, ref int index)
        {
            if (index >= tokens.Count)
                throw new InvalidOperationException("Unexpected end of expression.");

            ExpressionToken token = tokens[index];
            if (token.Kind == ExpressionTokenKind.LeftParen)
            {
                index++;
                AuroraExpressionNode inner = ParseOr(tokens, ref index);
                if (index >= tokens.Count || tokens[index].Kind != ExpressionTokenKind.RightParen)
                    throw new InvalidOperationException("Expected closing parenthesis.");

                index++;
                return inner;
            }

            if (token.Kind != ExpressionTokenKind.Value)
                throw new InvalidOperationException($"Unexpected token '{token.Text}'.");

            index++;
            return new AuroraExpressionNode
            {
                Kind = "value",
                ValueType = DetermineValueType(token.Text),
                ValueText = token.Text
            };
        }

        private static AuroraExpressionNode Combine(string kind, AuroraExpressionNode left, AuroraExpressionNode right)
        {
            if (left.Kind == kind)
            {
                left.Children.Add(right);
                return left;
            }

            var parent = new AuroraExpressionNode { Kind = kind };
            parent.Children.Add(left);
            parent.Children.Add(right);
            return parent;
        }

        private static string DetermineValueType(string tokenText)
        {
            string value = tokenText?.Trim() ?? string.Empty;
            if (value.StartsWith("$(", StringComparison.Ordinal))
                return "macro";

            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                return "bracket";

            if (value.StartsWith("ID_", StringComparison.OrdinalIgnoreCase))
                return "aurora-id";

            if (decimal.TryParse(value, out _))
                return "number";

            if (value.Contains('/') && value.Split('/').All(part => int.TryParse(part, out _)))
                return "fraction";

            return "text";
        }

        private static bool StartsWith(string input, int index, string candidate)
        {
            return input.AsSpan(index).StartsWith(candidate, StringComparison.Ordinal);
        }

        private static AuroraExpressionNode CreateFallbackValueNode(string rawText)
        {
            return new AuroraExpressionNode
            {
                Kind = "value",
                ValueType = "raw",
                ValueText = rawText?.Trim()
            };
        }

        private sealed record ExpressionToken(ExpressionTokenKind Kind, string Text);

        private enum ExpressionTokenKind
        {
            LeftParen,
            RightParen,
            And,
            Or,
            Not,
            Value
        }
    }
}

