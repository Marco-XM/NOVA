using System.Globalization;

namespace Nova.Core.Tools;

public readonly record struct EvaluationResult(bool Success, double Value, string? Error)
{
    public string Format()
    {
        if (!Success) return Error ?? "Error";
        if (double.IsNaN(Value)) return "Not a number";
        if (double.IsInfinity(Value)) return Value > 0 ? "∞" : "-∞";
        var rounded = Math.Round(Value, 10);
        if (Math.Abs(rounded) >= 1e15 || (Math.Abs(rounded) < 1e-6 && rounded != 0))
            return rounded.ToString("0.######E+0", CultureInfo.CurrentCulture);
        return rounded.ToString("#,0.##########", CultureInfo.CurrentCulture);
    }
}

/// <summary>
/// Safe recursive-descent calculator (no code execution, no reflection). Supports + - × ÷ % ^,
/// parentheses, unary minus, constants (pi, e) and common functions.
/// </summary>
public static class ExpressionEvaluator
{
    public static EvaluationResult Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return new(false, 0, "Empty");
        try
        {
            var parser = new Parser(Normalize(expression));
            var value = parser.ParseExpression();
            parser.SkipWhitespace();
            if (!parser.AtEnd) return new(false, 0, "Unexpected input");
            return new(true, value, null);
        }
        catch (FormatException ex)
        {
            return new(false, 0, ex.Message);
        }
        catch (DivideByZeroException)
        {
            return new(false, 0, "Can't divide by zero");
        }
    }

    private static string Normalize(string s) => s
        .Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace("**", "^")
        .Replace(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator == "," ? "," : "\u0000", ".");

    private sealed class Parser(string text)
    {
        private int _pos;
        private int _depth;

        public bool AtEnd => _pos >= text.Length;

        public void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        private bool Accept(char c)
        {
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == c) { _pos++; return true; }
            return false;
        }

        public double ParseExpression()
        {
            if (++_depth > 64) throw new FormatException("Too complex");
            var value = ParseTerm();
            while (true)
            {
                if (Accept('+')) value += ParseTerm();
                else if (Accept('-')) value -= ParseTerm();
                else break;
            }
            _depth--;
            return value;
        }

        private double ParseTerm()
        {
            var value = ParseUnary();
            while (true)
            {
                if (Accept('*')) value *= ParseUnary();
                else if (Accept('/'))
                {
                    var divisor = ParseUnary();
                    if (divisor == 0) throw new DivideByZeroException();
                    value /= divisor;
                }
                else if (Accept('%'))
                {
                    // "50%" alone means 0.5; "7 % 3" is modulo.
                    SkipWhitespace();
                    if (AtEnd || text[_pos] is ')' or '+' or '-' or '*' or '/') value /= 100.0;
                    else
                    {
                        var divisor = ParseUnary();
                        if (divisor == 0) throw new DivideByZeroException();
                        value %= divisor;
                    }
                }
                else break;
            }
            return value;
        }

        private double ParseUnary()
        {
            if (Accept('-')) return -ParseUnary();
            if (Accept('+')) return ParseUnary();
            return ParsePower();
        }

        private double ParsePower()
        {
            var b = ParsePrimary();
            if (Accept('^')) return Math.Pow(b, ParseUnary()); // right associative
            return b;
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (Accept('('))
            {
                var inner = ParseExpression();
                if (!Accept(')')) throw new FormatException("Missing )");
                return inner;
            }

            if (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.'))
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
                if (_pos < text.Length && (text[_pos] == 'e' || text[_pos] == 'E') && _pos + 1 < text.Length && (char.IsDigit(text[_pos + 1]) || text[_pos + 1] is '-' or '+'))
                {
                    _pos += 2;
                    while (_pos < text.Length && char.IsDigit(text[_pos])) _pos++;
                }
                if (!double.TryParse(text.AsSpan(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw new FormatException("Invalid number");
                return number;
            }

            if (_pos < text.Length && char.IsLetter(text[_pos]))
            {
                var start = _pos;
                while (_pos < text.Length && char.IsLetter(text[_pos])) _pos++;
                var name = text[start.._pos].ToLowerInvariant();
                switch (name)
                {
                    case "pi": return Math.PI;
                    case "e": return Math.E;
                }
                if (!Accept('(')) throw new FormatException($"Unknown '{name}'");
                var arg = ParseExpression();
                if (!Accept(')')) throw new FormatException("Missing )");
                return name switch
                {
                    "sqrt" => arg < 0 ? throw new FormatException("Invalid input") : Math.Sqrt(arg),
                    "sin" => Math.Sin(arg),
                    "cos" => Math.Cos(arg),
                    "tan" => Math.Tan(arg),
                    "abs" => Math.Abs(arg),
                    "ln" => arg <= 0 ? throw new FormatException("Invalid input") : Math.Log(arg),
                    "log" => arg <= 0 ? throw new FormatException("Invalid input") : Math.Log10(arg),
                    "round" => Math.Round(arg),
                    "floor" => Math.Floor(arg),
                    "ceil" => Math.Ceiling(arg),
                    _ => throw new FormatException($"Unknown function '{name}'"),
                };
            }

            throw new FormatException(AtEnd ? "Incomplete" : $"Unexpected '{text[_pos]}'");
        }
    }
}
