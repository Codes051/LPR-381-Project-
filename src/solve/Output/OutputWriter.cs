using System.Globalization;
using System.Text;
using Solve.Models;

namespace Solve.Output;

// ============================================================================
//  OWNER: Person A
//  Marks: Output File (2)
//  SHARED UTILITY - every workstream writes through this class. Do not print
//  tableaus anywhere else, or the output file ends up inconsistent.
// ============================================================================

/// <summary>
/// Writes the canonical form and every iteration of the chosen algorithm to the output
/// text file. All decimal values are rounded to three places, as the brief requires.
/// </summary>
/// <remarks>
/// A note for whoever writes a solver: <see cref="WriteResult"/> prints the iteration list
/// in order and nothing else, so the FIRST tableau every solver records must be the
/// canonical form. That is what puts the canonical form in the output file.
/// </remarks>
public class OutputWriter
{
    /// <summary>The rounding the brief mandates. Use this everywhere, never a raw ToString().</summary>
    public const string NumberFormat = "0.000";

    /// <summary>Minimum width of a value column, so short columns still line up readably.</summary>
    private const int MinimumColumnWidth = 8;

    private readonly StringBuilder _buffer = new StringBuilder();

    public static string Format(double value)
    {
        // Negative zero prints as "-0.000" otherwise, which reads as a real negative in a
        // tableau. Comparing to 0.0 catches -0.0 as well, and the assignment normalises it.
        if (value == 0.0)
            value = 0.0;

        // Invariant culture, not the machine culture. Our machines are set to a locale that
        // uses a comma decimal separator, which would render 4.000 as "4,000" - unreadable
        // in a tableau, and inconsistent with the input file format, which uses a point.
        return value.ToString(NumberFormat, CultureInfo.InvariantCulture);
    }

    public void WriteLine(string text = "") => _buffer.AppendLine(text);

    public void WriteHeading(string text)
    {
        _buffer.AppendLine();
        _buffer.AppendLine(text);
        _buffer.AppendLine(new string('=', text.Length));
    }

    /// <summary>Prints the canonical form: column labels, objective row, constraint rows, RHS.</summary>
    public void WriteCanonicalForm(CanonicalMatrix model)
    {
        if (model == null)
        {
            WriteLine("(no canonical form available)");
            return;
        }

        WriteHeading("Canonical Form");

        if (model.OriginalObjectiveType == ProblemType.Min)
            WriteLine("Original problem was a minimisation, normalised to a maximisation below.");

        WriteGrid(model.Grid, model.BasicVariables, model.ColumnLabels, null, -1, -1);
    }

    /// <summary>Prints one captured iteration, including its note and pivot markers.</summary>
    public void WriteTableau(Tableau tableau)
    {
        if (tableau == null)
            return;

        WriteHeading(tableau.Title);
        WriteGrid(tableau.Grid, tableau.BasicVariables, tableau.ColumnLabels, tableau.RowLabels,
                  tableau.PivotRow, tableau.PivotColumn);

        // A legend rather than a restatement: the note underneath already names the entering
        // and leaving variables, so repeating them here just makes the output file longer.
        if (tableau.PivotColumn >= 0)
            WriteLine(tableau.PivotRow >= 0 ? "* entering column, > pivot row" : "* entering column");

        if (!string.IsNullOrWhiteSpace(tableau.Note))
            WriteWrapped(tableau.Note);
    }

    /// <summary>Prints a full solve: canonical form, every iteration, then the answer.</summary>
    public void WriteResult(SolveResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        WriteHeading(result.AlgorithmName);

        foreach (var tableau in result.Iterations)
            WriteTableau(tableau);

        WriteHeading("Result");
        WriteLine($"Status: {result.Status}");

        switch (result.Status)
        {
            case SolutionStatus.Optimal:
                WriteLine($"Objective value: {Format(result.ObjectiveValue)}");
                WriteVariableValues(result);
                break;

            default:
                // Infeasible and unbounded have no solution to print, only an explanation.
                WriteWrapped(string.IsNullOrWhiteSpace(result.Message)
                    ? "The model has no optimal solution."
                    : result.Message);
                break;
        }

        if (!string.IsNullOrWhiteSpace(result.BestCandidateDescription))
        {
            WriteHeading("Best Candidate");
            WriteWrapped(result.BestCandidateDescription);
        }
    }

    /// <summary>
    /// Writes prose broken at word boundaries, so a long explanation does not run off the side
    /// of the output file next to tables that are already close to the same width.
    /// </summary>
    private void WriteWrapped(string text, int width = 90)
    {
        foreach (var paragraph in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = new StringBuilder();

            foreach (var word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > width)
                {
                    WriteLine(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');

                line.Append(word);
            }

            if (line.Length > 0)
                WriteLine(line.ToString());
        }
    }

    public void Save(string path) => File.WriteAllText(path, _buffer.ToString());

    public override string ToString() => _buffer.ToString();

    private void WriteVariableValues(SolveResult result)
    {
        if (result.VariableValues == null || result.VariableValues.Length == 0)
            return;

        var labels = result.FinalTableau != null ? result.FinalTableau.ColumnLabels : null;

        for (var j = 0; j < result.VariableValues.Length; j++)
            WriteLine($"  {LabelAt(labels, j, "x" + (j + 1))} = {Format(result.VariableValues[j])}");
    }

    /// <summary>
    /// Renders a grid as a fixed-width table. Every cell is built as a string first and the
    /// column widths measured afterwards, so decorated labels (the pivot markers) can never
    /// push a column out of alignment.
    /// </summary>
    private void WriteGrid(
        double[,] grid, int[] basicVariables, List<string> labels, List<string> rowLabels,
        int pivotRow, int pivotColumn)
    {
        if (grid == null)
        {
            WriteLine("(empty tableau)");
            return;
        }

        var rows = grid.GetLength(0);
        var columns = grid.GetLength(1);

        var cells = new string[rows + 1, columns + 1];

        cells[0, 0] = string.Empty;
        for (var j = 0; j < columns; j++)
            cells[0, j + 1] = LabelAt(labels, j) + (j == pivotColumn ? "*" : string.Empty);

        for (var i = 0; i < rows; i++)
        {
            cells[i + 1, 0] = (i == pivotRow ? ">" : " ") + RowLabel(i, basicVariables, labels, rowLabels);

            for (var j = 0; j < columns; j++)
                cells[i + 1, j + 1] = Format(grid[i, j]);
        }

        var widths = new int[columns + 1];
        for (var j = 0; j <= columns; j++)
        {
            var width = j == 0 ? 0 : MinimumColumnWidth;
            for (var i = 0; i <= rows; i++)
                width = Math.Max(width, cells[i, j].Length);

            widths[j] = width;
        }

        for (var i = 0; i <= rows; i++)
        {
            var line = new StringBuilder();
            for (var j = 0; j <= columns; j++)
            {
                // Row labels read better flush left; numbers line up on the decimal point
                // only when they are flush right.
                line.Append(j == 0
                    ? cells[i, j].PadRight(widths[j])
                    : cells[i, j].PadLeft(widths[j] + 2));
            }

            WriteLine(line.ToString().TrimEnd());
        }
    }

    /// <summary>Row 0 is always the objective; every other row is named for its basic variable.</summary>
    private static string RowLabel(int row, int[] basicVariables, List<string> labels, List<string> rowLabels)
    {
        // An explicit name always wins: a basis inverse has no row that means "the objective".
        if (rowLabels != null && row < rowLabels.Count)
            return rowLabels[row];

        if (row == 0)
            return "z";

        var constraintIndex = row - 1;
        if (basicVariables == null || constraintIndex >= basicVariables.Length)
            return "c" + row;

        return LabelAt(labels, basicVariables[constraintIndex], "c" + row);
    }

    private static string LabelAt(List<string> labels, int index, string fallback = null)
    {
        if (labels != null && index >= 0 && index < labels.Count && !string.IsNullOrEmpty(labels[index]))
            return labels[index];

        return fallback ?? ("col" + index);
    }
}
