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

    /// <summary>
    /// One entry per variable as the user wrote it, saying which column or columns represent
    /// it. Set by the canonicalizer. See <see cref="VariableMapping"/> for why this is not
    /// simply the identity.
    /// </summary>
    public VariableMapping[] VariableMap { get; set; }

    /// <summary>
    /// True when the objective is quadratic. The linear algorithms refuse such a model, since
    /// row 0 of the grid cannot represent a quadratic objective.
    /// </summary>
    public bool IsNonLinear { get; set; }

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

    /// <summary>
    /// Turns a full row of column values into the values of the variables the user actually
    /// declared, undoing any substitution made for a <c>-</c> or <c>urs</c> restriction.
    /// </summary>
    /// <param name="columnValues">
    /// One entry per grid column: the value of that column in the current solution, zero for
    /// anything non-basic.
    /// </param>
    public double[] RecoverOriginalValues(double[] columnValues)
    {
        if (columnValues == null)
            throw new ArgumentNullException(nameof(columnValues));

        // No map means no substitution happened, so the leading decision columns are the
        // variables. Keeps older callers and hand-built matrices working.
        if (VariableMap == null)
        {
            var plain = new double[DecisionVariableCount];
            for (var j = 0; j < plain.Length && j < columnValues.Length; j++)
                plain[j] = columnValues[j];

            return plain;
        }

        var values = new double[VariableMap.Length];

        for (var i = 0; i < VariableMap.Length; i++)
        {
            var map = VariableMap[i];
            var value = Read(columnValues, map.PositiveColumn);

            if (map.NegativeColumn >= 0)
                value -= Read(columnValues, map.NegativeColumn);

            values[i] = map.Scale * value;
        }

        return values;
    }

    private static double Read(double[] values, int index) =>
        index >= 0 && index < values.Length ? values[index] : 0.0;

    /// <summary>True if the given grid column holds an artificial variable.</summary>
    public bool IsArtificial(int column) =>
        ColumnTypes != null
        && column >= 0
        && column < ColumnTypes.Length
        && ColumnTypes[column] == VariableType.Artificial;

    /// <summary>
    /// Returns a new model with one more constraint, adding whatever slack, surplus or
    /// artificial columns that relation needs. The original is left untouched.
    /// </summary>
    /// <param name="coefficients">
    /// One entry per existing variable column, i.e. <see cref="RhsColumn"/> of them. Anything
    /// shorter is padded with zeros.
    /// </param>
    /// <remarks>
    /// Written for the cutting plane, which bolts a Gomory cut onto the model and re-solves,
    /// but it is deliberately general: Person C needs exactly this for the "add a new
    /// constraint to an optimal solution" sensitivity operation.
    /// A negative right-hand side is multiplied through by -1 and the relation flipped, for
    /// the same reason the canonicalizer does it - a row that starts basic at a negative value
    /// is infeasible with nothing to repair it.
    /// </remarks>
    public CanonicalMatrix WithExtraConstraint(double[] coefficients, Relation relation, double rhs)
    {
        if (coefficients == null)
            throw new ArgumentNullException(nameof(coefficients));

        var variableColumns = RhsColumn;
        var sign = rhs < 0 ? -1.0 : 1.0;
        var effective = relation;

        if (rhs < 0)
        {
            if (relation == Relation.LEQ) effective = Relation.GEQ;
            else if (relation == Relation.GEQ) effective = Relation.LEQ;
        }

        // Work out the extra columns this relation needs before anything is sized.
        var addedTypes = new List<VariableType>();
        var addedValues = new List<double>();

        switch (effective)
        {
            case Relation.LEQ:
                addedTypes.Add(VariableType.Slack);
                addedValues.Add(1.0);
                break;
            case Relation.GEQ:
                addedTypes.Add(VariableType.Surplus);
                addedValues.Add(-1.0);
                addedTypes.Add(VariableType.Artificial);
                addedValues.Add(1.0);
                break;
            default:
                addedTypes.Add(VariableType.Artificial);
                addedValues.Add(1.0);
                break;
        }

        var newVariableColumns = variableColumns + addedTypes.Count;
        var newRows = RowCount + 1;
        var grid = new double[newRows, newVariableColumns + 1];

        // The right-hand side moves right by however many columns were inserted, so the old
        // grid cannot simply be block-copied.
        for (var i = 0; i < RowCount; i++)
        {
            for (var j = 0; j < variableColumns; j++)
                grid[i, j] = Grid[i, j];

            grid[i, newVariableColumns] = Grid[i, RhsColumn];
        }

        var newRow = RowCount;
        for (var j = 0; j < variableColumns && j < coefficients.Length; j++)
            grid[newRow, j] = sign * coefficients[j];

        for (var k = 0; k < addedTypes.Count; k++)
            grid[newRow, variableColumns + k] = addedValues[k];

        grid[newRow, newVariableColumns] = sign * rhs;

        var labels = new List<string>();
        for (var j = 0; j < variableColumns; j++)
            labels.Add(ColumnLabels[j]);

        var constraintNumber = ConstraintCount + 1;
        foreach (var type in addedTypes)
        {
            var prefix = type == VariableType.Slack ? "s" : type == VariableType.Surplus ? "e" : "a";
            labels.Add(prefix + constraintNumber);
        }

        labels.Add("rhs");

        var basics = new int[ConstraintCount + 1];
        Array.Copy(BasicVariables, basics, ConstraintCount);

        // Never the surplus: it carries -1.0 and cannot seed an identity basis.
        for (var k = 0; k < addedTypes.Count; k++)
        {
            if (addedTypes[k] != VariableType.Surplus)
                basics[ConstraintCount] = variableColumns + k;
        }

        var intMask = new bool[newVariableColumns + 1];
        var binMask = new bool[newVariableColumns + 1];
        var types = new VariableType[newVariableColumns + 1];

        for (var j = 0; j < variableColumns; j++)
        {
            intMask[j] = IsIntegerMask[j];
            binMask[j] = IsBinaryMask[j];
            types[j] = ColumnTypes == null ? VariableType.Decision : ColumnTypes[j];
        }

        for (var k = 0; k < addedTypes.Count; k++)
            types[variableColumns + k] = addedTypes[k];

        return new CanonicalMatrix(grid, basics, labels, intMask, binMask)
        {
            OriginalObjectiveType = OriginalObjectiveType,
            IsNonLinear = IsNonLinear,
            ColumnTypes = types,
            // Existing variable columns keep their indices, so the map is still correct.
            VariableMap = VariableMap
        };
    }

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
            IsNonLinear = IsNonLinear,
            ColumnTypes = ColumnTypes == null ? null : (VariableType[])ColumnTypes.Clone(),
            VariableMap = VariableMap
        };
    }
}
