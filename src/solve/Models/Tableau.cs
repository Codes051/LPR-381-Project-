namespace Solve.Models;

/// <summary>
/// A snapshot of one iteration, captured so the output file can show every step.
/// The brief requires all tableau iterations to be displayed, so every solver must
/// record one of these per pivot rather than only reporting the final answer.
/// </summary>
public class Tableau
{
    public Tableau(string title, double[,] grid, int[] basicVariables, List<string> columnLabels)
    {
        Title = title;
        Grid = grid;
        BasicVariables = basicVariables;
        ColumnLabels = columnLabels;
    }

    /// <summary>e.g. "Iteration 2", "Sub-problem 1.2 — Canonical Form", "Product Form B^-1".</summary>
    public string Title { get; }

    public double[,] Grid { get; }

    public int[] BasicVariables { get; }

    public List<string> ColumnLabels { get; }

    /// <summary>Optional note printed under the table: pivot chosen, ratio test, fathom reason.</summary>
    public string Note { get; set; }

    /// <summary>Column pivoted on for this iteration, or -1 if not applicable.</summary>
    public int PivotColumn { get; set; } = -1;

    /// <summary>Row pivoted on for this iteration, or -1 if not applicable.</summary>
    public int PivotRow { get; set; } = -1;

    /// <summary>Convenience factory that snapshots the current state of a model.</summary>
    public static Tableau Snapshot(string title, CanonicalMatrix model) =>
        new Tableau(title, (double[,])model.Grid.Clone(), (int[])model.BasicVariables.Clone(),
                    new List<string>(model.ColumnLabels));
}
