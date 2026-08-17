using System.Diagnostics;
using System.Globalization;

namespace Playroma.JRPG.FormulaSandbox;

public static class FormulaSandboxEvaluator
{
    public static FormulaSandboxResult Evaluate(FormulaSandboxRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            int sampleCount = Math.Clamp(request.SampleCount, 1, 10_000);
            DeterministicRandom random = new(request.Seed);
            EvaluationContext context = new(request, random);
            FormulaExpressionParser parser = new(request.Formula, context);
            double raw = parser.Parse();
            if (!double.IsFinite(raw)) throw new InvalidDataException("The formula result was not a finite number.");

            int sign = request.Item.DamageType is 3 or 4 ? -1 : 1;
            double baseDamage = Math.Max(raw, 0) * sign;
            double criticalDamage = request.ApplyCritical && request.Item.Critical ? baseDamage * 3d : baseDamage;
            List<double> samples = new(sampleCount);
            for (int index = 0; index < sampleCount; index++)
            {
                samples.Add(ApplyVariance(criticalDamage, request.Item.Variance, random));
            }

            return new FormulaSandboxResult(
                true,
                "MV Core Approximation",
                raw,
                baseDamage,
                criticalDamage,
                samples.Min(),
                samples.Average(),
                samples.Max(),
                samples.Count <= 256 ? samples : samples.Take(256).ToArray(),
                context.ReferencedProperties.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                context.ReferencedVariables.OrderBy(value => value).ToArray(),
                null,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            return FormulaSandboxResult.Failure(exception.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    private static double ApplyVariance(double damage, int variance, DeterministicRandom random)
    {
        int amplitude = (int)Math.Floor(Math.Max(Math.Abs(damage) * Math.Clamp(variance, 0, 100) / 100d, 0));
        if (amplitude <= 0) return Math.Round(damage);
        int varied = (int)Math.Round(damage) + random.Next(amplitude + 1) + random.Next(amplitude + 1) - amplitude;
        return varied;
    }
}

internal sealed class EvaluationContext
{
    private readonly FormulaSandboxRequest _request;
    private readonly DeterministicRandom _random;
    private readonly IReadOnlyDictionary<string, double> _user;
    private readonly IReadOnlyDictionary<string, double> _target;
    private readonly Dictionary<string, double> _item;

    public EvaluationContext(FormulaSandboxRequest request, DeterministicRandom random)
    {
        _request = request;
        _random = random;
        _user = request.User.ToValues();
        _target = request.Target.ToValues();
        _item = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = request.Item.Id,
            ["damageType"] = request.Item.DamageType,
            ["elementId"] = request.Item.ElementId,
            ["variance"] = request.Item.Variance,
            ["critical"] = request.Item.Critical ? 1 : 0
        };
        if (request.Item.Extra is not null)
        {
            foreach ((string key, double value) in request.Item.Extra) _item[key] = value;
        }
    }

    public HashSet<string> ReferencedProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<int> ReferencedVariables { get; } = [];

    public double ResolveProperty(string root, IReadOnlyList<string> properties)
    {
        if (properties.Count == 0)
        {
            throw new InvalidDataException($"'{root}' must be followed by a property.");
        }
        string key = string.Join('.', properties);
        ReferencedProperties.Add(root + "." + key);
        IReadOnlyDictionary<string, double> values = root.ToLowerInvariant() switch
        {
            "a" or "user" => _user,
            "b" or "target" => _target,
            "item" => _item,
            _ => throw new InvalidDataException($"Unknown formula object '{root}'.")
        };
        if (values.TryGetValue(key, out double value)) return value;
        if (properties.Count == 2 && string.Equals(properties[0], "damage", StringComparison.OrdinalIgnoreCase)
            && values.TryGetValue(properties[1], out value)) return value;
        throw new InvalidDataException($"Unknown formula property '{root}.{key}'.");
    }

    public double ResolveVariable(int id)
    {
        if (id < 0) throw new InvalidDataException("Variable IDs cannot be negative.");
        ReferencedVariables.Add(id);
        return _request.Variables is not null && _request.Variables.TryGetValue(id, out double value) ? value : 0;
    }

    public double InvokeFunction(string name, IReadOnlyList<double> arguments)
    {
        string normalized = name.StartsWith("Math.", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
        return normalized.ToLowerInvariant() switch
        {
            "abs" => One(Math.Abs),
            "floor" => One(Math.Floor),
            "ceil" => One(Math.Ceiling),
            "round" => One(Math.Round),
            "sqrt" => One(Math.Sqrt),
            "trunc" => One(Math.Truncate),
            "sign" => One(value => Math.Sign(value)),
            "sin" => One(Math.Sin),
            "cos" => One(Math.Cos),
            "tan" => One(Math.Tan),
            "log" => One(Math.Log),
            "exp" => One(Math.Exp),
            "pow" => Two(Math.Pow),
            "min" => arguments.Count > 0 ? arguments.Min() : throw Arity("min", "at least one"),
            "max" => arguments.Count > 0 ? arguments.Max() : throw Arity("max", "at least one"),
            "random" => arguments.Count == 0 ? _random.NextDouble() : throw Arity("random", "zero"),
            "randomint" => arguments.Count == 1 ? _random.Next(Math.Max(1, checked((int)Math.Truncate(arguments[0])))) : throw Arity("randomInt", "one"),
            _ => throw new InvalidDataException($"Unsupported formula function '{name}'.")
        };

        double One(Func<double, double> operation) => arguments.Count == 1 ? operation(arguments[0]) : throw Arity(name, "one");
        double Two(Func<double, double, double> operation) => arguments.Count == 2 ? operation(arguments[0], arguments[1]) : throw Arity(name, "two");
        static Exception Arity(string function, string expected) => new InvalidDataException($"Function '{function}' requires {expected} argument(s). ");
    }
}

internal sealed class DeterministicRandom
{
    private readonly Random _random;
    public DeterministicRandom(int seed) => _random = new Random(seed);
    public int Next(int maximumExclusive) => _random.Next(maximumExclusive);
    public double NextDouble() => _random.NextDouble();
}

internal sealed class FormulaExpressionParser
{
    private readonly FormulaLexer _lexer;
    private readonly EvaluationContext _context;
    private FormulaToken _current;

    public FormulaExpressionParser(string expression, EvaluationContext context)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new InvalidDataException("The damage formula is empty.");
        _lexer = new FormulaLexer(expression);
        _context = context;
        _current = _lexer.Next();
    }

    public double Parse()
    {
        double result = ParseConditional();
        Expect(FormulaTokenKind.End);
        return result;
    }

    private double ParseConditional()
    {
        double condition = ParseOr();
        if (!Match("?")) return condition;
        double whenTrue = ParseConditional();
        Require(":");
        double whenFalse = ParseConditional();
        return Truthy(condition) ? whenTrue : whenFalse;
    }

    private double ParseOr()
    {
        double left = ParseAnd();
        while (Match("||"))
        {
            double right = ParseAnd();
            left = Truthy(left) || Truthy(right) ? 1 : 0;
        }
        return left;
    }

    private double ParseAnd()
    {
        double left = ParseEquality();
        while (Match("&&"))
        {
            double right = ParseEquality();
            left = Truthy(left) && Truthy(right) ? 1 : 0;
        }
        return left;
    }

    private double ParseEquality()
    {
        double left = ParseComparison();
        while (_current.Text is "==" or "===" or "!=" or "!==")
        {
            string op = Consume().Text;
            double right = ParseComparison();
            bool equal = Math.Abs(left - right) <= double.Epsilon;
            left = op.StartsWith('!') ? (equal ? 0 : 1) : (equal ? 1 : 0);
        }
        return left;
    }

    private double ParseComparison()
    {
        double left = ParseAdditive();
        while (_current.Text is "<" or "<=" or ">" or ">=")
        {
            string op = Consume().Text;
            double right = ParseAdditive();
            left = op switch
            {
                "<" => left < right ? 1 : 0,
                "<=" => left <= right ? 1 : 0,
                ">" => left > right ? 1 : 0,
                ">=" => left >= right ? 1 : 0,
                _ => 0
            };
        }
        return left;
    }

    private double ParseAdditive()
    {
        double left = ParseMultiplicative();
        while (_current.Text is "+" or "-")
        {
            string op = Consume().Text;
            double right = ParseMultiplicative();
            left = op == "+" ? left + right : left - right;
        }
        return left;
    }

    private double ParseMultiplicative()
    {
        double left = ParsePower();
        while (_current.Text is "*" or "/" or "%")
        {
            string op = Consume().Text;
            double right = ParsePower();
            left = op switch
            {
                "*" => left * right,
                "/" => right == 0 ? throw new DivideByZeroException("The damage formula divided by zero.") : left / right,
                "%" => right == 0 ? throw new DivideByZeroException("The damage formula used modulo zero.") : left % right,
                _ => left
            };
        }
        return left;
    }

    private double ParsePower()
    {
        double left = ParseUnary();
        if (Match("**")) return Math.Pow(left, ParsePower());
        return left;
    }

    private double ParseUnary()
    {
        if (Match("+")) return ParseUnary();
        if (Match("-")) return -ParseUnary();
        if (Match("!")) return Truthy(ParseUnary()) ? 0 : 1;
        return ParsePrimary();
    }

    private double ParsePrimary()
    {
        if (_current.Kind == FormulaTokenKind.Number)
        {
            FormulaToken token = Consume();
            return double.Parse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        if (Match("("))
        {
            double value = ParseConditional();
            Require(")");
            return value;
        }
        if (_current.Kind != FormulaTokenKind.Identifier)
        {
            throw Error($"Expected a number, formula object, or function but found '{_current.Text}'.");
        }

        string root = Consume().Text;
        if (string.Equals(root, "true", StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(root, "false", StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.Equals(root, "Infinity", StringComparison.Ordinal)) return double.PositiveInfinity;
        if (string.Equals(root, "NaN", StringComparison.Ordinal)) return double.NaN;

        if (string.Equals(root, "v", StringComparison.OrdinalIgnoreCase))
        {
            Require("[");
            double rawId = ParseConditional();
            Require("]");
            int id = checked((int)Math.Truncate(rawId));
            return _context.ResolveVariable(id);
        }

        List<string> members = [];
        while (Match("."))
        {
            if (_current.Kind != FormulaTokenKind.Identifier) throw Error("Expected a property or function name after '.'.");
            members.Add(Consume().Text);
        }
        if (Match("("))
        {
            List<double> arguments = [];
            if (!Match(")"))
            {
                do { arguments.Add(ParseConditional()); } while (Match(","));
                Require(")");
            }
            string name = members.Count == 0 ? root : root + "." + string.Join('.', members);
            return _context.InvokeFunction(name, arguments);
        }
        return _context.ResolveProperty(root, members);
    }

    private FormulaToken Consume()
    {
        FormulaToken result = _current;
        _current = _lexer.Next();
        return result;
    }

    private bool Match(string text)
    {
        if (!string.Equals(_current.Text, text, StringComparison.Ordinal)) return false;
        Consume();
        return true;
    }

    private void Require(string text)
    {
        if (!Match(text)) throw Error($"Expected '{text}' but found '{_current.Text}'.");
    }

    private void Expect(FormulaTokenKind kind)
    {
        if (_current.Kind != kind) throw Error($"Unexpected token '{_current.Text}'.");
    }

    private InvalidDataException Error(string message) => new($"{message} At character {_current.Position + 1}.");
    private static bool Truthy(double value) => value != 0 && !double.IsNaN(value);
}

internal enum FormulaTokenKind
{
    End,
    Number,
    Identifier,
    Operator
}

internal readonly record struct FormulaToken(FormulaTokenKind Kind, string Text, int Position);

internal sealed class FormulaLexer
{
    private readonly string _text;
    private int _position;

    public FormulaLexer(string text) => _text = text;

    public FormulaToken Next()
    {
        SkipWhitespace();
        if (_position >= _text.Length) return new FormulaToken(FormulaTokenKind.End, string.Empty, _position);
        int start = _position;
        char current = _text[_position];
        if (char.IsDigit(current) || current == '.' && _position + 1 < _text.Length && char.IsDigit(_text[_position + 1]))
        {
            _position++;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
            if (_position < _text.Length && (_text[_position] == 'e' || _text[_position] == 'E'))
            {
                _position++;
                if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-')) _position++;
                while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
            }
            return new FormulaToken(FormulaTokenKind.Number, _text[start.._position], start);
        }
        if (char.IsLetter(current) || current is '_' or '$')
        {
            _position++;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] is '_' or '$')) _position++;
            return new FormulaToken(FormulaTokenKind.Identifier, _text[start.._position], start);
        }

        string[] operators = ["===", "!==", "**", "<=", ">=", "==", "!=", "&&", "||"];
        foreach (string op in operators)
        {
            if (_position + op.Length <= _text.Length && string.CompareOrdinal(_text, _position, op, 0, op.Length) == 0)
            {
                _position += op.Length;
                return new FormulaToken(FormulaTokenKind.Operator, op, start);
            }
        }
        if ("+-*/%()[]<>,.!?:".IndexOf(current) >= 0)
        {
            _position++;
            return new FormulaToken(FormulaTokenKind.Operator, current.ToString(), start);
        }
        throw new InvalidDataException($"Unsupported character '{current}' at character {start + 1}.");
    }

    private void SkipWhitespace()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
    }
}
