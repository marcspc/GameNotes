using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameNotes.Formula
{
    /// <summary>
    /// Deliberately small formula engine (we're not trying to build Excel).
    /// Supports:
    ///   - Cell references: B2, A1, C10...
    ///   - Ranges: B2:B10
    ///   - Operators: + - * /
    ///   - Functions: SUM, AVERAGE, MIN, MAX
    ///
    /// Any error (broken reference, unknown function, division by zero...)
    /// is caught and returned as a cell error, never as an unhandled
    /// exception: a bad formula can never crash Playnite.
    /// </summary>
    public class FormulaEngine
    {
        /// <summary>
        /// Resolves the numeric value of a cell given its reference (e.g. "B2").
        /// Implemented by the table/grid UI, which knows how to map columns/rows.
        /// </summary>
        public Func<string, double?> CellValueResolver { get; set; }

        private static readonly Regex RangeRegex = new Regex(@"^([A-Za-z]+\d+):([A-Za-z]+\d+)$", RegexOptions.Compiled);
        private static readonly Regex FunctionRegex = new Regex(@"^([A-Za-z]+)\((.*)\)$", RegexOptions.Compiled);
        private static readonly Regex CellRegex = new Regex(@"^[A-Za-z]+\d+$", RegexOptions.Compiled);

        public class FormulaResult
        {
            public bool IsError { get; set; }
            public string ErrorText { get; set; }
            public double Value { get; set; }

            public override string ToString() => IsError ? "#ERROR: " + ErrorText : Value.ToString("G10");
        }

        public FormulaResult Evaluate(string formula)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(formula))
                {
                    return new FormulaResult { Value = 0 };
                }

                var expr = formula.Trim();
                if (expr.StartsWith("="))
                {
                    expr = expr.Substring(1);
                }

                var value = EvaluateExpression(expr);
                return new FormulaResult { Value = value };
            }
            catch (Exception ex)
            {
                // The error is shown on the cell, never as an exception that
                // bubbles up to the UI or the Playnite host.
                return new FormulaResult { IsError = true, ErrorText = ex.Message };
            }
        }

        private double EvaluateExpression(string expr)
        {
            expr = expr.Trim();

            // A function that spans the whole expression: SUM(B2:B10)
            var fMatch = FunctionRegex.Match(expr);
            if (fMatch.Success && IsWholeMatch(expr, fMatch))
            {
                return EvaluateFunction(fMatch.Groups[1].Value.ToUpperInvariant(), fMatch.Groups[2].Value);
            }

            // Simple binary operators (left to right, no advanced precedence
            // beyond * / over + -: enough for builds/DPS/inventories).
            var tokens = Tokenize(expr);
            return EvaluateTokens(tokens);
        }

        private bool IsWholeMatch(string expr, Match m) => m.Value.Length == expr.Length;

        private double EvaluateFunction(string name, string argsRaw)
        {
            var values = new List<double>();
            foreach (var part in SplitArgs(argsRaw))
            {
                var trimmed = part.Trim();
                if (RangeRegex.IsMatch(trimmed))
                {
                    values.AddRange(ResolveRange(trimmed));
                }
                else if (CellRegex.IsMatch(trimmed))
                {
                    values.Add(ResolveCell(trimmed));
                }
                else
                {
                    values.Add(EvaluateExpression(trimmed));
                }
            }

            switch (name)
            {
                case "SUM": return values.Sum();
                case "AVERAGE": return values.Count == 0 ? 0 : values.Average();
                case "MIN": return values.Count == 0 ? 0 : values.Min();
                case "MAX": return values.Count == 0 ? 0 : values.Max();
                default:
                    throw new InvalidOperationException($"Unknown function: {name}");
            }
        }

        private IEnumerable<string> SplitArgs(string argsRaw)
        {
            // Simple comma split (no nested functions with commas inside
            // ranges, so this is enough).
            return argsRaw.Split(',').Where(s => !string.IsNullOrWhiteSpace(s));
        }

        private IEnumerable<double> ResolveRange(string range)
        {
            var m = RangeRegex.Match(range);
            var (col1, row1) = SplitCellRef(m.Groups[1].Value);
            var (col2, row2) = SplitCellRef(m.Groups[2].Value);

            int colStart = Math.Min(ColumnToIndex(col1), ColumnToIndex(col2));
            int colEnd = Math.Max(ColumnToIndex(col1), ColumnToIndex(col2));
            int rowStart = Math.Min(row1, row2);
            int rowEnd = Math.Max(row1, row2);

            for (int c = colStart; c <= colEnd; c++)
            {
                for (int r = rowStart; r <= rowEnd; r++)
                {
                    yield return ResolveCell(IndexToColumn(c) + r);
                }
            }
        }

        private double ResolveCell(string cellRef)
        {
            if (CellValueResolver == null)
            {
                throw new InvalidOperationException("No table is attached to this formula.");
            }

            var value = CellValueResolver(cellRef);
            if (value == null)
            {
                throw new InvalidOperationException($"Invalid reference: {cellRef}");
            }

            return value.Value;
        }

        private static (string col, int row) SplitCellRef(string cellRef)
        {
            var match = Regex.Match(cellRef, @"^([A-Za-z]+)(\d+)$");
            if (!match.Success)
            {
                throw new InvalidOperationException($"Invalid reference: {cellRef}");
            }
            return (match.Groups[1].Value.ToUpperInvariant(), int.Parse(match.Groups[2].Value));
        }

        private static int ColumnToIndex(string col)
        {
            int index = 0;
            foreach (var ch in col.ToUpperInvariant())
            {
                index = index * 26 + (ch - 'A' + 1);
            }
            return index;
        }

        private static string IndexToColumn(int index)
        {
            var result = "";
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                result = (char)('A' + rem) + result;
                index = (index - 1) / 26;
            }
            return result;
        }

        // --- Tokenizer / evaluator for simple arithmetic expressions ---

        private List<string> Tokenize(string expr)
        {
            var tokens = new List<string>();
            var current = "";
            foreach (var ch in expr)
            {
                if ("+-*/()".IndexOf(ch) >= 0)
                {
                    if (current.Length > 0) { tokens.Add(current); current = ""; }
                    tokens.Add(ch.ToString());
                }
                else if (char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0) { tokens.Add(current); current = ""; }
                }
                else
                {
                    current += ch;
                }
            }
            if (current.Length > 0) tokens.Add(current);
            return tokens;
        }

        private double EvaluateTokens(List<string> tokens)
        {
            // Simplified shunting-yard: * / have higher precedence than + -.
            var values = new Stack<double>();
            var ops = new Stack<char>();

            double Apply(double a, double b, char op)
            {
                switch (op)
                {
                    case '+': return a + b;
                    case '-': return a - b;
                    case '*': return a * b;
                    case '/':
                        if (b == 0) throw new DivideByZeroException("Division by zero");
                        return a / b;
                    default: throw new InvalidOperationException("Unknown operator: " + op);
                }
            }

            int Precedence(char op) => (op == '*' || op == '/') ? 2 : 1;

            foreach (var token in tokens)
            {
                if (token.Length == 1 && "+-*/".IndexOf(token[0]) >= 0)
                {
                    var op = token[0];
                    while (ops.Count > 0 && Precedence(ops.Peek()) >= Precedence(op))
                    {
                        var b = values.Pop(); var a = values.Pop();
                        values.Push(Apply(a, b, ops.Pop()));
                    }
                    ops.Push(op);
                }
                else
                {
                    values.Push(ResolveOperand(token));
                }
            }

            while (ops.Count > 0)
            {
                var b = values.Pop(); var a = values.Pop();
                values.Push(Apply(a, b, ops.Pop()));
            }

            if (values.Count != 1)
            {
                throw new InvalidOperationException("Invalid expression");
            }
            return values.Pop();
        }

        private double ResolveOperand(string token)
        {
            if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                return num;
            }

            if (CellRegex.IsMatch(token))
            {
                return ResolveCell(token);
            }

            throw new InvalidOperationException($"Cannot parse: {token}");
        }
    }
}
