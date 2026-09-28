using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameNotes.Formula;

namespace GameNotes.UI
{
    /// <summary>
    /// "Mini-Excel" style table. Each cell stores its raw content (plain
    /// text or a formula starting with "="). Losing focus recalculates and
    /// shows the result; gaining focus shows the formula again for editing.
    ///
    /// Formulas support Excel-style "click to reference": once a cell's text
    /// starts with "=", clicking any other cell inserts that cell's
    /// reference into the formula instead of moving focus there. Press
    /// Enter to confirm the formula, or Escape to cancel and revert.
    /// </summary>
    public partial class TableGridControl : UserControl
    {
        // Raw content of each cell: raw[row][col].
        private List<List<string>> _raw = new List<List<string>>();
        private readonly FormulaEngine _engine = new FormulaEngine();
        private TextBox[,] _cells;

        // The cell currently being edited as a formula (its raw text starts
        // with "="), if any. While this is set, clicking other cells inserts
        // their reference instead of moving focus.
        private TextBox _activeFormulaBox;

        public event EventHandler ContentChanged;

        public TableGridControl()
        {
            InitializeComponent();
            _engine.CellValueResolver = ResolveCellNumeric;
        }

        /// <summary>Creates a new empty rows x cols table.</summary>
        public void InitializeNew(int rows = 3, int cols = 3)
        {
            _raw = Enumerable.Range(0, rows)
                .Select(_ => Enumerable.Range(0, cols).Select(__ => "").ToList())
                .ToList();
            Rebuild();
        }

        /// <summary>Loads an existing table from the persisted model.</summary>
        public void LoadFrom(List<List<string>> rawCells)
        {
            _raw = rawCells.Select(r => new List<string>(r)).ToList();
            Rebuild();
        }

        public List<List<string>> ExportRaw() => _raw.Select(r => new List<string>(r)).ToList();

        private void Rebuild()
        {
            CellsGrid.Children.Clear();
            CellsGrid.RowDefinitions.Clear();
            CellsGrid.ColumnDefinitions.Clear();

            int rows = _raw.Count;
            int cols = rows > 0 ? _raw[0].Count : 0;
            _cells = new TextBox[rows, cols];

            // Fixed row height (not Auto): an Auto row resizes to whatever
            // the focused cell's border/padding happens to render at, which
            // made the whole table visibly "jump" while editing.
            for (int r = 0; r < rows; r++) CellsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            for (int c = 0; c < cols; c++) CellsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var box = new TextBox
                    {
                        Margin = new Thickness(1),
                        Padding = new Thickness(4),
                        MinWidth = 70,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Tag = (r, c),
                    };
                    box.GotFocus += Cell_GotFocus;
                    box.LostFocus += Cell_LostFocus;
                    box.PreviewMouseLeftButtonDown += Cell_PreviewMouseLeftButtonDown;
                    box.PreviewKeyDown += Cell_PreviewKeyDown;
                    box.TextChanged += (s, e) => ContentChanged?.Invoke(this, EventArgs.Empty);

                    Grid.SetRow(box, r);
                    Grid.SetColumn(box, c);
                    CellsGrid.Children.Add(box);
                    _cells[r, c] = box;

                    box.Text = FormatDisplay(_raw[r][c]);
                }
            }
        }

        private void Cell_GotFocus(object sender, RoutedEventArgs e)
        {
            var box = (TextBox)sender;
            var (r, c) = ((int, int))box.Tag;
            // Show the raw content (the formula, if any) while editing.
            box.Text = _raw[r][c];
            box.CaretIndex = box.Text.Length;
        }

        private void Cell_LostFocus(object sender, RoutedEventArgs e)
        {
            var box = (TextBox)sender;
            var (r, c) = ((int, int))box.Tag;
            _raw[r][c] = box.Text ?? "";
            if (_activeFormulaBox == box) _activeFormulaBox = null;
            RecalculateAll();
        }

        /// <summary>
        /// Excel-style click-to-reference: while a formula is being typed in
        /// one cell (its text starts with "="), clicking a different cell
        /// inserts that cell's reference into the formula instead of moving
        /// focus away from it.
        /// </summary>
        private void Cell_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var clicked = (TextBox)sender;

            var editingBox = _activeFormulaBox ?? (Keyboard.FocusedElement as TextBox);
            if (editingBox == null || editingBox == clicked || !editingBox.Text.TrimStart().StartsWith("="))
            {
                _activeFormulaBox = null;
                return;
            }

            _activeFormulaBox = editingBox;

            var (r, c) = ((int, int))clicked.Tag;
            var reference = IndexToColumnLetter(c) + (r + 1);

            InsertAtCaret(editingBox, reference);

            e.Handled = true; // don't move focus to the clicked cell
            Dispatcher.BeginInvoke(new Action(() => editingBox.Focus()));
        }

        private static void InsertAtCaret(TextBox box, string text)
        {
            int caret = box.CaretIndex;
            box.Text = box.Text.Insert(caret, text);
            box.CaretIndex = caret + text.Length;
        }

        private static string IndexToColumnLetter(int index)
        {
            index += 1; // to 1-based for the classic A,B,C... conversion
            var result = "";
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                result = (char)('A' + rem) + result;
                index = (index - 1) / 26;
            }
            return result;
        }

        private void Cell_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = (TextBox)sender;

            if (e.Key == Key.Enter)
            {
                // Confirm: move focus away normally, which commits via Cell_LostFocus.
                _activeFormulaBox = null;
                box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Cancel: revert to the last committed value and step away.
                var (r, c) = ((int, int))box.Tag;
                box.Text = _raw[r][c];
                _activeFormulaBox = null;
                box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private string FormatDisplay(string rawCell)
        {
            if (string.IsNullOrEmpty(rawCell)) return "";
            if (!rawCell.TrimStart().StartsWith("=")) return rawCell;

            var result = _engine.Evaluate(rawCell);
            return result.IsError ? "#ERROR" : result.Value.ToString("G10");
        }

        /// <summary>When a cell changes, every cell that depends on it is recalculated.</summary>
        private void RecalculateAll()
        {
            for (int r = 0; r < _raw.Count; r++)
            {
                for (int c = 0; c < _raw[r].Count; c++)
                {
                    if (_cells[r, c].IsFocused) continue; // don't overwrite what the user is currently typing
                    var display = FormatDisplay(_raw[r][c]);
                    _cells[r, c].Text = display;
                    _cells[r, c].Foreground = display == "#ERROR"
                        ? Brushes.OrangeRed
                        : Brushes.Black;
                }
            }
        }

        /// <summary>
        /// Resolves the numeric value of a cell for the formula engine.
        /// Includes a small recursion guard so circular references never
        /// hang the addon.
        /// </summary>
        private int _resolveDepth = 0;
        private double? ResolveCellNumeric(string cellRef)
        {
            if (_resolveDepth > 50) return null; // breaks circular references

            var match = System.Text.RegularExpressions.Regex.Match(cellRef, @"^([A-Za-z]+)(\d+)$");
            if (!match.Success) return null;

            int col = ColumnLetterToIndex(match.Groups[1].Value);
            int row = int.Parse(match.Groups[2].Value) - 1; // examples use B2 = row 2, 1-indexed

            if (row < 0 || row >= _raw.Count || col < 0 || col >= _raw[row].Count) return null;

            var cellRaw = _raw[row][col];
            if (string.IsNullOrWhiteSpace(cellRaw)) return 0;

            if (cellRaw.TrimStart().StartsWith("="))
            {
                _resolveDepth++;
                try
                {
                    var result = _engine.Evaluate(cellRaw);
                    return result.IsError ? (double?)null : result.Value;
                }
                finally
                {
                    _resolveDepth--;
                }
            }

            return double.TryParse(cellRaw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num) ? num : (double?)0;
        }

        private static int ColumnLetterToIndex(string col)
        {
            int index = 0;
            foreach (var ch in col.ToUpperInvariant()) index = index * 26 + (ch - 'A' + 1);
            return index - 1; // 0-indexed for our internal list
        }

        // ---------- Structure editing ----------

        private void OnAddRowClick(object sender, RoutedEventArgs e)
        {
            int cols = _raw.Count > 0 ? _raw[0].Count : 3;
            _raw.Add(Enumerable.Range(0, cols).Select(_ => "").ToList());
            Rebuild();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnRemoveRowClick(object sender, RoutedEventArgs e)
        {
            if (_raw.Count > 1) _raw.RemoveAt(_raw.Count - 1);
            Rebuild();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnAddColumnClick(object sender, RoutedEventArgs e)
        {
            foreach (var row in _raw) row.Add("");
            Rebuild();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnRemoveColumnClick(object sender, RoutedEventArgs e)
        {
            if (_raw.Count > 0 && _raw[0].Count > 1)
            {
                foreach (var row in _raw) row.RemoveAt(row.Count - 1);
            }
            Rebuild();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
