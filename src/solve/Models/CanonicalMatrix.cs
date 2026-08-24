namespace Solve.Models;

/// <summary>
/// The finished grid, ready to hand to a solver.
/// Row 0 is the objective row; rows 1..M are the constraints. The last column is the RHS.
/// The problem is always stored normalised to a maximisation — a Min problem has its
/// objective row flipped twice during canonicalization, so the caller must flip the final
/// objective value back before reporting it.
/// </summary>
public class CanonicalMatrix
{
    public CanonicalMatrix(
        double[,] grid,
        int[] basicVariables,
        List<string> columnLabels,
        bool[] isIntegerMask,
        bool[] isBinaryMask)
    {
        Grid = grid;
        BasicVariables = basicVariables;
        ColumnLabels = columnLabels;
        IsIntegerMask = isIntegerMask;
        IsBinaryMask = isBinaryMask;
    }

    /// <summary>(numConstraints + 1) rows by (totalColumns + 1) columns.</summary>
    public double[,] Grid { get; }

    /// <summary>For each constraint row, which column starts as its basic variable.</summary>
    public int[] BasicVariables { get; }

    /// <summary>A readable name per column: x1, x2, s1, e2, a3 ...</summary>
    public List<string> ColumnLabels { get; }

    /// <summary>True for columns restricted to integer values (int or bin). Added columns are false.</summary>
    public bool[] IsIntegerMask { get; }

    /// <summary>True only for columns restricted to 0/1. Added columns are false.</summary>
    public bool[] IsBinaryMask { get; }

    /// <summary>Whether the source model was a Min, so callers know to flip the objective back.</summary>
    public ProblemType OriginalObjectiveType { get; set; } = ProblemType.Max;

    public int RowCount => Grid.GetLength(0);

    public int ColumnCount => Grid.GetLength(1);

    /// <summary>Number of constraint rows, excluding the objective row.</summary>
    public int ConstraintCount => RowCount - 1;

    /// <summary>Index of the right-hand-side column.</summary>
    public int RhsColumn => ColumnCount - 1;

    /// <summary>True if any column carries an integer or binary restriction.</summary>
    public bool IsIntegerProblem => IsIntegerMask.Any(f => f) || IsBinaryMask.Any(f => f);

    /// <summary>
    /// Deep copy. Branch and bound and cutting plane both need to mutate a model without
    /// disturbing the parent node, so both rely on this.
    /// </summary>
    public CanonicalMatrix Clone()
    {
        var grid = (double[,])Grid.Clone();
        var basics = (int[])BasicVariables.Clone();
        var labels = new List<string>(ColumnLabels);
        var intMask = (bool[])IsIntegerMask.Clone();
        var binMask = (bool[])IsBinaryMask.Clone();

        return new CanonicalMatrix(grid, basics, labels, intMask, binMask)
        {
            OriginalObjectiveType = OriginalObjectiveType
        };
    }
}
