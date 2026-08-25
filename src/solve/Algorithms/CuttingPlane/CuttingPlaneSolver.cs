using Solve.Algorithms.Simplex;
using Solve.Exceptions;
using Solve.Models;
using Solve.Output;

namespace Solve.Algorithms.CuttingPlane;

// ============================================================================
//  OWNER: Person A
//  Marks: Cutting Plane Algorithm or Revised Cutting Plane Algorithm (14)
//  Criteria: display the canonical form and solve, showing ALL Product Form and
//  Price Out iterations.
//
//  Worth more than both simplex algorithms combined. It is the revised variant:
//  every relaxation is solved by the revised simplex, so each round contributes
//  its own full set of price out and product form steps to the output.
// ============================================================================

/// <summary>
/// Gomory fractional cutting plane. Solves the relaxation, and while any integer-restricted
/// variable is still fractional, derives a cut that removes the current fractional optimum
/// without removing a single integer point, bolts it onto the model, and re-solves.
/// </summary>
/// <remarks>
/// <para>
/// The cut comes from a row of the optimal tableau whose basic variable should be an integer
/// but is not. Writing that row as
/// <c>x_B(r) + sum ā_rj x_j = b̄_r</c> and splitting every coefficient into an integer part
/// and a fraction gives <c>sum frac(ā_rj) x_j &gt;= frac(b̄_r)</c>, which every integer point
/// satisfies and the current fractional optimum does not.
/// </para>
/// <para>
/// Two things about this implementation are worth knowing.
/// </para>
/// <para>
/// First, binary variables. A <c>bin</c> restriction sets a mask and nothing else - no
/// <c>x &lt;= 1</c> row is ever written into the canonical form - so the relaxation of a
/// binary model is not bounded by 1 and the cuts would chase a fractional optimum that is
/// not even in the feasible region. Those bounds are added here before the first solve.
/// </para>
/// <para>
/// Second, these are pure integer cuts. They are valid when every variable in the model takes
/// integer values, which holds for the models this project targets. On a genuinely mixed
/// model - some variables integer, some continuous - a pure Gomory cut can remove feasible
/// points, so that case is detected and reported rather than answered wrongly.
/// </para>
/// </remarks>
public class CuttingPlaneSolver : ISolver
{
    /// <summary>Below this a value counts as a whole number.</summary>
    private const double IntegerTolerance = 1e-6;

    /// <summary>
    /// Gomory cuts converge in theory but can be slow in practice, so the round count is
    /// capped and reported rather than left to run forever.
    /// </summary>
    private const int MaximumCuts = 40;

    public string Name => "Cutting Plane Algorithm (Revised)";

    public bool CanSolve(CanonicalMatrix model) => model != null && model.IsIntegerProblem;

    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        if (!model.IsIntegerProblem)
        {
            throw new LpException(
                "The cutting plane algorithm needs at least one variable restricted to int or " +
                "bin. This model has none, so solve it with the simplex instead.");
        }

        var result = new SolveResult(Name);
        result.Iterations.Add(Tableau.Snapshot("Canonical Form", model));

        var mixed = HasContinuousVariables(model);

        var working = AddBinaryUpperBounds(model, result);
        var relaxation = new RevisedPrimalSimplexSolver();

        for (var round = 0; round <= MaximumCuts; round++)
        {
            var label = round == 0 ? "LP Relaxation" : $"Re-solve After Cut {round}";
            var solved = relaxation.Solve(working);

            CopyWorking(solved, result, label);

            if (solved.Status != SolutionStatus.Optimal)
            {
                result.Status = solved.Status;
                result.Message = round == 0
                    ? solved.Message
                    : $"{solved.Message} This appeared after {round} cut(s), which means the " +
                      "integer problem itself has no feasible solution.";
                return result;
            }

            var sourceRow = ChooseSourceRow(solved.FinalTableau);
            if (sourceRow < 0)
            {
                // Every integer-restricted variable landed on a whole number, so the
                // relaxation optimum is already the integer optimum.
                result.Status = SolutionStatus.Optimal;
                result.ObjectiveValue = solved.ObjectiveValue;
                result.VariableValues = solved.VariableValues;
                result.FinalTableau = solved.FinalTableau;
                result.BestCandidateDescription = Describe(solved, round, mixed);
                return result;
            }

            if (round == MaximumCuts)
            {
                throw new LpException(
                    $"Still fractional after {MaximumCuts} Gomory cuts. Gomory cuts are known to " +
                    "converge slowly on some models; branch and bound will handle this one.");
            }

            working = AddGomoryCut(working, solved.FinalTableau, sourceRow, round + 1, result);
        }

        throw new LpException("The cutting plane loop ended without reaching a conclusion.");
    }

    // ------------------------------------------------------------- cut making

    /// <summary>
    /// Picks the tableau row to cut from: the integer-restricted basic variable sitting
    /// furthest from a whole number. Returns -1 when every one of them is already integral.
    /// </summary>
    private static int ChooseSourceRow(CanonicalMatrix tableau)
    {
        var best = -1;
        var bestFraction = IntegerTolerance;

        for (var r = 0; r < tableau.BasicVariables.Length; r++)
        {
            var column = tableau.BasicVariables[r];
            if (column >= tableau.IsIntegerMask.Length || !tableau.IsIntegerMask[column])
                continue;

            var value = tableau.Grid[r + 1, tableau.RhsColumn];
            var fraction = Fraction(value);

            // Both ends of the range mean "nearly whole", so measure distance from whichever
            // is closer rather than treating 0.999 as more fractional than 0.5.
            var distance = Math.Min(fraction, 1.0 - fraction);
            if (distance <= IntegerTolerance)
                continue;

            if (fraction > bestFraction)
            {
                bestFraction = fraction;
                best = r;
            }
        }

        return best;
    }

    private static CanonicalMatrix AddGomoryCut(
        CanonicalMatrix working, CanonicalMatrix tableau, int sourceRow, int cutNumber, SolveResult result)
    {
        var variableColumns = tableau.RhsColumn;
        var coefficients = new double[variableColumns];

        for (var j = 0; j < variableColumns; j++)
            coefficients[j] = Fraction(tableau.Grid[sourceRow + 1, j]);

        var rhs = Fraction(tableau.Grid[sourceRow + 1, tableau.RhsColumn]);
        var sourceName = tableau.ColumnLabels[tableau.BasicVariables[sourceRow]];
        var sourceValue = tableau.Grid[sourceRow + 1, tableau.RhsColumn];

        RecordCut(result, tableau, coefficients, rhs, cutNumber, sourceName, sourceValue);

        return working.WithExtraConstraint(coefficients, Relation.GEQ, rhs);
    }

    /// <summary>The fractional part, defined so that a negative value still yields 0 &lt;= f &lt; 1.</summary>
    private static double Fraction(double value)
    {
        var fraction = value - Math.Floor(value);

        // Floating point can leave a value a hair under the next integer, where the true
        // fraction is zero but the arithmetic says 0.9999999999. That would generate a cut
        // that removes nothing and loops forever.
        if (fraction < IntegerTolerance || fraction > 1.0 - IntegerTolerance)
            return 0.0;

        return fraction;
    }

    // ---------------------------------------------------------- binary bounds

    private static bool HasContinuousVariables(CanonicalMatrix model)
    {
        for (var j = 0; j < model.RhsColumn; j++)
        {
            if (model.ColumnTypes != null
                && model.ColumnTypes[j] == VariableType.Decision
                && !model.IsIntegerMask[j])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Adds an <c>x &lt;= 1</c> row for every binary column, because nothing else in the
    /// pipeline ever writes that bound into the grid.
    /// </summary>
    private static CanonicalMatrix AddBinaryUpperBounds(CanonicalMatrix model, SolveResult result)
    {
        var working = model;
        var added = new List<string>();

        for (var j = 0; j < model.RhsColumn; j++)
        {
            if (!model.IsBinaryMask[j])
                continue;

            var row = new double[working.RhsColumn];
            row[j] = 1.0;
            working = working.WithExtraConstraint(row, Relation.LEQ, 1.0);
            added.Add(model.ColumnLabels[j]);
        }

        if (added.Count > 0)
        {
            result.Iterations.Add(Tableau.Snapshot("Canonical Form With Binary Bounds", working));
            var last = result.Iterations[result.Iterations.Count - 1];
            last.Note =
                "A bin restriction is only a flag - no upper bound is written into the canonical " +
                "form - so an explicit x <= 1 row was added for " + string.Join(", ", added.ToArray()) +
                ". Without these the relaxation is not bounded by 1 and the cuts would chase a " +
                "point that is not even feasible.";
        }

        return working;
    }

    // ---------------------------------------------------------------- display

    /// <summary>
    /// Folds one relaxation solve into this solver's own iteration list, so the output file
    /// shows every price out and product form step of every round in order.
    /// </summary>
    private static void CopyWorking(SolveResult solved, SolveResult result, string label)
    {
        foreach (var step in solved.Iterations)
        {
            // The relaxation restates the canonical form each round; it is already in the
            // output from the first round, and repeating it just buries the actual working.
            if (step.Title == "Canonical Form")
                continue;

            result.Iterations.Add(new Tableau(
                label + " - " + step.Title, step.Grid, step.BasicVariables, step.ColumnLabels)
            {
                RowLabels = step.RowLabels,
                Note = step.Note,
                PivotRow = step.PivotRow,
                PivotColumn = step.PivotColumn
            });
        }
    }

    private static void RecordCut(
        SolveResult result, CanonicalMatrix tableau, double[] coefficients, double rhs,
        int cutNumber, string sourceName, double sourceValue)
    {
        var columns = tableau.ColumnCount;
        var grid = new double[1, columns];

        for (var j = 0; j < tableau.RhsColumn; j++)
            grid[0, j] = coefficients[j];

        grid[0, tableau.RhsColumn] = rhs;

        result.Iterations.Add(new Tableau(
            $"Gomory Cut {cutNumber}", grid, new int[0], new List<string>(tableau.ColumnLabels))
        {
            RowLabels = new List<string> { "cut" },
            Note =
                $"Source row: {sourceName} = {OutputWriter.Format(sourceValue)}, which has to be a " +
                $"whole number. Taking the fractional part of every coefficient in that row gives " +
                $"the cut above, read as >= {OutputWriter.Format(rhs)}. Every integer point " +
                $"satisfies it and the current fractional optimum does not, so the cut is added to " +
                $"the model and the relaxation is solved again."
        });
    }

    private static string Describe(SolveResult solved, int cuts, bool mixed)
    {
        var parts = new List<string>();
        for (var j = 0; j < solved.VariableValues.Length; j++)
            parts.Add($"x{j + 1} = {OutputWriter.Format(solved.VariableValues[j])}");

        var summary =
            $"Integer optimum after {cuts} cut(s): {string.Join(", ", parts.ToArray())}, " +
            $"objective {OutputWriter.Format(solved.ObjectiveValue)}.";

        if (mixed)
        {
            summary +=
                " WARNING: this model mixes integer and continuous variables. Pure Gomory " +
                "fractional cuts are only valid when every variable is integer, so this answer " +
                "should be confirmed with branch and bound.";
        }

        return summary;
    }
}
