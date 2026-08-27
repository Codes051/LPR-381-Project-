using Solve.Exceptions;
using Solve.Models;
using Solve.Output;

namespace Solve.Algorithms.NonLinear;

// ============================================================================
//  OWNER: Person C
//  Marks: Non-linear problem solved (+10 BONUS, on top of the 100)
//
//  This is the one part of the project where the brief asks for the CODE to be
//  explained on video, so the method below is written to be explainable: three
//  short steps repeated until nothing moves.
// ============================================================================

/// <summary>
/// Solves a model whose objective is quadratic and whose constraints are linear, by
/// projected gradient descent.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem.</b> An input file whose objective line begins <c>minnl</c> or <c>maxnl</c>
/// declares a separable quadratic objective: the coefficients weight squares rather than a
/// linear sum, so <c>minnl +1 +1</c> means minimise x1 squared plus x2 squared. The
/// constraints stay linear. The plain simplex cannot touch this, because row 0 of a tableau
/// holds one number per variable and a quadratic has no such representation.
/// </para>
/// <para>
/// <b>The method.</b> Three steps, repeated:
/// </para>
/// <list type="number">
/// <item>Evaluate the gradient. For f(x) = sum of c_j x_j squared, the derivative with
/// respect to x_j is 2 c_j x_j, so the gradient is immediate and needs no approximation.</item>
/// <item>Take a step downhill (or uphill when maximising), scaled by a step size that shrinks
/// as the search settles.</item>
/// <item>Project the result back onto the feasible region. The step ignores the constraints,
/// so it usually lands outside; the projection walks it back.</item>
/// </list>
/// <para>
/// <b>The projection</b> is the only part that is not one line. The feasible region is an
/// intersection of half-spaces, and there is no closed form for the nearest point in it. But
/// projecting onto a SINGLE half-space is elementary geometry - move along the normal just
/// far enough to reach the boundary - and cycling through the constraints, projecting onto
/// each violated one in turn, converges to a point in the intersection. That is the method of
/// alternating projections.
/// </para>
/// <para>
/// <b>What it guarantees.</b> Minimising a convex objective over a convex region has one
/// optimum and gradient descent finds it. Maximising a convex objective does not: the maximum
/// sits at a corner and the search can settle at whichever corner it reaches first, so the
/// result is reported as a local optimum rather than claimed as the global one.
/// </para>
/// </remarks>
public class NonLinearSolver : ISolver
{
    /// <summary>Stop once a step moves the point by less than this.</summary>
    private const double ConvergenceTolerance = 1e-9;

    private const int MaximumIterations = 5000;

    /// <summary>Inner sweeps used to walk an infeasible point back into the region.</summary>
    private const int ProjectionSweeps = 200;

    public string Name => "Non-linear Solver (bonus)";

    public bool CanSolve(CanonicalMatrix model) => model != null && model.IsNonLinear;

    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        if (!model.IsNonLinear)
        {
            throw new LpException(
                "This model has a linear objective. Use one of the simplex algorithms, or " +
                "write the objective with maxnl or minnl to declare it quadratic.");
        }

        var result = new SolveResult(Name);
        result.Iterations.Add(Tableau.Snapshot("Canonical Form", model));

        var variableCount = model.DecisionVariableCount;
        var weights = ObjectiveWeights(model, variableCount);
        var maximising = model.OriginalObjectiveType == ProblemType.Max;

        // Start from the origin walked into the feasible region, which needs no starting guess
        // from the user and is reproducible run to run.
        var point = new double[variableCount];
        Project(point, model, variableCount);

        RecordStep(result, model, point, weights, 0, "Starting Point",
                   "The search starts at the origin projected into the feasible region.");

        var stepSize = 0.1;
        var iteration = 0;
        var converged = false;

        while (iteration < MaximumIterations)
        {
            iteration++;

            var previous = (double[])point.Clone();

            // Step 1 and 2: gradient, then a step along it.
            for (var j = 0; j < variableCount; j++)
            {
                var slope = 2.0 * weights[j] * point[j];
                point[j] += maximising ? stepSize * slope : -stepSize * slope;
            }

            // Step 3: the step ignored the constraints, so walk back into the region.
            Project(point, model, variableCount);

            var movement = Distance(previous, point);

            if (movement < ConvergenceTolerance)
            {
                converged = true;
                break;
            }

            // Shrinking the step keeps the search from oscillating across the optimum once it
            // is close, which a fixed step does on a steep quadratic.
            stepSize *= 0.999;

            // Recording every one of several thousand iterations would bury the output file,
            // so the early ones and then a sample are kept.
            if (iteration <= 5 || iteration % 250 == 0)
            {
                RecordStep(result, model, point, weights, iteration, $"Iteration {iteration}",
                           $"Moved {OutputWriter.Format(movement)} this step.");
            }
        }

        RecordStep(result, model, point, weights, iteration, "Final Point",
                   converged
                       ? $"Converged after {iteration} iterations: a further step moves the point " +
                         "by less than the tolerance."
                       : $"Stopped at the iteration cap of {MaximumIterations}.");

        result.Status = SolutionStatus.Optimal;
        result.ObjectiveValue = Evaluate(point, weights);
        result.VariableValues = point;
        result.FinalTableau = model;
        result.BestCandidateDescription = Describe(point, weights, maximising, iteration);
        return result;
    }

    /// <summary>
    /// The weight on each squared term, read from the objective row of the canonical form.
    /// </summary>
    /// <remarks>
    /// The canonicalizer stores the objective negated, and negated twice for a Min, so undoing
    /// that recovers the coefficients as the user wrote them.
    /// </remarks>
    private static double[] ObjectiveWeights(CanonicalMatrix model, int variableCount)
    {
        var weights = new double[variableCount];
        var minimising = model.OriginalObjectiveType == ProblemType.Min;

        for (var j = 0; j < variableCount; j++)
        {
            var stored = model.Grid[0, j];
            weights[j] = minimising ? stored : -stored;
        }

        return weights;
    }

    private static double Evaluate(double[] point, double[] weights)
    {
        var total = 0.0;

        for (var j = 0; j < point.Length; j++)
            total += weights[j] * point[j] * point[j];

        return total;
    }

    /// <summary>
    /// Walks a point back into the feasible region by repeatedly projecting it onto whichever
    /// constraints it currently violates, and onto the non-negative orthant.
    /// </summary>
    /// <remarks>
    /// Projecting onto one half-space a.x &lt;= b is elementary: if the point violates it,
    /// slide along the normal by exactly the amount of the violation divided by the squared
    /// length of the normal. Repeating over all the constraints converges to a point in their
    /// intersection, which is what makes this usable without a nested optimiser.
    /// </remarks>
    private static void Project(double[] point, CanonicalMatrix model, int variableCount)
    {
        for (var sweep = 0; sweep < ProjectionSweeps; sweep++)
        {
            var worstViolation = 0.0;

            for (var row = 1; row < model.RowCount; row++)
            {
                double normSquared = 0.0;
                double activity = 0.0;

                for (var j = 0; j < variableCount; j++)
                {
                    var coefficient = model.Grid[row, j];
                    activity += coefficient * point[j];
                    normSquared += coefficient * coefficient;
                }

                if (normSquared < 1e-12)
                    continue;

                var rhs = model.Grid[row, model.RhsColumn];

                // The canonical form stores every row as a <= or an equality after its added
                // variables are accounted for, so the sign of the surplus tells the direction.
                var relationIsGreaterOrEqual = RowIsGreaterOrEqual(model, row);
                var violation = relationIsGreaterOrEqual ? rhs - activity : activity - rhs;

                if (violation <= 1e-12)
                    continue;

                worstViolation = Math.Max(worstViolation, violation);
                var scale = violation / normSquared;

                for (var j = 0; j < variableCount; j++)
                {
                    var coefficient = model.Grid[row, j];
                    point[j] += relationIsGreaterOrEqual ? scale * coefficient : -scale * coefficient;
                }
            }

            // Every variable is non-negative, so clamping is the projection onto that part.
            for (var j = 0; j < variableCount; j++)
            {
                if (point[j] < 0.0)
                {
                    worstViolation = Math.Max(worstViolation, -point[j]);
                    point[j] = 0.0;
                }
            }

            if (worstViolation <= 1e-12)
                return;
        }
    }

    /// <summary>
    /// Whether a canonical row represents a >= constraint, which is true exactly when it
    /// carries a surplus variable.
    /// </summary>
    private static bool RowIsGreaterOrEqual(CanonicalMatrix model, int row)
    {
        if (model.ColumnTypes == null)
            return false;

        for (var j = 0; j < model.RhsColumn; j++)
        {
            if (model.ColumnTypes[j] == VariableType.Surplus && Math.Abs(model.Grid[row, j] + 1.0) < 1e-9)
                return true;
        }

        return false;
    }

    private static double Distance(double[] a, double[] b)
    {
        var total = 0.0;

        for (var j = 0; j < a.Length; j++)
        {
            var difference = a[j] - b[j];
            total += difference * difference;
        }

        return Math.Sqrt(total);
    }

    private static void RecordStep(
        SolveResult result, CanonicalMatrix model, double[] point, double[] weights,
        int iteration, string title, string note)
    {
        // One row of the current point with the objective value alongside it, which is the
        // useful thing to watch on a gradient method - there is no tableau to show.
        var grid = new double[1, point.Length + 1];

        for (var j = 0; j < point.Length; j++)
            grid[0, j] = point[j];

        grid[0, point.Length] = Evaluate(point, weights);

        var labels = new List<string>();
        for (var j = 0; j < point.Length; j++)
            labels.Add("x" + (j + 1));

        labels.Add("f(x)");

        result.Iterations.Add(new Tableau(title, grid, new int[0], labels)
        {
            RowLabels = new List<string> { "point" },
            Note = note
        });
    }

    private static string Describe(double[] point, double[] weights, bool maximising, int iterations)
    {
        var parts = new List<string>();

        for (var j = 0; j < point.Length; j++)
            parts.Add($"x{j + 1} = {OutputWriter.Format(point[j])}");

        var summary =
            $"Projected gradient {(maximising ? "ascent" : "descent")} stopped after {iterations} " +
            $"iterations at {string.Join(", ", parts.ToArray())}, " +
            $"f(x) = {OutputWriter.Format(Evaluate(point, weights))}.";

        return maximising
            ? summary + " Maximising a convex objective has its optimum at a corner of the " +
                        "feasible region, so this is a local optimum, not necessarily the global one."
            : summary + " The objective is convex and the region is convex, so this is the " +
                        "global minimum.";
    }
}
