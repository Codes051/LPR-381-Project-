using Solve.Models;

namespace Solve.Parsing;

// ============================================================================
//  OWNER: Person A
//  Design reference: LP_Parser.pdf, section 4
// ============================================================================

/// <summary>
/// Takes a <see cref="ParsedLP"/> and builds the <see cref="CanonicalMatrix"/> that every
/// solver consumes, adding the slack, surplus and artificial variables the matrix form needs.
/// </summary>
/// <remarks>
/// Indexing conventions the whole group depends on:
/// <list type="bullet">
/// <item>Grid row 0 is the objective; grid row i+1 is constraint i.</item>
/// <item>The final grid column is the right-hand-side.</item>
/// <item><c>ColumnLabels</c>, <c>IsIntegerMask</c> and <c>IsBinaryMask</c> are all indexed by
/// grid column and all have length <c>ColumnCount</c>, so index j always means the same
/// thing across every one of them.</item>
/// <item><c>BasicVariables</c> is the exception: it has one entry per constraint, so
/// <c>BasicVariables[i]</c> is the basic column of grid row <c>i + 1</c>.</item>
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

        var totalColumns = AssignAddedVariables(model, variableCount);

        // Cannot size the grid until every constraint has been walked, because a >= row
        // contributes two columns and a <= row only one.
        var grid = new double[constraintCount + 1, totalColumns + 1];
        var rhsColumn = totalColumns;

        WriteObjectiveRow(model, grid);

        var basicVariables = new int[constraintCount];
        WriteConstraintRows(model, grid, basicVariables, rhsColumn);

        var labels = BuildColumnLabels(model, variableCount, totalColumns);
        bool[] isInteger;
        bool[] isBinary;
        BuildMasks(model, variableCount, totalColumns, out isInteger, out isBinary);

        return new CanonicalMatrix(grid, basicVariables, labels, isInteger, isBinary)
        {
            OriginalObjectiveType = model.ObjectiveType
        };
    }

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
    private static int AssignAddedVariables(ParsedLP model, int variableCount)
    {
        var nextColumn = variableCount;

        foreach (var constraint in model.Constraints)
        {
            switch (constraint.RelationalOperator)
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
    /// Writes row 0. Coefficients are negated for standard form, and negated a second time
    /// for a Min problem so the model is normalised to a maximisation internally.
    /// </summary>
    /// <remarks>
    /// <c>OriginalObjectiveType</c> records which way round the problem started, because the
    /// caller has to flip the final objective value back before reporting it to the user.
    /// </remarks>
    private static void WriteObjectiveRow(ParsedLP model, double[,] grid)
    {
        var minimising = model.ObjectiveType == ProblemType.Min;

        for (var j = 0; j < model.ObjectiveCoefficients.Count; j++)
        {
            var value = -model.ObjectiveCoefficients[j];
            if (minimising)
                value = -value;

            grid[0, j] = value;
        }
    }

    private static void WriteConstraintRows(ParsedLP model, double[,] grid, int[] basicVariables, int rhsColumn)
    {
        for (var i = 0; i < model.Constraints.Count; i++)
        {
            var constraint = model.Constraints[i];
            var row = i + 1;

            for (var j = 0; j < constraint.Coefficients.Count; j++)
                grid[row, j] = constraint.Coefficients[j];

            foreach (var added in constraint.GeneratedVariables)
                grid[row, added.ColumnIndex] = added.Coefficient;

            grid[row, rhsColumn] = constraint.RHS;
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

    /// <summary>
    /// Builds a readable name for every column: x1..xn for the decision variables, then
    /// s / e / a plus the constraint number for slack, excess (surplus) and artificial.
    /// </summary>
    private static List<string> BuildColumnLabels(ParsedLP model, int variableCount, int totalColumns)
    {
        var labels = new string[totalColumns + 1];

        for (var j = 0; j < variableCount; j++)
            labels[j] = "x" + (j + 1);

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
    /// and the right-hand-side column are always false - only decision variables can be
    /// restricted. The flags are set here but not acted on; branch and bound and cutting
    /// plane read them later.
    /// </summary>
    private static void BuildMasks(
        ParsedLP model, int variableCount, int totalColumns, out bool[] isInteger, out bool[] isBinary)
    {
        isInteger = new bool[totalColumns + 1];
        isBinary = new bool[totalColumns + 1];

        for (var j = 0; j < variableCount; j++)
        {
            var restriction = model.Restrictions[j];
            isInteger[j] = restriction == SignRestriction.Integer || restriction == SignRestriction.Binary;
            isBinary[j] = restriction == SignRestriction.Binary;
        }
    }
}
