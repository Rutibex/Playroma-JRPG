using System.Globalization;
using PlayromaMusic.Core.AdvancedComposition;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.Randomness;
using PlayromaMusic.Core.StructuredComposition;

namespace PlayromaMusic.Generators.Scripting;

public sealed class MusicalExpressionEngine
{
    public double Evaluate(string expression, IReadOnlyDictionary<string, double> variables)
    {
        if (string.IsNullOrWhiteSpace(expression)) return 0;
        Parser parser = new(expression, variables);
        double result = parser.ParseExpression();
        parser.ExpectEnd();
        if (!double.IsFinite(result)) throw new InvalidDataException("Expression produced a non-finite value.");
        return result;
    }

    public void Apply(ProjectDocument project, CompositionRoleSettings role, SongTrack track)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(track);
        if (!project.AdvancedComposition.Extensions.EnableUserScripts) return;

        ExpressionScriptDefinition[] scripts = project.AdvancedComposition.Scripts
            .Where(script => script.Enabled && (script.TargetRoleId == "*" || script.TargetRoleId.Equals(role.Id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (scripts.Length == 0) return;

        long beatTicks = Math.Max(1, project.Song.TicksPerQuarterNote * 4L / Math.Max(1, project.Song.MeterMap.FirstOrDefault()?.Denominator ?? 4));
        int beatsPerBar = Math.Max(1, project.Song.MeterMap.FirstOrDefault()?.Numerator ?? 4);
        long barTicks = beatTicks * beatsPerBar;
        int noteIndex = 0;
        HashSet<string> reportedScriptFailures = new(StringComparer.OrdinalIgnoreCase);
        foreach (SongClip clip in track.Clips)
        {
            SongSection? section = project.Song.Sections.FirstOrDefault(item => item.Id == clip.Id || item.StartTick == clip.StartTick);
            double energy = section?.Energy ?? 0.5;
            List<NoteEvent> retained = new();
            foreach (NoteEvent note in clip.Notes.OrderBy(item => item.StartTick).ThenBy(item => item.Pitch))
            {
                bool keep = true;
                double pitch = note.Pitch;
                double velocity = note.Velocity;
                double duration = note.DurationTicks;
                long absoluteTick = clip.StartTick + note.StartTick;
                int bar = (int)(absoluteTick / Math.Max(1, barTicks));
                double beat = (absoluteTick % Math.Max(1, barTicks)) / (double)beatTicks;
                double tension = project.AdvancedComposition.TensionCurve
                    .Where(point => point.Bar <= bar)
                    .OrderBy(point => point.Bar)
                    .LastOrDefault()?.Value ?? energy;
                DeterministicRandom random = new(SeedDerivation.Derive(project.MasterSeed, "stage6", "script", role.Id, note.Id));

                foreach (ExpressionScriptDefinition script in scripts)
                {
                    Dictionary<string, double> vars = new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["pitch"] = pitch,
                        ["velocity"] = velocity,
                        ["duration"] = duration,
                        ["index"] = noteIndex,
                        ["bar"] = bar,
                        ["beat"] = beat,
                        ["energy"] = energy,
                        ["tension"] = tension,
                        ["random"] = random.NextDouble(),
                        ["pi"] = Math.PI
                    };
                    foreach ((string key, double value) in script.Parameters) vars[key] = value;
                    try
                    {
                        bool scriptKeep = Evaluate(script.GateExpression, vars) > 0.0;
                        if (!scriptKeep)
                        {
                            keep = false;
                            break;
                        }
                        double nextPitch = Evaluate(script.PitchExpression, vars);
                        vars["pitch"] = nextPitch;
                        double nextVelocity = Evaluate(script.VelocityExpression, vars);
                        vars["velocity"] = nextVelocity;
                        double nextDuration = Evaluate(script.DurationExpression, vars);
                        pitch = nextPitch;
                        velocity = nextVelocity;
                        duration = nextDuration;
                    }
                    catch (Exception exception)
                    {
                        if (reportedScriptFailures.Add(script.Id))
                        {
                            project.AdvancedComposition.Diagnostics.Add($"Script '{script.Name}' was ignored after failing on note {note.Id}: {exception.Message}");
                        }
                    }
                }

                if (keep)
                {
                    note.Pitch = Math.Clamp((int)Math.Round(pitch), 0, 127);
                    note.Velocity = Math.Clamp((int)Math.Round(velocity), 1, 127);
                    long remainingTicks = Math.Max(1, clip.LengthTicks - note.StartTick);
                    note.DurationTicks = Math.Clamp((long)Math.Round(duration), 1, remainingTicks);
                    retained.Add(note);
                }
                noteIndex++;
            }
            clip.Notes = retained;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, double> _variables;
        private int _position;

        public Parser(string text, IReadOnlyDictionary<string, double> variables)
        {
            _text = text;
            _variables = variables;
        }

        public double ParseExpression() => ParseComparison();

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_position != _text.Length) throw Error("Unexpected trailing input.");
        }

        private double ParseComparison()
        {
            double left = ParseAdditive();
            while (true)
            {
                string? op = MatchAny(">=", "<=", "==", "!=", ">", "<");
                if (op is null) return left;
                double right = ParseAdditive();
                left = op switch
                {
                    ">=" => left >= right ? 1 : 0,
                    "<=" => left <= right ? 1 : 0,
                    "==" => Math.Abs(left - right) < 0.0000001 ? 1 : 0,
                    "!=" => Math.Abs(left - right) >= 0.0000001 ? 1 : 0,
                    ">" => left > right ? 1 : 0,
                    "<" => left < right ? 1 : 0,
                    _ => 0
                };
            }
        }

        private double ParseAdditive()
        {
            double value = ParseMultiplicative();
            while (true)
            {
                if (Match("+")) value += ParseMultiplicative();
                else if (Match("-")) value -= ParseMultiplicative();
                else return value;
            }
        }

        private double ParseMultiplicative()
        {
            double value = ParsePower();
            while (true)
            {
                if (Match("*")) value *= ParsePower();
                else if (Match("/")) value /= ParsePower();
                else if (Match("%")) value %= ParsePower();
                else return value;
            }
        }

        private double ParsePower()
        {
            double value = ParseUnary();
            if (Match("^")) value = Math.Pow(value, ParsePower());
            return value;
        }

        private double ParseUnary()
        {
            if (Match("+")) return ParseUnary();
            if (Match("-")) return -ParseUnary();
            if (Match("!")) return ParseUnary() > 0 ? 0 : 1;
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Match("("))
            {
                double value = ParseExpression();
                if (!Match(")")) throw Error("Missing closing parenthesis.");
                return value;
            }
            if (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) return ParseNumber();
            string identifier = ParseIdentifier();
            if (Match("("))
            {
                List<double> args = new();
                if (!Match(")"))
                {
                    do { args.Add(ParseExpression()); } while (Match(","));
                    if (!Match(")")) throw Error("Missing closing function parenthesis.");
                }
                return Call(identifier, args);
            }
            if (_variables.TryGetValue(identifier, out double variable)) return variable;
            throw Error($"Unknown variable '{identifier}'.");
        }

        private double ParseNumber()
        {
            SkipWhitespace();
            int start = _position;
            while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] is '.' or 'e' or 'E' or '+' or '-'))
            {
                char current = _text[_position];
                if ((current is '+' or '-') && _position > start && _text[_position - 1] is not ('e' or 'E')) break;
                _position++;
            }
            if (!double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error("Invalid number.");
            return value;
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            int start = _position;
            if (_position < _text.Length && (char.IsLetter(_text[_position]) || _text[_position] == '_')) _position++;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_')) _position++;
            if (start == _position) throw Error("Expected a number, variable or function.");
            return _text[start.._position];
        }

        private static double Call(string name, IReadOnlyList<double> args) => name.ToLowerInvariant() switch
        {
            "min" when args.Count >= 2 => args.Min(),
            "max" when args.Count >= 2 => args.Max(),
            "clamp" when args.Count == 3 => Math.Clamp(args[0], args[1], args[2]),
            "abs" when args.Count == 1 => Math.Abs(args[0]),
            "sin" when args.Count == 1 => Math.Sin(args[0]),
            "cos" when args.Count == 1 => Math.Cos(args[0]),
            "floor" when args.Count == 1 => Math.Floor(args[0]),
            "ceil" when args.Count == 1 => Math.Ceiling(args[0]),
            "round" when args.Count == 1 => Math.Round(args[0]),
            "sqrt" when args.Count == 1 => Math.Sqrt(args[0]),
            "pow" when args.Count == 2 => Math.Pow(args[0], args[1]),
            "if" when args.Count == 3 => args[0] > 0 ? args[1] : args[2],
            _ => throw new InvalidDataException($"Unknown function or wrong argument count: {name}.")
        };

        private bool Match(string token)
        {
            SkipWhitespace();
            if (!_text.AsSpan(_position).StartsWith(token, StringComparison.Ordinal)) return false;
            _position += token.Length;
            return true;
        }

        private string? MatchAny(params string[] tokens)
        {
            foreach (string token in tokens) if (Match(token)) return token;
            return null;
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
        }

        private InvalidDataException Error(string message) => new($"{message} At character {_position + 1} in '{_text}'.");
    }
}
