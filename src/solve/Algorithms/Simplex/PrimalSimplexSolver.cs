using Solve.Exceptions;
using Solve.Models;
using Solve.Output;

namespace Solve.Algorithms.Simplex;

// ============================================================================
//  OWNER: Person A
//  Marks: Primal Simplex Algorithm (4)
//  Criteria: display the canonical form and solve, showing ALL tableau iterations.
// ============================================================================

/// <summary>
/// Tableau primal simplex, two-phase when the model contains artificial variables.
/// </summary>
/// <remarks>
/// <para>
/// The tableau convention throughout: row 0 is the equation
/// <c>z + sum(row0_j * x_j) = grid[0, rhs]</c>. A basis is optimal when every entry in row 0
/// is non-negative, and the objective value is then read straight out of the right-hand-side
/// cell. The canonicalizer normalises Min problems to Max, so this class only ever maximises
/// and flips the sign back once at the end.
/// </para>
/// <para>
/// Phase one is needed because a >= or = constraint starts basic on an artificial variable,
/// which is not a feasible point of the real problem. Phase one minimises the sum of the
/// artificials; if that sum cannot reach zero, no point satisfies all the constraints at
/// once and the model is infeasible.
/// </para>
/// <para>
/// NOTE FOR PERSON B: <see cref="Solve"/> ignores the integer and binary masks completely -
/// it always solves the LP relaxation. That is deliberate, so branch and bound can call it
/// on each sub-problem. <see cref="CanSolve"/> is the stricter check the menu uses.
/// </para>
/// </remarks>
public class PrimalSimplexSolver : ISolver
{
    /// <summary>
    /// Values closer to zero than this are treated as zero. Pivoting accumulates floating
    /// point error, so an exact comparison would eventually mistake noise for a real negative
    /// reduced cost and pivot forever.
    /// </summary>
    private const double Tolerance = 1e-9;

    public string Name => "Primal Simplex Algorithm";

    public bool CanSolve(CanonicalMatrix model)
    {
        // Plain simplex solves the relaxation only. The menu refuses an integer or binary
        // model here and says so, which is part of the error handling criterion.
        return model != null && !model.IsIntegerProblem;
    }

    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        // Never mutate the caller's model: branch and bound relies on the parent surviving
        // its children untouched.
        var work = model.Clone();
        var result = new SolveResult(Name);

        // The canonical form has to be the first recorded tableau - it is what puts the
        // canonical form into the output file. See OutputWriter.
        result.Iterations.Add(Tableau.Snapshot("Canonical Form", work));

        var twoPhase = HasArtificials(work);

        if (twoPhase && !RunPhaseOne(work, result))
        {
            result.Status = SolutionStatus.Infeasible;
            result.Message =
                "Infeasible: phase 1 could not drive the artificial variables to zero, so no " +
                "point satisfies every constraint at the same time.";
            return result;
        }

        if (twoPhase)
        {
            RestoreObjectiveRow(work, model);
            result.Iterations.Add(Tableau.Snapshot("Phase 2 - Starting Tableau", work));
        }

        var status = Optimise(work, result, barArtificials: true,
                              titlePrefix: twoPhase ? "Phase 2 Iteration" : "Iteration");

        result.Status = status;

        if (status == SolutionStatus.Unbounded)
        {
            result.Message =
                "Unbounded: the objective can be improved without limit, so there is no " +
                "optimal solution.";
            return result;
        }

        result.FinalTableau = work;
        result.ObjectiveValue = ReadObjectiveValue(work);
        result.VariableValues = ExtractVariableValues(work);
        return result;
    }

    // ---------------------------------------------------------------- phase 1

    /// <summary>Runs phase one. Returns false if the model is infeasible.</summary>
    private bool RunPhaseOne(CanonicalMatrix work, SolveResult result)
    {
        BuildPhaseOneObjective(work);
        result.Iterations.Add(Tableau.Snapshot("Phase 1 - Starting Tableau", work));

        // Phase one cannot be unbounded - the sum of the artificials is bounded below by
        // zero - so the only outcome worth inspecting is the objective value it reaches.
        Optimise(work, result, barArtificials: false, titlePrefix: "Phase 1 Iteration");

        var remainingInfeasibility = -work.Grid[0, work.RhsColumn];
        if (remainingInfeasibility > Tolerance)
            return false;

        DriveArtificialsOutOfBasis(work);
        return true;
    }

    /// <summary>
    /// Replaces row 0 with the phase one objective: maximise the negated sum of the
    /// artificial variables, which is the same thing as minimising their sum.
    /// </summary>
    private static void BuildPhaseOneObjective(CanonicalMatrix work)
    {
        var grid = work.Grid;
        var columns = work.ColumnCount;

        for (var j = 0; j < columns; j++)
            grid[0, j] = work.IsArtificial(j) ? 1.0 : 0.0;

        // The artificials start basic, and a basic variable must have a zero in row 0.
        // Subtracting each artificial row prices it out and drags the right-hand-side cell
        // down to minus the current total infeasibility.
        for (var i = 1; i < work.RowCount; i++)
        {
            if (!work.IsArtificial(work.BasicVariables[i - 1]))
                continue;

            for (var j = 0; j < columns; j++)
                grid[0, j] -= grid[i, j];
        }
    }

    /// <summary>
    /// Pivots any artificial still sitting in the basis at zero out of it.
    /// </summary>
    /// <remarks>
    /// Phase two bars artificials from entering, but one left basic could still be pushed
    /// positive by a later pivot on a row where it has a negative entry, which would silently
    /// produce a solution that violates a constraint. Its right-hand-side is zero at this
    /// point, so any non-artificial column with a non-zero entry can take over the basis for
    /// free. A row where no such column exists is redundant, and leaving the artificial basic
    /// there is safe precisely because no permitted pivot can move that row.
    /// </remarks>
    private static void DriveArtificialsOutOfBasis(CanonicalMatrix work)
    {
        for (var i = 1; i < work.RowCount; i++)
        {
            if (!work.IsArtificial(work.BasicVariables[i - 1]))
                continue;

            for (var j = 0; j < work.RhsColumn; j++)
            {
                if (work.IsArtificial(j) || Math.Abs(work.Grid[i, j]) < Tolerance)
                    continue;

                Pivot(work, i, j);
                break;
            }
        }
    }

    /// <summary>
    /// Puts the real objective back into row 0 after phase one, then prices out whatever is
    /// currently basic so the tableau is consistent again.
    /// </summary>
    private static void RestoreObjectiveRow(CanonicalMatrix work, CanonicalMatrix original)
    {
        var grid = work.Grid;
        var columns = work.ColumnCount;

        for (var j = 0; j < columns; j++)
            grid[0, j] = original.Grid[0, j];

        for (var i = 1; i < work.RowCount; i++)
        {
            var basic = work.BasicVariables[i - 1];
            var coefficient = grid[0, basic];
            if (Math.Abs(coefficient) < Tolerance)
                continue;

            for (var j = 0; j < columns; j++)
                grid[0, j] -= coefficient * grid[i, j];
        }
    }

    // ------------------------------------------------------------ the simplex

    private SolutionStatus Optimise(
        CanonicalMatrix work, SolveResult result, bool barArtificials, string titlePrefix)
    {
        // Dantzig's rule can cycle on a degenerate model. Bland's rule cannot, but converges
        // more slowly, so it is held back as a fallback once the iteration count passes what
        // any non-cycling solve should need.
        var blandThreshold = 2 * (work.RowCount + work.ColumnCount);
        var iterationLimit = 50 * (work.RowCount + work.ColumnCount) + 100;
        var iteration = 0;

        while (true)
        {
            var useBland = iteration >= blandThreshold;

            var entering = ChooseEnteringColumn(work, barArtificials, useBland);
            if (entering < 0)
                return SolutionStatus.Optimal;

            var leaving = ChooseLeavingRow(work, entering);
            if (leaving < 0)
            {
                MarkUnbounded(work, result, entering);
                return SolutionStatus.Unbounded;
            }

            // The pivot is annotated on the tableau it was chosen from, so the output reads
            // the way the algorithm is taught: here is the table, here is the pivot, here is
            // what it produced.
            AnnotatePivot(work, result, leaving, entering, useBland);

            Pivot(work, leaving, entering);
            iteration++;
            result.Iterations.Add(Tableau.Snapshot($"{titlePrefix} {iteration}", work));

            if (iteration > iterationLimit)
            {
                throw new LpException(
                    $"The simplex did not converge after {iteration} iterations. This usually " +
                    "means the model is degenerate in a way the pivot rules did not resolve.");
            }
        }
    }

    private static int ChooseEnteringColumn(CanonicalMatrix work, bool barArtificials, bool useBland)
    {
        var best = -1;
        var bestValue = -Tolerance;

        for (var j = 0; j < work.RhsColumn; j++)
        {
            if (barArtificials && work.IsArtificial(j))
                continue;

            var value = work.Grid[0, j];
            if (value >= -Tolerance)
                continue;

            // Bland: the first improving column wins, which is what guarantees termination.
            if (useBland)
                return j;

            if (value < bestValue)
            {
                bestValue = value;
                best = j;
            }
        }

        return best;
    }

    /// <summary>
    /// Minimum ratio test. Returns -1 when no row bounds the entering column, which means
    /// the model is unbounded.
    /// </summary>
    private static int ChooseLeavingRow(CanonicalMatrix work, int entering)
    {
        var best = -1;
        var bestRatio = double.PositiveInfinity;
        var bestBasic = int.MaxValue;

        for (var i = 1; i < work.RowCount; i++)
        {
            var coefficient = work.Grid[i, entering];
            if (coefficient <= Tolerance)
                continue;

            var ratio = work.Grid[i, work.RhsColumn] / coefficient;

            // Ties are broken on the lowest basic variable index. That is Bland's leaving
            // rule, and it is what stops a degenerate model cycling between two bases.
            var better = ratio < bestRatio - Tolerance
                         || (Math.Abs(ratio - bestRatio) <= Tolerance
                             && work.BasicVariables[i - 1] < bestBasic);

            if (better)
            {
                best = i;
                bestRatio = ratio;
                bestBasic = work.BasicVariables[i - 1];
            }
        }

        return best;
    }

    private static void Pivot(CanonicalMatrix work, int row, int column)
    {
        var grid = work.Grid;
        var columns = work.ColumnCount;
        var pivot = grid[row, column];

        for (var j = 0; j < columns; j++)
            grid[row, j] /= pivot;

        // Assigned rather than divided, so accumulated error cannot leave the pivot cell at
        // 0.9999999999 and slowly corrupt the basis.
        grid[row, column] = 1.0;

        for (var r = 0; r < work.RowCount; r++)
        {
            if (r == row)
                continue;

            var factor = grid[r, column];
            if (Math.Abs(factor) < Tolerance)
                continue;

            for (var j = 0; j < columns; j++)
                grid[r, j] -= factor * grid[row, j];

            grid[r, column] = 0.0;
        }

        work.BasicVariables[row - 1] = column;
    }

    // ------------------------------------------------------------- reporting

    private static void AnnotatePivot(
        CanonicalMatrix work, SolveResult result, int leaving, int entering, bool useBland)
    {
        var current = result.Iterations[result.Iterations.Count - 1];
        current.PivotRow = leaving;
        current.PivotColumn = entering;

        var ratio = work.Grid[leaving, work.RhsColumn] / work.Grid[leaving, entering];

        current.Note =
            $"{Label(work, entering)} enters, {Label(work, work.BasicVariables[leaving - 1])} leaves " +
            $"on a minimum ratio of {OutputWriter.Format(ratio)}." +
            (useBland ? " (Bland rule in use to break a cycle.)" : string.Empty);
    }

    private static void MarkUnbounded(CanonicalMatrix work, SolveResult result, int entering)
    {
        var current = result.Iterations[result.Iterations.Count - 1];
        current.PivotColumn = entering;
        current.Note =
            $"{Label(work, entering)} improves the objective but every entry in its column is " +
            "zero or negative, so no row limits how far it can increase. The model is unbounded.";
    }

    private static double ReadObjectiveValue(CanonicalMatrix work)
    {
        var value = work.Grid[0, work.RhsColumn];

        // The canonicalizer normalised a Min problem into a Max by negating the objective,
        // so the value has to be negated back before anyone sees it.
        return work.OriginalObjectiveType == ProblemType.Min ? -value : value;
    }

    private static double[] ExtractVariableValues(CanonicalMatrix work)
    {
        // Anything not in the basis is non-basic and therefore zero, which the array already
        // holds, so only the basic columns need reading out.
        var columnValues = new double[work.ColumnCount];

        for (var i = 1; i < work.RowCount; i++)
            columnValues[work.BasicVariables[i - 1]] = work.Grid[i, work.RhsColumn];

        // Not a straight copy of the leading columns: a variable declared urs or - was
        // substituted away during canonicalization and has to be reassembled.
        return work.RecoverOriginalValues(columnValues);
    }

    private static bool HasArtificials(CanonicalMatrix work)
    {
        for (var j = 0; j < work.RhsColumn; j++)
        {
            if (work.IsArtificial(j))
                return true;
        }

        return false;
    }

    private static string Label(CanonicalMatrix work, int column) =>
        column >= 0 && column < work.ColumnLabels.Count ? work.ColumnLabels[column] : "col" + column;
}
