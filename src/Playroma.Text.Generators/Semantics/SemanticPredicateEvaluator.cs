using System.Globalization;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Semantics;

public sealed record SemanticPredicateResult(bool Success, bool Value, string? Error = null);

/// <summary>
/// Safe declarative predicate evaluator shared by Phase 6 constraints, filters and scorers.
/// Supported: &&, ||, !, parentheses, =, ==, !=, >, >=, <, <=, ~=, has(...), tag(...).
/// Identifiers resolve against feature.*, context.*, field.*, tag.*, score.*, project.id,
/// target.kind, target.id and primary. Bare names prefer semantic features then context values.
/// </summary>
public static class SemanticPredicateEvaluator
{
    public static SemanticPredicateResult Evaluate(string? expression, GenerationContext context, GeneratedTextBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(expression)) return new SemanticPredicateResult(true, true);
        try
        {
            var parser = new Parser(expression!, context, bundle);
            var value = parser.Parse();
            return new SemanticPredicateResult(true, value);
        }
        catch (PredicateParseException ex)
        {
            return new SemanticPredicateResult(false, false, ex.Message);
        }
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly GenerationContext _context;
        private readonly GeneratedTextBundle _bundle;
        private int _index;

        public Parser(string expression, GenerationContext context, GeneratedTextBundle bundle)
        {
            _tokens = Tokenize(expression);
            _context = context;
            _bundle = bundle;
        }

        public bool Parse()
        {
            var value = ParseOr();
            if (Peek().Kind != TokenKind.End) throw Error($"Unexpected token '{Peek().Text}'.");
            return value;
        }

        private bool ParseOr()
        {
            var left = ParseAnd();
            while (Match(TokenKind.Or))
            {
                var right = ParseAnd();
                left = left || right;
            }
            return left;
        }

        private bool ParseAnd()
        {
            var left = ParseUnary();
            while (Match(TokenKind.And))
            {
                var right = ParseUnary();
                left = left && right;
            }
            return left;
        }

        private bool ParseUnary()
        {
            if (Match(TokenKind.Not)) return !ParseUnary();
            if (Match(TokenKind.LParen))
            {
                var value = ParseOr();
                Expect(TokenKind.RParen, "Expected ')' to close predicate group.");
                return value;
            }
            return ParseComparison();
        }

        private bool ParseComparison()
        {
            var left = ParseValue();
            var op = Peek().Kind;
            if (op is TokenKind.Eq or TokenKind.Ne or TokenKind.Gt or TokenKind.Ge or TokenKind.Lt or TokenKind.Le or TokenKind.Contains)
            {
                _index++;
                var right = ParseValue();
                return Compare(left, right, op);
            }
            return Truthy(left);
        }

        private object? ParseValue()
        {
            var token = Peek();
            _index++;
            return token.Kind switch
            {
                TokenKind.String => token.Text,
                TokenKind.Number => double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0d,
                TokenKind.True => true,
                TokenKind.False => false,
                TokenKind.Null => null,
                TokenKind.Identifier => ParseIdentifierOrFunction(token.Text),
                _ => throw Error($"Expected a value but found '{token.Text}'.")
            };
        }

        private object? ParseIdentifierOrFunction(string name)
        {
            if (!Match(TokenKind.LParen)) return Resolve(name);
            var arguments = new List<object?>();
            if (Peek().Kind != TokenKind.RParen)
            {
                do { arguments.Add(ParseValue()); } while (Match(TokenKind.Comma));
            }
            Expect(TokenKind.RParen, $"Expected ')' after {name}(...).");
            return name.ToLowerInvariant() switch
            {
                "has" => arguments.Count == 1 && arguments[0] is not null && (arguments[0] is not string s || !string.IsNullOrWhiteSpace(s)),
                "tag" => arguments.Count == 1 && HasTag(Convert.ToString(arguments[0], CultureInfo.InvariantCulture) ?? string.Empty),
                _ => throw Error($"Unknown predicate function '{name}'.")
            };
        }

        private object? Resolve(string identifier)
        {
            if (identifier.Equals("primary", StringComparison.OrdinalIgnoreCase)) return _bundle.PrimaryText;
            if (identifier.Equals("project.id", StringComparison.OrdinalIgnoreCase)) return _context.ProjectId;
            if (identifier.Equals("target.kind", StringComparison.OrdinalIgnoreCase)) return _context.TargetKind;
            if (identifier.Equals("target.id", StringComparison.OrdinalIgnoreCase)) return _context.TargetId;

            if (TryPrefix(identifier, "feature.", out var feature))
                return _bundle.Features.TryGetValue(feature, out var f) ? f.ToObject() : null;
            if (TryPrefix(identifier, "context.", out var contextKey))
                return _context.Data.TryGetValue(contextKey, out var cv) ? cv : null;
            if (TryPrefix(identifier, "field.", out var field))
                return _bundle.Fields.FirstOrDefault(x => x.Name.Equals(field, StringComparison.OrdinalIgnoreCase))?.Text;
            if (TryPrefix(identifier, "tag.", out var tag))
            {
                if (_bundle.Tags.TryGetValue(tag, out var tagValue)) return tagValue;
                return _context.EffectiveTags.Contains(tag);
            }
            if (TryPrefix(identifier, "score.", out var score))
                return _bundle.Scores.LastOrDefault(x => x.Name.Equals(score, StringComparison.OrdinalIgnoreCase))?.Value;

            if (_bundle.Features.TryGetValue(identifier, out var featureValue)) return featureValue.ToObject();
            if (_context.Data.TryGetValue(identifier, out var contextValue)) return contextValue;
            if (_bundle.Tags.TryGetValue(identifier, out var bundleTag)) return bundleTag;
            if (_context.EffectiveTags.Contains(identifier)) return true;
            return null;
        }

        private bool HasTag(string tag) => _context.EffectiveTags.Contains(tag) || _bundle.Tags.ContainsKey(tag) ||
            _bundle.Tags.Values.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));

        private static bool TryPrefix(string input, string prefix, out string suffix)
        {
            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { suffix = input[prefix.Length..]; return true; }
            suffix = string.Empty; return false;
        }

        private static bool Compare(object? left, object? right, TokenKind op)
        {
            if (left is IEnumerable<string> leftList && left is not string)
                return CompareList(leftList, right, op);
            if (right is IEnumerable<string> rightList && right is not string)
                return CompareRightList(left, rightList, op);

            if (TryNumber(left, out var l) && TryNumber(right, out var r))
            {
                return op switch
                {
                    TokenKind.Eq => Math.Abs(l - r) <= 1e-12,
                    TokenKind.Ne => Math.Abs(l - r) > 1e-12,
                    TokenKind.Gt => l > r,
                    TokenKind.Ge => l >= r,
                    TokenKind.Lt => l < r,
                    TokenKind.Le => l <= r,
                    TokenKind.Contains => Convert.ToString(left, CultureInfo.InvariantCulture)?.Contains(Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
                    _ => false
                };
            }

            if (left is bool lb && right is bool rb)
                return op switch { TokenKind.Eq => lb == rb, TokenKind.Ne => lb != rb, _ => false };

            var ls = Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty;
            var rs = Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty;
            var cmp = string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
            return op switch
            {
                TokenKind.Eq => cmp == 0,
                TokenKind.Ne => cmp != 0,
                TokenKind.Gt => cmp > 0,
                TokenKind.Ge => cmp >= 0,
                TokenKind.Lt => cmp < 0,
                TokenKind.Le => cmp <= 0,
                TokenKind.Contains => ls.Contains(rs, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static bool CompareList(IEnumerable<string> values, object? right, TokenKind op)
        {
            var rs = Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty;
            var anyEqual = values.Any(x => string.Equals(x, rs, StringComparison.OrdinalIgnoreCase));
            var anyContains = values.Any(x => x.Contains(rs, StringComparison.OrdinalIgnoreCase));
            return op switch { TokenKind.Eq => anyEqual, TokenKind.Ne => !anyEqual, TokenKind.Contains => anyContains, _ => false };
        }

        private static bool CompareRightList(object? left, IEnumerable<string> values, TokenKind op)
        {
            var ls = Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty;
            var anyEqual = values.Any(x => string.Equals(x, ls, StringComparison.OrdinalIgnoreCase));
            return op switch { TokenKind.Eq => anyEqual, TokenKind.Ne => !anyEqual, TokenKind.Contains => values.Any(x => x.Contains(ls, StringComparison.OrdinalIgnoreCase)), _ => false };
        }

        private static bool TryNumber(object? value, out double number)
        {
            if (value is null) { number = 0; return false; }
            if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            {
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture); return true;
            }
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static bool Truthy(object? value) => value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0",
            IEnumerable<string> values => values.Any(),
            _ when TryNumber(value, out var n) => Math.Abs(n) > double.Epsilon,
            _ => true
        };

        private Token Peek() => _tokens[Math.Min(_index, _tokens.Count - 1)];
        private bool Match(TokenKind kind) { if (Peek().Kind != kind) return false; _index++; return true; }
        private void Expect(TokenKind kind, string message) { if (!Match(kind)) throw Error(message); }
        private PredicateParseException Error(string message) => new($"{message} Position {Peek().Position}.");
    }

    private static List<Token> Tokenize(string expression)
    {
        var result = new List<Token>();
        var i = 0;
        while (i < expression.Length)
        {
            if (char.IsWhiteSpace(expression[i])) { i++; continue; }
            var start = i;
            var c = expression[i];
            if (c is '\'' or '"')
            {
                var quote = c; i++;
                var sb = new System.Text.StringBuilder();
                while (i < expression.Length && expression[i] != quote)
                {
                    if (expression[i] == '\\' && i + 1 < expression.Length) { i++; sb.Append(expression[i++]); }
                    else sb.Append(expression[i++]);
                }
                if (i >= expression.Length) throw new PredicateParseException($"Unterminated string at position {start}.");
                i++;
                result.Add(new Token(TokenKind.String, sb.ToString(), start));
                continue;
            }
            if (char.IsDigit(c) || (c == '-' && i + 1 < expression.Length && char.IsDigit(expression[i + 1])))
            {
                i++;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] is '.' or 'e' or 'E' or '+' or '-')) i++;
                result.Add(new Token(TokenKind.Number, expression[start..i], start)); continue;
            }
            if (char.IsLetter(c) || c is '_' or '$')
            {
                i++;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] is '_' or '.' or '-' or ':' or '$')) i++;
                var text = expression[start..i];
                var identifierKind = text.Equals("true", StringComparison.OrdinalIgnoreCase) ? TokenKind.True :
                    text.Equals("false", StringComparison.OrdinalIgnoreCase) ? TokenKind.False :
                    text.Equals("null", StringComparison.OrdinalIgnoreCase) ? TokenKind.Null : TokenKind.Identifier;
                result.Add(new Token(identifierKind, text, start)); continue;
            }

            if (i + 1 < expression.Length)
            {
                var pair = expression.Substring(i, 2);
                var pairKind = pair switch
                {
                    "&&" => TokenKind.And,
                    "||" => TokenKind.Or,
                    "==" => TokenKind.Eq,
                    "!=" => TokenKind.Ne,
                    ">=" => TokenKind.Ge,
                    "<=" => TokenKind.Le,
                    "~=" => TokenKind.Contains,
                    _ => TokenKind.End
                };
                if (pairKind != TokenKind.End) { result.Add(new Token(pairKind, pair, i)); i += 2; continue; }
            }

            var kind = c switch
            {
                '!' => TokenKind.Not,
                '=' => TokenKind.Eq,
                '>' => TokenKind.Gt,
                '<' => TokenKind.Lt,
                '(' => TokenKind.LParen,
                ')' => TokenKind.RParen,
                ',' => TokenKind.Comma,
                _ => throw new PredicateParseException($"Unexpected character '{c}' at position {i}.")
            };
            result.Add(new Token(kind, c.ToString(), i)); i++;
        }
        result.Add(new Token(TokenKind.End, string.Empty, expression.Length));
        return result;
    }

    private enum TokenKind { Identifier, String, Number, True, False, Null, And, Or, Not, Eq, Ne, Gt, Ge, Lt, Le, Contains, LParen, RParen, Comma, End }
    private sealed record Token(TokenKind Kind, string Text, int Position);
    private sealed class PredicateParseException(string message) : Exception(message);
}
