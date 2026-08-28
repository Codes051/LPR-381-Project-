using System.Globalization;
using System.Drawing;
using System.Windows.Forms;
using Solve.Algorithms;
using Solve.Algorithms.BranchAndBound;
using Solve.Algorithms.CuttingPlane;
using Solve.Algorithms.NonLinear;
using Solve.Algorithms.Simplex;
using Solve.Exceptions;
using Solve.Models;
using Solve.Output;
using Solve.Parsing;
using Solve.Sensitivity;

namespace Solve.Gui;

// ============================================================================
//  A window over the same solvers the console uses.
//
//  Built for demonstrating the program on video: choosing a model and an
//  algorithm is a click rather than a typed menu number, variables and
//  constraints are chosen BY NAME rather than by a zero-based column index,
//  and the tableau output gets a scrollable monospace pane instead of a
//  terminal that has to be 170 columns wide.
//
//  Every result shown here is the string OutputWriter produces for the console.
//  Nothing is recomputed or reformatted, so the window cannot disagree with
//  solve.exe.
// ============================================================================

public class MainForm : Form
{
    private readonly ISolver[] _solvers =
    {
        new PrimalSimplexSolver(),
        new RevisedPrimalSimplexSolver(),
        new BranchAndBoundSimplexSolver(),
        new CuttingPlaneSolver(),
        new KnapsackBranchAndBoundSolver(),
        new NonLinearSolver()
    };

    private static readonly string[] Operations =
    {
        "1  Range of a non-basic variable",
        "2  Change a non-basic variable",
        "3  Range of a basic variable",
        "4  Change a basic variable",
        "5  Range of a constraint right-hand side",
        "6  Change a constraint right-hand side",
        "7  Range of a variable in a non-basic column",
        "8  Change a variable in a non-basic column",
        "9  Add a new activity",
        "10 Add a new constraint",
        "11 Display shadow prices",
        "12 Duality: build, solve, verify"
    };

    private ParsedLP _parsed;
    private CanonicalMatrix _canonical;
    private SolveResult _result;

    private readonly TextBox _path = new TextBox();
    private readonly Label _modelInfo = new Label();
    private readonly ListBox _algorithms = new ListBox();
    private readonly Button _solve = new Button();
    private readonly TextBox _output = new TextBox();

    private readonly ComboBox _operation = new ComboBox();
    private readonly ComboBox _variable = new ComboBox();
    private readonly ComboBox _constraint = new ComboBox();
    private readonly TextBox _value = new TextBox();
    private readonly TextBox _list = new TextBox();
    private readonly ComboBox _relation = new ComboBox();
    private readonly Label _hint = new Label();
    private readonly Button _run = new Button();

    public MainForm()
    {
        Text = "LPR381 Solver";
        Width = 1180;
        Height = 820;
        MinimumSize = new Size(900, 640);
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(BuildLayout());
        SetSensitivityEnabled(false);
        UpdateOperationInputs();
    }

    // ------------------------------------------------------------------ layout

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12)
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var model = BuildModelPanel();
        root.Controls.Add(model, 0, 0);
        root.SetColumnSpan(model, 2);

        root.Controls.Add(BuildAlgorithmPanel(), 0, 1);
        root.Controls.Add(BuildOutputPanel(), 1, 1);

        var sensitivity = BuildSensitivityPanel();
        root.Controls.Add(sensitivity, 0, 2);
        root.SetColumnSpan(sensitivity, 2);

        return root;
    }

    private Control BuildModelPanel()
    {
        var box = new GroupBox { Text = "Model", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, AutoSize = true };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _path.Dock = DockStyle.Fill;
        _path.PlaceholderText = "Choose an input file...";

        var browse = new Button { Text = "Browse...", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        browse.Click += (s, e) => Browse();

        var samples = new Button { Text = "Samples", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        samples.Click += (s, e) => ShowSamples(samples);

        var load = new Button { Text = "Load", AutoSize = true, Padding = new Padding(14, 2, 14, 2) };
        load.Click += (s, e) => LoadModel();

        row.Controls.Add(_path, 0, 0);
        row.Controls.Add(browse, 1, 0);
        row.Controls.Add(samples, 2, 0);
        row.Controls.Add(load, 3, 0);

        _modelInfo.Text = "No model loaded.";
        _modelInfo.AutoSize = true;
        _modelInfo.ForeColor = SystemColors.GrayText;
        _modelInfo.Margin = new Padding(3, 8, 3, 0);
        row.Controls.Add(_modelInfo, 0, 1);
        row.SetColumnSpan(_modelInfo, 4);

        box.Controls.Add(row);
        return box;
    }

    private Control BuildAlgorithmPanel()
    {
        var box = new GroupBox { Text = "Algorithm", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _algorithms.Dock = DockStyle.Fill;
        _algorithms.IntegralHeight = false;
        _algorithms.DrawMode = DrawMode.OwnerDrawFixed;
        _algorithms.ItemHeight = 26;
        _algorithms.DrawItem += DrawAlgorithmItem;
        _algorithms.SelectedIndexChanged += (s, e) => UpdateSolveEnabled();

        foreach (var solver in _solvers)
            _algorithms.Items.Add(solver.Name);

        _solve.Text = "Solve";
        _solve.Dock = DockStyle.Fill;
        _solve.Height = 38;
        _solve.Enabled = false;
        _solve.Click += (s, e) => SolveModel();

        stack.Controls.Add(_algorithms, 0, 0);
        stack.Controls.Add(_solve, 0, 1);
        box.Controls.Add(stack);
        return box;
    }

    /// <summary>
    /// Greys out any algorithm that reports it cannot handle the loaded model, so an
    /// unsuitable choice is visible before it is clicked rather than as an error afterwards.
    /// </summary>
    private void DrawAlgorithmItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
            return;

        var suitable = _canonical == null || _solvers[e.Index].CanSolve(_canonical);
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected && suitable;

        e.DrawBackground();

        if (selected)
            e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds);

        var colour = !suitable ? SystemColors.GrayText
                   : selected ? SystemColors.HighlightText
                   : SystemColors.ControlText;

        var text = _solvers[e.Index].Name + (suitable ? string.Empty : "   (not suitable)");

        TextRenderer.DrawText(
            e.Graphics, text, e.Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 4, e.Bounds.Width - 8, e.Bounds.Height),
            colour, TextFormatFlags.Left);
    }

    private Control BuildOutputPanel()
    {
        var box = new GroupBox { Text = "Output", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Monospace and no wrapping: the tableaus are column-aligned and lose all meaning if
        // the lines reflow. Horizontal scrolling is the correct behaviour here.
        _output.Multiline = true;
        _output.ReadOnly = true;
        _output.WordWrap = false;
        _output.ScrollBars = ScrollBars.Both;
        _output.Dock = DockStyle.Fill;
        _output.Font = new Font("Consolas", 9.5f);
        _output.BackColor = Color.White;

        var save = new Button { Text = "Save output file...", AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
        save.Click += (s, e) => SaveOutput();

        var copy = new Button { Text = "Copy", AutoSize = true, Padding = new Padding(10, 3, 10, 3) };
        copy.Click += (s, e) =>
        {
            if (_output.TextLength > 0)
                Clipboard.SetText(_output.Text);
        };

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(save);
        buttons.Controls.Add(copy);

        stack.Controls.Add(_output, 0, 0);
        stack.SetColumnSpan(_output, 2);
        stack.Controls.Add(buttons, 0, 1);

        box.Controls.Add(stack);
        return box;
    }

    private Control BuildSensitivityPanel()
    {
        var box = new GroupBox { Text = "Sensitivity analysis", Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10) };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        _operation.DropDownStyle = ComboBoxStyle.DropDownList;
        _operation.Width = 300;
        _operation.Items.AddRange(Operations);
        _operation.SelectedIndex = 0;
        _operation.SelectedIndexChanged += (s, e) => UpdateOperationInputs();

        // Variables and constraints are chosen by name. The console asks for a zero-based
        // grid column, which is the single easiest thing to get wrong while being filmed.
        _variable.DropDownStyle = ComboBoxStyle.DropDownList;
        _variable.Width = 150;

        _constraint.DropDownStyle = ComboBoxStyle.DropDownList;
        _constraint.Width = 150;

        _value.Width = 110;
        _value.PlaceholderText = "value";

        _list.Width = 220;
        _list.PlaceholderText = "comma separated";

        _relation.DropDownStyle = ComboBoxStyle.DropDownList;
        _relation.Width = 70;
        _relation.Items.AddRange(new object[] { "<=", ">=", "=" });
        _relation.SelectedIndex = 0;

        _run.Text = "Run";
        _run.AutoSize = true;
        _run.Padding = new Padding(18, 3, 18, 3);
        _run.Click += (s, e) => RunOperation();

        _hint.AutoSize = true;
        _hint.ForeColor = SystemColors.GrayText;
        _hint.Margin = new Padding(3, 9, 3, 0);

        flow.Controls.Add(_operation);
        flow.Controls.Add(_variable);
        flow.Controls.Add(_constraint);
        flow.Controls.Add(_value);
        flow.Controls.Add(_list);
        flow.Controls.Add(_relation);
        flow.Controls.Add(_run);
        flow.Controls.Add(_hint);

        box.Controls.Add(flow);
        return box;
    }

    // ------------------------------------------------------------------ actions

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "Model files (*.txt)|*.txt|All files (*.*)|*.*" };

        var samples = SamplesFolder();
        if (samples != null)
            dialog.InitialDirectory = samples;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _path.Text = dialog.FileName;
            LoadModel();
        }
    }

    private void ShowSamples(Control anchor)
    {
        var folder = SamplesFolder();

        if (folder == null)
        {
            Report("No samples folder was found next to the application.");
            return;
        }

        var menu = new ContextMenuStrip();

        foreach (var file in Directory.GetFiles(folder, "*.txt").OrderBy(f => f))
        {
            var name = Path.GetFileName(file);
            var captured = file;
            menu.Items.Add(name, null, (s, e) => { _path.Text = captured; LoadModel(); });
        }

        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private static string SamplesFolder()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "samples");
        if (Directory.Exists(beside))
            return beside;

        // Running from the IDE, the repo copy is a few levels up.
        var walk = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && walk != null; i++)
        {
            var candidate = Path.Combine(walk.FullName, "samples");
            if (Directory.Exists(candidate))
                return candidate;

            walk = walk.Parent;
        }

        return null;
    }

    private void LoadModel()
    {
        Guarded(() =>
        {
            _parsed = new LpParser().ParseFile(_path.Text.Trim().Trim('"'));
            _canonical = new Canonicalizer().ToCanonicalForm(_parsed);
            _result = null;

            _modelInfo.Text =
                $"{_parsed.DecisionVariableCount} variables, {_parsed.Constraints.Count} constraints" +
                $"  ·  {(_parsed.ObjectiveType == ProblemType.Max ? "maximise" : "minimise")}" +
                (_parsed.IsNonLinear ? "  ·  quadratic objective" : string.Empty);

            PopulateChoosers();
            SetSensitivityEnabled(false);

            var writer = new OutputWriter();
            writer.WriteCanonicalForm(_canonical);
            _output.Text = writer.ToString().Replace("\n", Environment.NewLine);

            _algorithms.Invalidate();
            SelectFirstSuitableAlgorithm();
            UpdateSolveEnabled();
        });
    }

    private void SelectFirstSuitableAlgorithm()
    {
        for (var i = 0; i < _solvers.Length; i++)
        {
            if (_solvers[i].CanSolve(_canonical))
            {
                _algorithms.SelectedIndex = i;
                return;
            }
        }

        _algorithms.ClearSelected();
    }

    private void SolveModel()
    {
        Guarded(() =>
        {
            if (_canonical == null)
                throw new LpException("Load a model first.");

            var solver = _solvers[_algorithms.SelectedIndex];

            if (!solver.CanSolve(_canonical))
                throw new LpException($"{solver.Name} cannot solve this model.");

            _result = solver.Solve(_canonical);

            // Exactly what the console writes to the output file, shown in the pane.
            var writer = new OutputWriter();
            writer.WriteResult(_result);
            _output.Text = writer.ToString().Replace("\n", Environment.NewLine);
            _output.Select(0, 0);

            SetSensitivityEnabled(_result.Status == SolutionStatus.Optimal);
        });
    }

    private void SaveOutput()
    {
        if (_output.TextLength == 0)
        {
            Report("There is nothing to save yet. Solve a model first.");
            return;
        }

        using var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "output.txt" };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Guarded(() =>
            {
                File.WriteAllText(dialog.FileName, _output.Text);
                Report($"Written to {dialog.FileName}");
            });
        }
    }

    // ------------------------------------------------------------- sensitivity

    private void PopulateChoosers()
    {
        _variable.Items.Clear();
        _constraint.Items.Clear();

        if (_canonical?.VariableMap != null)
        {
            foreach (var entry in _canonical.VariableMap)
                _variable.Items.Add(entry.Name);
        }

        if (_parsed != null)
        {
            for (var i = 0; i < _parsed.Constraints.Count; i++)
                _constraint.Items.Add("Constraint " + (i + 1));
        }

        if (_variable.Items.Count > 0) _variable.SelectedIndex = 0;
        if (_constraint.Items.Count > 0) _constraint.SelectedIndex = 0;
    }

    private void SetSensitivityEnabled(bool enabled)
    {
        _operation.Enabled = enabled;
        _run.Enabled = enabled;
        UpdateOperationInputs();
    }

    private void UpdateOperationInputs()
    {
        var op = _operation.SelectedIndex + 1;
        var live = _run.Enabled;

        var needsVariable = op is 1 or 2 or 3 or 4 or 7 or 8;
        var needsConstraint = op is 5 or 6 or 7 or 8;
        var needsValue = op is 2 or 4 or 6 or 8 or 9 or 10;
        var needsList = op is 9 or 10;
        var needsRelation = op == 10;

        _variable.Enabled = live && needsVariable;
        _constraint.Enabled = live && needsConstraint;
        _value.Enabled = live && needsValue;
        _list.Enabled = live && needsList;
        _relation.Enabled = live && needsRelation;

        _hint.Text = op switch
        {
            2 or 4 => "value = the new objective coefficient",
            6 => "value = the new right-hand side",
            8 => "value = the new technological coefficient",
            9 => "value = objective coefficient, list = one coefficient per constraint",
            10 => "list = one coefficient per variable, value = right-hand side",
            _ => string.Empty
        };
    }

    private void RunOperation()
    {
        Guarded(() =>
        {
            if (_result == null || _result.Status != SolutionStatus.Optimal)
                throw new LpException("Solve a model to optimality first.");

            var analyzer = new SensitivityAnalyzer(_result, _parsed);
            var op = _operation.SelectedIndex + 1;
            var column = SelectedVariableColumn();
            var constraintRow = _constraint.SelectedIndex;

            switch (op)
            {
                case 1: ShowRange(analyzer.RangeOfNonBasicVariable(column)); break;
                case 2: ShowResult(analyzer.ChangeNonBasicVariable(column, Number(_value, "value"))); break;
                case 3: ShowRange(analyzer.RangeOfBasicVariable(column)); break;
                case 4: ShowResult(analyzer.ChangeBasicVariable(column, Number(_value, "value"))); break;
                case 5: ShowRange(analyzer.RangeOfRhs(constraintRow)); break;
                case 6: ShowResult(analyzer.ChangeRhs(constraintRow, Number(_value, "value"))); break;
                case 7: ShowRange(analyzer.RangeOfCoefficientInNonBasicColumn(column, constraintRow)); break;
                case 8: ShowResult(analyzer.ChangeCoefficientInNonBasicColumn(column, constraintRow, Number(_value, "value"))); break;
                case 9: ShowResult(analyzer.AddActivity(Number(_value, "objective coefficient"), Numbers(_list, _parsed.Constraints.Count, "constraint"))); break;
                case 10: ShowResult(analyzer.AddConstraint(Numbers(_list, _parsed.DecisionVariableCount, "variable"), SelectedRelation(), Number(_value, "right-hand side"))); break;
                case 11: ShowShadowPrices(analyzer); break;
                case 12: ShowDuality(); break;
            }
        });
    }

    private int SelectedVariableColumn()
    {
        if (_canonical?.VariableMap == null || _variable.SelectedIndex < 0)
            return 0;

        return _canonical.VariableMap[_variable.SelectedIndex].PositiveColumn;
    }

    private Relation SelectedRelation() => _relation.SelectedIndex switch
    {
        1 => Relation.GEQ,
        2 => Relation.EQ,
        _ => Relation.LEQ
    };

    /// <summary>
    /// Reads a number, accepting either decimal separator.
    /// </summary>
    /// <remarks>
    /// Everything this program displays uses a point, because the tableaus and the input files
    /// do. Parsing with the machine culture alone would reject "2.5" on a machine set to a
    /// comma decimal separator - so the user reads 2.500 on screen, types it back, and is told
    /// it is not a number. Invariant is tried first, then the local culture, so both work.
    /// </remarks>
    private static bool TryReadNumber(string text, out double value)
    {
        text = (text ?? string.Empty).Trim();

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static double Number(TextBox box, string what)
    {
        if (!TryReadNumber(box.Text, out var value))
            throw new LpException($"Enter a number for the {what}.");

        return value;
    }

    private static double[] Numbers(TextBox box, int expected, string per)
    {
        // Split on commas and spaces only. A comma is also a decimal separator in some
        // locales, but a list of coefficients is written with points here, matching the
        // input file format shown everywhere else.
        var parts = box.Text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var values = new double[expected];

        if (parts.Length != expected)
            throw new LpException($"Enter {expected} numbers separated by commas, one per {per}.");

        for (var i = 0; i < expected; i++)
        {
            if (!TryReadNumber(parts[i], out values[i]))
                throw new LpException($"\"{parts[i].Trim()}\" is not a number.");
        }

        return values;
    }

    private void ShowRange(SensitivityRange range)
    {
        var writer = new OutputWriter();
        writer.WriteHeading(range.Subject);
        writer.WriteLine($"  Current value   {OutputWriter.Format(range.Current)}");
        writer.WriteLine($"  Lower bound     {Bound(range.Lower)}");
        writer.WriteLine($"  Upper bound     {Bound(range.Upper)}");
        Append(writer);
    }

    private static string Bound(double value) =>
        double.IsPositiveInfinity(value) ? "+infinity"
        : double.IsNegativeInfinity(value) ? "-infinity"
        : OutputWriter.Format(value);

    private void ShowResult(SolveResult result)
    {
        var writer = new OutputWriter();
        writer.WriteResult(result);
        Append(writer);
    }

    private void ShowShadowPrices(SensitivityAnalyzer analyzer)
    {
        var prices = analyzer.ShadowPrices();
        var writer = new OutputWriter();
        writer.WriteHeading("Shadow Prices");

        for (var i = 0; i < prices.Length; i++)
            writer.WriteLine($"  Constraint {i + 1}   {OutputWriter.Format(prices[i])}");

        Append(writer);
    }

    private void ShowDuality()
    {
        var duality = new DualityAnalyzer();
        var dual = duality.SolveDual(_parsed);

        var writer = new OutputWriter();
        writer.WriteHeading("Duality");
        writer.WriteLine($"  Primal objective   {OutputWriter.Format(_result.ObjectiveValue)}");
        writer.WriteLine($"  Dual objective     {OutputWriter.Format(dual.ObjectiveValue)}");
        writer.WriteLine();
        writer.WriteLine(duality.VerifyDuality(_result, dual));
        Append(writer);
    }

    /// <summary>
    /// Adds to the pane rather than replacing it, so a run of sensitivity operations reads as
    /// one transcript on video instead of each one wiping the last.
    /// </summary>
    private void Append(OutputWriter writer)
    {
        var text = writer.ToString().Replace("\n", Environment.NewLine);
        _output.AppendText(text);
        _output.SelectionStart = Math.Max(0, _output.TextLength - text.Length);
        _output.ScrollToCaret();
    }

    // ------------------------------------------------------------------ plumbing

    private void UpdateSolveEnabled() =>
        _solve.Enabled = _canonical != null
                         && _algorithms.SelectedIndex >= 0
                         && _solvers[_algorithms.SelectedIndex].CanSolve(_canonical);

    /// <summary>
    /// Runs an action and turns any failure into a message box. Same contract as the console
    /// menu: an expected failure is a readable sentence, never a stack trace.
    /// </summary>
    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (LpException ex)
        {
            Report(ex.Message);
        }
        catch (Exception ex)
        {
            Report(ex.Message, "Unexpected error");
        }
    }

    /// <summary>
    /// Where user-facing messages go. Left null in normal use, when they become message boxes.
    /// </summary>
    /// <remarks>
    /// A test can set this to collect messages instead, which is the only way to drive the
    /// window unattended - a modal box has no one to dismiss it and would hang the run.
    /// </remarks>
    internal Action<string> OnReport { get; set; }

    private void Report(string message, string title = "LPR381 Solver")
    {
        if (OnReport != null)
        {
            OnReport(message);
            return;
        }

        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
