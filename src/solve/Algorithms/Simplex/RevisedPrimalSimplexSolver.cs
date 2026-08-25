using Solve.Exceptions;
using Solve.Models;
using Solve.Output;

namespace Solve.Algorithms.Simplex;

// ============================================================================
//  OWNER: Person A
//  Marks: Revised Primal Simplex Algorithm (4)
//  Criteria: display the canonical form and solve, showing ALL Product Form and
//  Price Out iterations.
//
//  This is the engine the Cutting Plane algorithm reuses, so the basis inverse
//  and the price-out step are kept reusable rather than inlined into the loop.
// ============================================================================

/// <summary>
/// Revised primal simplex. Instead of pivoting the whole tableau every iteration, it carries
/// the basis inverse and rebuilds only the two things an iteration actually needs: the
/// reduced costs, and the column of the entering variable.
/// </summary>
/// <remarks>
/// <para>
/// Each iteration is two displayed steps, which is what the brief asks to see. The
/// <b>price out</b> computes the simplex multipliers <c>y = c_B B^-1</c> and from them the
/// reduced cost <c>z_j - c_j = y·A_j - c_j</c> of every column; the most negative one enters.
/// The <b>product form</b> updates the inverse by pre-multiplying it with an elementary
/// matrix built from the entering column, which is why the inverse never has to be
/// recomputed from scratch.
/// </para>
/// <para>
/// The reduced-cost row printed by the price-out step is exactly what row 0 of the
/// corresponding full tableau would hold, so the two simplex implementations can be compared
/// line for line on the same model.
/// </para>
/// <para>
/// Like the primal simplex, this ignores the integer masks and solves the relaxation, so
/// branch and bound can drive it directly.
/// </para>
/// </remarks>
public class RevisedPrimalSimplexSolver : ISolver
{
    private const double Tolerance = 1e-9;

    public string Name => "Revised Primal Simplex Algorithm";

    public bool CanSolve(CanonicalMatrix model) => model != null && !model.IsIntegerProblem;

    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var result = new SolveResult(Name);
        result.Iterations.Add(Tableau.Snapshot("Canonical Form", model));

        var state = new BasisState(model);
        var twoPhase = HasArtificials(model);

        if (twoPhase)
        {
            var phaseOneCost = BuildPhaseOneCost(model);
            Run(model, state, result, phaseOneCost, barArtificials: false, phase: "Phase 1");

            // Phase one maximises the negated sum of the artificials, so its optimal value is
            // zero exactly when every artificial has been driven out.
            if (state.ObjectiveValue(phaseOneCost) < -Tolerance)
            {
                result.Status = SolutionStatus.Infeasible;
                result.Message =
                    "Infeasible: phase 1 could not drive the artificial variables to zero, so " +
                    "no point satisfies every constraint at the same time.";
                return result;
            }

            DriveArtificialsOutOfBasis(model, state);
        }

        var cost = BuildObjectiveCost(model);
        var status = Run(model, state, result, cost, barArtificials: true,
                         phase: twoPhase ? "Phase 2" : null);

        result.Status = status;

        if (status == SolutionStatus.Unbounded)
        {
            result.Message =
                "Unbounded: the objective can be improved without limit, so there is no " +
                "optimal solution.";
            return result;
        }

        // Sensitivity analysis works off a tableau, so the inverse is expanded back into one.
        // It matches what the full-tableau primal simplex would have produced.
        var finalTableau = state.ToTableau(model, cost);
        result.FinalTableau = finalTableau;
        result.Iterations.Add(Tableau.Snapshot("Final Tableau", finalTableau));

        var objective = state.ObjectiveValue(cost);
        result.ObjectiveValue =
            model.OriginalObjectiveType == ProblemType.Min ? -objective : objective;
        result.VariableValues = state.DecisionValues(model);
        return result;
    }

    // ------------------------------------------------------------- the engine

    private SolutionStatus Run(
        CanonicalMatrix model, BasisState state, SolveResult result,
        double[] cost, bool barArtificials, string phase)
    {
        var prefix = phase == null ? string.Empty : phase + " ";
        var blandThreshold = 2 * (model.RowCount + model.ColumnCount);
        var iterationLimit = 50 * (model.RowCount + model.ColumnCount) + 100;
        var iteration = 0;

        RecordProductForm(
            model, state, result, $"{prefix}Initial Basis Inverse",
            phase == "Phase 2"
                ? "Phase 2 carries on from the basis phase 1 finished on, so the inverse is the " +
                  "one phase 1 built. Only the cost vector changes."
                : "The basis starts on the slack and artificial columns, each carrying +1 in its " +
                  "own row, so the starting basis matrix and its inverse are both the identity.");

        while (true)
        {
            iteration++;

            var y = state.SimplexMultipliers(model, cost);
            var reduced = state.ReducedCosts(model, cost, y);
            var entering = ChooseEnteringColumn(model, reduced, barArtificials,
                                                useBland: iteration > blandThreshold);

            RecordPriceOut(model, state, result, cost, y, reduced, entering,
                           $"{prefix}Price Out {iteration}");

            if (entering < 0)
                return SolutionStatus.Optimal;

            var alpha = state.EnteringColumn(model, entering);
            var leaving = ChooseLeavingRow(state, alpha);

            if (leaving < 0)
            {
                var last = result.Iterations[result.Iterations.Count - 1];
                last.Note +=
                    $" No row bounds {Label(model, entering)}: every entry of B^-1 A_j is zero " +
                    "or negative, so the model is unbounded.";
                return SolutionStatus.Unbounded;
            }

            var leavingColumn = state.Basis[leaving];
            var ratio = state.BasicValues[leaving] / alpha[leaving];

            state.Update(alpha, leaving, entering);

            RecordProductForm(
                model, state, result, $"{prefix}Product Form {iteration}",
                $"{Label(model, entering)} enters, {Label(model, leavingColumn)} leaves on a " +
                $"minimum ratio of {OutputWriter.Format(ratio)}. The inverse was pre-multiplied " +
                $"by the elementary matrix for row {leaving + 1}, eta = {Vector(state.LastEta)}.");

            if (iteration > iterationLimit)
            {
                throw new LpException(
                    $"The revised simplex did not converge after {iteration} iterations. This " +
                    "usually means the model is degenerate in a way the pivot rules did not resolve.");
            }
        }
    }

    private static int ChooseEnteringColumn(
        CanonicalMatrix model, double[] reduced, bool barArtificials, bool useBland)
    {
        var best = -1;
        var bestValue = -Tolerance;

        for (var j = 0; j < model.RhsColumn; j++)
        {
            if (barArtificials && model.IsArtificial(j))
                continue;

            if (reduced[j] >= -Tolerance)
                continue;

            if (useBland)
                return j;

            if (reduced[j] < bestValue)
            {
                bestValue = reduced[j];
                best = j;
            }
        }

        return best;
    }

    /// <summary>Minimum ratio test over B^-1 b against B^-1 A_j. -1 means unbounded.</summary>
    private static int ChooseLeavingRow(BasisState state, double[] alpha)
    {
        var best = -1;
        var bestRatio = double.PositiveInfinity;
        var bestBasic = int.MaxValue;

        for (var r = 0; r < alpha.Length; r++)
        {
            if (alpha[r] <= Tolerance)
                continue;

            var ratio = state.BasicValues[r] / alpha[r];

            var better = ratio < bestRatio - Tolerance
                         || (Math.Abs(ratio - bestRatio) <= Tolerance && state.Basis[r] < bestBasic);

            if (better)
            {
                best = r;
                bestRatio = ratio;
                bestBasic = state.Basis[r];
            }
        }

        return best;
    }

    /// <summary>
    /// Pivots any artificial left basic at zero out of the basis, for the same reason the
    /// full-tableau version does: phase two bars them from entering, but one left basic could
    /// still be pushed positive by a later pivot and quietly break a constraint.
    /// </summary>
    private static void DriveArtificialsOutOfBasis(CanonicalMatrix model, BasisState state)
    {
        for (var r = 0; r < state.Basis.Length; r++)
        {
            if (!model.IsArtificial(state.Basis[r]))
                continue;

            for (var j = 0; j < model.RhsColumn; j++)
            {
                if (model.IsArtificial(j))
                    continue;

                var alpha = state.EnteringColumn(model, j);
                if (Math.Abs(alpha[r]) < Tolerance)
                    continue;

                state.Update(alpha, r, j);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- display

    private static void RecordPriceOut(
        CanonicalMatrix model, BasisState state, SolveResult result,
        double[] cost, double[] y, double[] reduced, int entering, string title)
    {
        var columns = model.ColumnCount;
        var grid = new double[1, columns];

        for (var j = 0; j < model.RhsColumn; j++)
            grid[0, j] = reduced[j];

        grid[0, model.RhsColumn] = state.ObjectiveValue(cost);

        var tableau = new Tableau(title, grid, new int[0], new List<string>(model.ColumnLabels))
        {
            RowLabels = new List<string> { "z-c" },
            PivotColumn = entering,
            Note = "y = c_B B^-1 = " + Vector(y) + ". " +
                   (entering < 0
                       ? "No reduced cost is negative, so this basis is optimal."
                       : $"Most negative reduced cost is {Label(model, entering)} at " +
                         $"{OutputWriter.Format(reduced[entering])}, so it enters.")
        };

        result.Iterations.Add(tableau);
    }

    private static void RecordProductForm(
        CanonicalMatrix model, BasisState state, SolveResult result, string title, string note)
    {
        var m = state.Basis.Length;

        // The inverse with B^-1 b appended, so the current basic solution is visible next to
        // the matrix that produced it.
        var grid = new double[m, m + 1];
        for (var r = 0; r < m; r++)
        {
            for (var k = 0; k < m; k++)
                grid[r, k] = state.Inverse[r, k];

            grid[r, m] = state.BasicValues[r];
        }

        // Labelled r for row, not e: the columns of the inverse correspond to the original
        // constraint rows, and e is already taken by the excess (surplus) variables - on a
        // model with both, "e2" would mean two different things on the same page.
        var columnLabels = new List<string>();
        for (var k = 0; k < m; k++)
            columnLabels.Add("r" + (k + 1));
        columnLabels.Add("B^-1 b");

        var rowLabels = new List<string>();
        for (var r = 0; r < m; r++)
            rowLabels.Add(Label(model, state.Basis[r]));

        result.Iterations.Add(new Tableau(title, grid, new int[0], columnLabels)
        {
            RowLabels = rowLabels,
            Note = note
        });
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Objective coefficients of the normalised maximisation. Row 0 of the canonical grid
    /// holds -c_j, which is why this negates it back.
    /// </summary>
    private static double[] BuildObjectiveCost(CanonicalMatrix model)
    {
        var cost = new double[model.ColumnCount];
        for (var j = 0; j < model.RhsColumn; j++)
            cost[j] = -model.Grid[0, j];

        return cost;
    }

    /// <summary>Phase one maximises minus the sum of the artificials.</summary>
    private static double[] BuildPhaseOneCost(CanonicalMatrix model)
    {
        var cost = new double[model.ColumnCount];
        for (var j = 0; j < model.RhsColumn; j++)
            cost[j] = model.IsArtificial(j) ? -1.0 : 0.0;

        return cost;
    }

    private static bool HasArtificials(CanonicalMatrix model)
    {
        for (var j = 0; j < model.RhsColumn; j++)
        {
            if (model.IsArtificial(j))
                return true;
        }

        return false;
    }

    private static string Label(CanonicalMatrix model, int column) =>
        column >= 0 && column < model.ColumnLabels.Count ? model.ColumnLabels[column] : "col" + column;

    private static string Vector(double[] values)
    {
        if (values == null)
            return "[]";

        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = OutputWriter.Format(values[i]);

        return "[" + string.Join(", ", parts) + "]";
    }

    // ------------------------------------------------------------ basis state

    /// <summary>
    /// The basis, its inverse, and the current basic values. Kept as its own type because the
    /// cutting plane needs to drive exactly this machinery after adding a cut.
    /// </summary>
    private sealed class BasisState
    {
        internal BasisState(CanonicalMatrix model)
        {
            var m = model.ConstraintCount;

            Basis = (int[])model.BasicVariables.Clone();
            Inverse = new double[m, m];
            BasicValues = new double[m];

            // The canonicalizer always seeds the basis with slack or artificial columns, each
            // carrying +1 in its own row, so the starting basis matrix is the identity and its
            // inverse is too.
            for (var r = 0; r < m; r++)
            {
                Inverse[r, r] = 1.0;
                BasicValues[r] = model.Grid[r + 1, model.RhsColumn];
            }
        }

        internal int[] Basis { get; }

        internal double[,] Inverse { get; }

        /// <summary>B^-1 b, the values of the basic variables.</summary>
        internal double[] BasicValues { get; }

        /// <summary>The eta column of the most recent update, kept for the displayed working.</summary>
        internal double[] LastEta { get; private set; }

        /// <summary>y = c_B B^-1.</summary>
        internal double[] SimplexMultipliers(CanonicalMatrix model, double[] cost)
        {
            var m = Basis.Length;
            var y = new double[m];

            for (var k = 0; k < m; k++)
            {
                var sum = 0.0;
                for (var r = 0; r < m; r++)
                    sum += cost[Basis[r]] * Inverse[r, k];

                y[k] = sum;
            }

            return y;
        }

        /// <summary>z_j - c_j for every column.</summary>
        internal double[] ReducedCosts(CanonicalMatrix model, double[] cost, double[] y)
        {
            var reduced = new double[model.ColumnCount];

            for (var j = 0; j < model.RhsColumn; j++)
            {
                var z = 0.0;
                for (var i = 0; i < y.Length; i++)
                    z += y[i] * model.Grid[i + 1, j];

                reduced[j] = z - cost[j];
            }

            return reduced;
        }

        /// <summary>B^-1 A_j, the entering column expressed in the current basis.</summary>
        internal double[] EnteringColumn(CanonicalMatrix model, int column)
        {
            var m = Basis.Length;
            var alpha = new double[m];

            for (var r = 0; r < m; r++)
            {
                var sum = 0.0;
                for (var k = 0; k < m; k++)
                    sum += Inverse[r, k] * model.Grid[k + 1, column];

                alpha[r] = sum;
            }

            return alpha;
        }

        internal double ObjectiveValue(double[] cost)
        {
            var total = 0.0;
            for (var r = 0; r < Basis.Length; r++)
                total += cost[Basis[r]] * BasicValues[r];

            return total;
        }

        /// <summary>
        /// The product form update: pre-multiply the inverse by the elementary matrix built
        /// from the entering column, which differs from the identity in one column only.
        /// </summary>
        internal void Update(double[] alpha, int leaving, int entering)
        {
            var m = Basis.Length;
            var pivot = alpha[leaving];

            var eta = new double[m];
            for (var r = 0; r < m; r++)
                eta[r] = r == leaving ? 1.0 / pivot : -alpha[r] / pivot;

            for (var k = 0; k < m; k++)
            {
                var pivotRowValue = Inverse[leaving, k];

                for (var r = 0; r < m; r++)
                {
                    Inverse[r, k] = r == leaving
                        ? pivotRowValue * eta[leaving]
                        : Inverse[r, k] + eta[r] * pivotRowValue;
                }
            }

            var pivotBasicValue = BasicValues[leaving];
            for (var r = 0; r < m; r++)
            {
                BasicValues[r] = r == leaving
                    ? pivotBasicValue * eta[leaving]
                    : BasicValues[r] + eta[r] * pivotBasicValue;
            }

            Basis[leaving] = entering;
            LastEta = eta;
        }

        internal double[] DecisionValues(CanonicalMatrix model)
        {
            var columnValues = new double[model.ColumnCount];

            for (var r = 0; r < Basis.Length; r++)
                columnValues[Basis[r]] = BasicValues[r];

            // Not a straight copy of the leading columns: a variable declared urs or - was
            // substituted away during canonicalization and has to be reassembled.
            return model.RecoverOriginalValues(columnValues);
        }

        /// <summary>
        /// Expands the inverse back into a full tableau, the same one the tableau simplex
        /// would have arrived at. Sensitivity analysis reads this.
        /// </summary>
        internal CanonicalMatrix ToTableau(CanonicalMatrix model, double[] cost)
        {
            var rows = model.RowCount;
            var columns = model.ColumnCount;
            var grid = new double[rows, columns];

            var y = SimplexMultipliers(model, cost);
            var reduced = ReducedCosts(model, cost, y);

            for (var j = 0; j < model.RhsColumn; j++)
                grid[0, j] = reduced[j];

            grid[0, model.RhsColumn] = ObjectiveValue(cost);

            for (var r = 0; r < Basis.Length; r++)
            {
                for (var j = 0; j < model.RhsColumn; j++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < Basis.Length; k++)
                        sum += Inverse[r, k] * model.Grid[k + 1, j];

                    grid[r + 1, j] = sum;
                }

                grid[r + 1, model.RhsColumn] = BasicValues[r];
            }

            return new CanonicalMatrix(
                grid,
                (int[])Basis.Clone(),
                new List<string>(model.ColumnLabels),
                (bool[])model.IsIntegerMask.Clone(),
                (bool[])model.IsBinaryMask.Clone())
            {
                OriginalObjectiveType = model.OriginalObjectiveType,
                ColumnTypes = model.ColumnTypes == null ? null : (VariableType[])model.ColumnTypes.Clone(),
                VariableMap = model.VariableMap
            };
        }
    }
}
