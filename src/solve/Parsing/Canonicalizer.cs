using Solve.Models;

namespace Solve.Parsing;

// ============================================================================
//  OWNER: Person A
//  Design reference: LP_Parser.pdf, section 4
// ============================================================================

/// <summary>
/// Takes a <see cref="ParsedLP"/> and builds the <see cref="CanonicalMatrix"/> that every
/// solver consumes, adding the slack, surplus and artificial variables the matrix form needs
/// and substituting away any variable that is not restricted to be non-negative.
/// </summary>
/// <remarks>
/// Indexing conventions the whole group depends on:
/// <list type="bullet">
/// <item>Grid row 0 is the objective; grid row i+1 is constraint i.</item>
/// <item>The final grid column is the right-hand-side.</item>
/// <item><c>ColumnLabels</c>, <c>IsIntegerMask</c>, <c>IsBinaryMask</c> and <c>ColumnTypes</c>
/// are all indexed by grid column and all have length <c>ColumnCount</c>, so index j always
/// means the same thing across every one of them.</item>
/// <item><c>BasicVariables</c> is the exception: it has one entry per constraint, so
/// <c>BasicVariables[i]</c> is the basic column of grid row <c>i + 1</c>.</item>
/// <item>Grid columns are <b>not</b> a one-to-one match for the variables in the input file.
/// Read <c>VariableMap</c> rather than assuming column j is variable j.</item>
/// </list>
/// </remarks>
public class Canonicalizer
{
    public CanonicalMatrix ToCanonicalForm(ParsedLP model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var variableCount = model.DecisionVariableCount;
        var constraintCount = model.Constraints.Count;

        var map = BuildVariableMap(model, variableCount);
        var variableColumns = CountVariableColumns(map, variableCount);

        var totalColumns = AssignAddedVariables(model, variableColumns);

        // Cannot size the grid until the substitutions and the added variables have both been
        // worked out, because a >= row contributes two columns and so does an urs variable.
        var grid = new double[constraintCount + 1, totalColumns + 1];
        var rhsColumn = totalColumns;

        WriteObjectiveRow(model, map, grid);

        var basicVariables = new int[constraintCount];
        WriteConstraintRows(model, map, grid, basicVariables, rhsColumn);

        var labels = BuildColumnLabels(model, map, variableColumns, totalColumns);
        bool[] isInteger;
        bool[] isBinary;
        BuildMasks(model, map, totalColumns, out isInteger, out isBinary);

        return new CanonicalMatrix(grid, basicVariables, labels, isInteger, isBinary)
        {
            OriginalObjectiveType = model.ObjectiveType,
            ColumnTypes = BuildColumnTypes(model, variableColumns, totalColumns),
            VariableMap = map
        };
    }

    // ------------------------------------------------------- sign restrictions

    /// <summary>
    /// Decides which column or columns stand in for each declared variable.
    /// </summary>
    /// <remarks>
    /// The simplex can only handle non-negative variables, so the two restrictions that break
    /// that have to be substituted away here:
    /// a <c>-</c> variable is stored as its own negation, and an <c>urs</c> variable is split
    /// into a positive and a negative part occupying two columns. Everything downstream reads
    /// the resulting map rather than assuming column j is variable j.
    /// The extra columns for split variables sit immediately after the declared ones, so the
    /// declared variables keep their natural column order.
    /// </remarks>
    private static VariableMapping[] BuildVariableMap(ParsedLP model, int variableCount)
    {
        var map = new VariableMapping[variableCount];
        var nextExtraColumn = variableCount;

        for (var j = 0; j < variableCount; j++)
        {
            var name = "x" + (j + 1);

            switch (model.Restrictions[j])
            {
                case SignRestriction.Unrestricted:
                    map[j] = new VariableMapping(name, j, nextExtraColumn++, 1.0);
                    break;

                case SignRestriction.Negative:
                    map[j] = new VariableMapping(name, j, -1, -1.0);
                    break;

                default:
                    map[j] = new VariableMapping(name, j, -1, 1.0);
                    break;
            }
        }

        return map;
    }

    private static int CountVariableColumns(VariableMapping[] map, int variableCount)
    {
        var columns = variableCount;
        foreach (var entry in map)
        {
            if (entry.NegativeColumn >= 0)
                columns++;
        }

        return columns;
    }

    /// <summary>
    /// Writes one row of user coefficients into the columns that stand in for those variables.
    /// </summary>
    /// <param name="rowSign">
    /// Applied to every coefficient before substitution: -1 on the objective row to reach
    /// standard form, and -1 on a constraint row whose right-hand side was negative.
    /// </param>
    private static void ScatterCoefficients(
        List<double> coefficients, List<SignRestriction> restrictions, VariableMapping[] map,
        double[,] grid, int row, double rowSign)
    {
        for (var j = 0; j < coefficients.Count; j++)
        {
            var value = rowSign * coefficients[j];

            // A non-positive variable is stored as its own negation, so c·x becomes -c·x.
            if (restrictions[j] == SignRestriction.Negative)
                value = -value;

            grid[row, map[j].PositiveColumn] = value;

            // For a split variable, x = x+ - x-, so the negative part carries the opposite sign.
            if (map[j].NegativeColumn >= 0)
                grid[row, map[j].NegativeColumn] = -value;
        }
    }

    // ------------------------------------------------------- added variables

    /// <summary>
    /// Decides which column each slack, surplus and artificial variable will occupy, and
    /// returns the total number of variable columns.
    /// </summary>
    /// <remarks>
    /// This pass only assigns column indices; the grid is written later. The index therefore
    /// has to be stored on the <see cref="AddedVariable"/> itself, or it is lost by the time
    /// the writing pass runs. One counter is shared across the whole problem rather than one
    /// per constraint, which guarantees no two variables collide and keeps the surplus and
    /// artificial of a >= row on consecutive columns.
    /// </remarks>
    private static int AssignAddedVariables(ParsedLP model, int firstColumn)
    {
        var nextColumn = firstColumn;

        foreach (var constraint in model.Constraints)
        {
            switch (EffectiveRelation(constraint))
            {
                case Relation.LEQ:
                    constraint.GeneratedVariables.Add(
                        new AddedVariable(VariableType.Slack, 1.0, nextColumn++));
                    break;

                case Relation.GEQ:
                    constraint.GeneratedVariables.Add(
                        new AddedVariable(VariableType.Surplus, -1.0, nextColumn++));
                    constraint.GeneratedVariables.Add(
                        new AddedVariable(VariableType.Artificial, 1.0, nextColumn++));
                    break;

                case Relation.EQ:
                    constraint.GeneratedVariables.Add(
                        new AddedVariable(VariableType.Artificial, 1.0, nextColumn++));
                    break;
            }
        }

        return nextColumn;
    }

    /// <summary>
    /// The relation to canonicalize against, which is the flip of what the user wrote when the
    /// right-hand-side is negative.
    /// </summary>
    /// <remarks>
    /// Multiplying a row through by -1 to make its right-hand-side non-negative reverses the
    /// inequality, so <c>x1 + x2 &lt;= -5</c> is canonicalized as <c>-x1 - x2 &gt;= 5</c> and
    /// therefore needs a surplus and an artificial rather than a slack. Equalities are
    /// unaffected by the sign flip.
    /// </remarks>
    private static Relation EffectiveRelation(Constraint constraint)
    {
        if (constraint.RHS >= 0)
            return constraint.RelationalOperator;

        switch (constraint.RelationalOperator)
        {
            case Relation.LEQ: return Relation.GEQ;
            case Relation.GEQ: return Relation.LEQ;
            default: return Relation.EQ;
        }
    }

    // -------------------------------------------------------------- the grid

    /// <summary>
    /// Writes row 0. Coefficients are negated for standard form, and negated a second time
    /// for a Min problem so the model is normalised to a maximisation internally.
    /// </summary>
    /// <remarks>
    /// <c>OriginalObjectiveType</c> records which way round the problem started, because the
    /// caller has to flip the final objective value back before reporting it.
    /// </remarks>
    private static void WriteObjectiveRow(ParsedLP model, VariableMapping[] map, double[,] grid)
    {
        var sign = model.ObjectiveType == ProblemType.Min ? 1.0 : -1.0;
        ScatterCoefficients(model.ObjectiveCoefficients, model.Restrictions, map, grid, 0, sign);
    }

    private static void WriteConstraintRows(
        ParsedLP model, VariableMapping[] map, double[,] grid, int[] basicVariables, int rhsColumn)
    {
        for (var i = 0; i < model.Constraints.Count; i++)
        {
            var constraint = model.Constraints[i];
            var row = i + 1;

            // A negative right-hand-side is written out multiplied through by -1, which is why
            // the relation was flipped when the added variables were assigned. Without this the
            // row starts with a negative basic value, which is an infeasible starting point that
            // the simplex has no artificial variable to repair - it would just return a wrong
            // answer rather than reporting a problem.
            var sign = constraint.RHS < 0 ? -1.0 : 1.0;

            ScatterCoefficients(constraint.Coefficients, model.Restrictions, map, grid, row, sign);

            // The added variables already carry the sign that matches the flipped relation.
            foreach (var added in constraint.GeneratedVariables)
                grid[row, added.ColumnIndex] = added.Coefficient;

            grid[row, rhsColumn] = sign * constraint.RHS;
            basicVariables[i] = ChooseBasicVariable(constraint, i);
        }
    }

    /// <summary>
    /// Picks the column that starts as basic for a constraint row.
    /// </summary>
    /// <remarks>
    /// Never the surplus. A surplus carries -1.0, so it cannot seed an identity basis - a
    /// >= row is basic on its artificial variable instead.
    /// </remarks>
    private static int ChooseBasicVariable(Constraint constraint, int constraintIndex)
    {
        foreach (var added in constraint.GeneratedVariables)
        {
            if (added.Type == VariableType.Slack || added.Type == VariableType.Artificial)
                return added.ColumnIndex;
        }

        // Unreachable for the three relations the parser accepts, but a silent wrong basis
        // would be far harder to debug than an exception naming the row.
        throw new InvalidOperationException(
            $"Constraint {constraintIndex + 1} has no slack or artificial variable to be basic on.");
    }

    // --------------------------------------------------- labels, masks, types

    /// <summary>
    /// Builds a readable name for every column: x1..xn for the declared variables, then
    /// s / e / a plus the constraint number for slack, excess (surplus) and artificial.
    /// A substituted variable says so in its label, so the tableau stays readable.
    /// </summary>
    private static List<string> BuildColumnLabels(
        ParsedLP model, VariableMapping[] map, int variableColumns, int totalColumns)
    {
        var labels = new string[totalColumns + 1];

        for (var j = 0; j < map.Length; j++)
        {
            var entry = map[j];

            if (entry.NegativeColumn >= 0)
            {
                // Split variable: x = x+ - x-.
                labels[entry.PositiveColumn] = entry.Name + "+";
                labels[entry.NegativeColumn] = entry.Name + "-";
            }
            else if (entry.Scale < 0)
            {
                // Stored as its own negation, so the column is not the variable itself.
                labels[entry.PositiveColumn] = entry.Name + "'";
            }
            else
            {
                labels[entry.PositiveColumn] = entry.Name;
            }
        }

        for (var i = 0; i < model.Constraints.Count; i++)
        {
            foreach (var added in model.Constraints[i].GeneratedVariables)
            {
                string prefix;
                switch (added.Type)
                {
                    case VariableType.Slack: prefix = "s"; break;
                    case VariableType.Surplus: prefix = "e"; break;
                    default: prefix = "a"; break;
                }

                labels[added.ColumnIndex] = prefix + (i + 1);
            }
        }

        labels[totalColumns] = "rhs";
        return new List<string>(labels);
    }

    /// <summary>
    /// Flags which columns carry an integer or binary restriction. Added-variable columns
    /// and the right-hand-side column are always false. The flags are set here but not acted
    /// on; branch and bound and cutting plane read them later.
    /// </summary>
    /// <remarks>
    /// An int or bin variable is never substituted, so its flag always lands on a single
    /// column - the input format allows one restriction per variable, so nothing can be both
    /// unrestricted and integer.
    /// </remarks>
    private static void BuildMasks(
        ParsedLP model, VariableMapping[] map, int totalColumns, out bool[] isInteger, out bool[] isBinary)
    {
        isInteger = new bool[totalColumns + 1];
        isBinary = new bool[totalColumns + 1];

        for (var j = 0; j < map.Length; j++)
        {
            var restriction = model.Restrictions[j];
            var column = map[j].PositiveColumn;

            isInteger[column] = restriction == SignRestriction.Integer || restriction == SignRestriction.Binary;
            isBinary[column] = restriction == SignRestriction.Binary;
        }
    }

    /// <summary>
    /// Records what each column actually is, so an algorithm never has to infer a variable
    /// role from its display label.
    /// </summary>
    private static VariableType[] BuildColumnTypes(ParsedLP model, int variableColumns, int totalColumns)
    {
        // Decision is the default, which is correct for every variable column including the
        // extra halves of split variables, and harmless for the trailing right-hand-side
        // column, whose entry is documented as unused.
        var types = new VariableType[totalColumns + 1];

        for (var j = 0; j < variableColumns; j++)
            types[j] = VariableType.Decision;

        foreach (var constraint in model.Constraints)
        {
            foreach (var added in constraint.GeneratedVariables)
                types[added.ColumnIndex] = added.Type;
        }

        return types;
    }
}
