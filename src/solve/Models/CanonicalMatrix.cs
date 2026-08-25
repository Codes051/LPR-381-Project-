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

    /// <summary>
    /// What role each column plays, indexed by grid column, same length as the other masks.
    /// The entry for the right-hand-side column is meaningless and should not be read.
    /// </summary>
    /// <remarks>
    /// Set by the canonicalizer. Algorithms need this: a two-phase simplex has to know which
    /// columns are artificial so it can bar them from re-entering the basis in phase two, and
    /// reporting a solution means knowing which columns are decision variables. The column
    /// labels encode the same information in their prefix, but reading algorithm behaviour out
    /// of a display string would break silently the first time a label is reworded.
    /// </remarks>
    public VariableType[] ColumnTypes { get; set; }

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

    /// <summary>How many columns are original decision variables, i.e. x1..xn.</summary>
    /// <remarks>
    /// Decision variables always occupy the leading columns, so this doubles as the exclusive
    /// upper bound of their column range. The right-hand-side column is deliberately excluded
    /// from the count: <see cref="VariableType.Decision"/> is the zero value of the enum, so
    /// the unset entry for that column would otherwise be counted as a decision variable.
    /// </remarks>
    public int DecisionVariableCount
    {
        get
        {
            if (ColumnTypes == null)
                return 0;

            var count = 0;
            for (var j = 0; j < RhsColumn && j < ColumnTypes.Length; j++)
            {
                if (ColumnTypes[j] == VariableType.Decision)
                    count++;
            }

            return count;
        }
    }

    /// <summary>True if the given grid column holds an artificial variable.</summary>
    public bool IsArtificial(int column) =>
        ColumnTypes != null
        && column >= 0
        && column < ColumnTypes.Length
        && ColumnTypes[column] == VariableType.Artificial;

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
            OriginalObjectiveType = OriginalObjectiveType,
            ColumnTypes = ColumnTypes == null ? null : (VariableType[])ColumnTypes.Clone()
        };
    }
}
