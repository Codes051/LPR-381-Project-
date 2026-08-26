using Solve.Algorithms.Simplex;
using Solve.Models;
using Solve.Output;

namespace Solve.Algorithms.BranchAndBound;

// Branch and Bound Simplex solver.
// Uses the primal simplex solver for each LP relaxation.
public class BranchAndBoundSimplexSolver : ISolver
{
    private const double Tolerance = 1e-8;

    public string Name => "Branch and Bound Simplex Algorithm";

    public bool CanSolve(CanonicalMatrix model)
    {
        return model != null && model.IsIntegerProblem;
    }

    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var result = new SolveResult(Name);
        var simplex = new PrimalSimplexSolver();

        BranchAndBoundNode bestNode = null;
        CanonicalMatrix bestFinalTableau = null;
        var sawUnbounded = false;

        double incumbent =
            model.OriginalObjectiveType == ProblemType.Max
                ? double.NegativeInfinity
                : double.PositiveInfinity;

        var rootModel = AddBinaryUpperBounds(model, result);

        var root = new BranchAndBoundNode("1", rootModel)
        {
            BranchDescription = "Root LP relaxation"
        };

        ExploreNode(
            root,
            simplex,
            result,
            ref bestNode,
            ref incumbent,
            ref bestFinalTableau,
            ref sawUnbounded);

        if (bestNode == null)
        {
            if (sawUnbounded)
            {
                result.Status = SolutionStatus.Unbounded;
                result.Message =
                    "The LP relaxation is unbounded, so no finite optimum was found.";

                result.BestCandidateDescription =
                    "No finite best candidate was found.";

                return result;
            }

            result.Status = SolutionStatus.Infeasible;
            result.Message =
                "No integer-feasible solution was found by Branch and Bound.";

            result.BestCandidateDescription =
                "No integer-feasible candidate was found.";

            return result;
        }

        result.Status = SolutionStatus.Optimal;
        result.ObjectiveValue = bestNode.Bound;

        result.VariableValues =
            bestNode.VariableValues == null
                ? Array.Empty<double>()
                : (double[])bestNode.VariableValues.Clone();

        result.FinalTableau =
            bestFinalTableau == null
                ? bestNode.Model.Clone()
                : bestFinalTableau.Clone();

        result.BestCandidateDescription =
            BuildBestCandidateDescription(bestNode);

        result.Message =
            $"Branch and Bound completed. Best candidate was found at node {bestNode.Label}.";

        return result;
    }

    // Explore a node and then continue down the tree
    private static void ExploreNode(
        BranchAndBoundNode node,
        PrimalSimplexSolver simplex,
        SolveResult overallResult,
        ref BranchAndBoundNode bestNode,
        ref double incumbent,
        ref CanonicalMatrix bestFinalTableau,
        ref bool sawUnbounded)
    {
        var relaxation = simplex.Solve(node.Model);

        node.Status = relaxation.Status;

        CopyIterations(node, relaxation, overallResult);

        // Fathom infeasible nodes
        if (relaxation.Status == SolutionStatus.Infeasible)
        {
            node.Fathomed = FathomReason.Infeasible;

            AddNodeMessage(
                node,
                overallResult,
                $"Node {node.Label} fathomed: LP relaxation is infeasible.");

            return;
        }

        if (relaxation.Status == SolutionStatus.Unbounded)
        {
            sawUnbounded = true;

            AddNodeMessage(
                node,
                overallResult,
                $"Node {node.Label}: LP relaxation is unbounded.");

            return;
        }

        node.Bound = relaxation.ObjectiveValue;

        node.VariableValues =
            relaxation.VariableValues == null
                ? Array.Empty<double>()
                : (double[])relaxation.VariableValues.Clone();

        // Fathom if this node cannot improve the best solution
        if (bestNode != null &&
            CannotBeatIncumbent(
                node.Bound,
                incumbent,
                node.Model.OriginalObjectiveType))
        {
            node.Fathomed = FathomReason.BoundedOut;

            AddNodeMessage(
                node,
                overallResult,
                $"Node {node.Label} fathomed by bound. " +
                $"Relaxation = {OutputWriter.Format(node.Bound)}, " +
                $"incumbent = {OutputWriter.Format(incumbent)}.");

            return;
        }

        // Find an integer variable that still has a fractional value
        int branchColumn;
        int originalVariable;
        double branchValue;

        var hasFractionalVariable = TryFindFractionalVariable(
            node.Model,
            relaxation.VariableValues,
            out branchColumn,
            out originalVariable,
            out branchValue);

        // No fractional variables means this is a valid integer solution
        if (!hasFractionalVariable)
        {
            node.Fathomed = FathomReason.IntegerFeasible;

            AddNodeMessage(
                node,
                overallResult,
                $"Node {node.Label} fathomed: integer-feasible candidate " +
                $"with objective {OutputWriter.Format(node.Bound)}.");

            if (bestNode == null ||
                IsBetter(
                    node.Bound,
                    incumbent,
                    node.Model.OriginalObjectiveType))
            {
                bestNode = node;
                incumbent = node.Bound;
                bestFinalTableau = relaxation.FinalTableau?.Clone();

                AddNodeMessage(
                    node,
                    overallResult,
                    $"New incumbent at node {node.Label}: " +
                    $"{OutputWriter.Format(node.Bound)}.");
            }

            return;
        }

        // Create the two branches
        var floor = Math.Floor(branchValue);
        var ceil = Math.Ceiling(branchValue);

        var leftModel = AddVariableBound(
            node.Model,
            branchColumn,
            Relation.LEQ,
            floor);

        var rightModel = AddVariableBound(
            node.Model,
            branchColumn,
            Relation.GEQ,
            ceil);

        var variableName = GetOriginalVariableName(originalVariable);

        var left = new BranchAndBoundNode(
            node.Label + ".1",
            leftModel,
            node)
        {
            BranchDescription =
                $"{variableName} <= {OutputWriter.Format(floor)}"
        };

        var right = new BranchAndBoundNode(
            node.Label + ".2",
            rightModel,
            node)
        {
            BranchDescription =
                $"{variableName} >= {OutputWriter.Format(ceil)}"
        };

        node.Children.Add(left);
        node.Children.Add(right);

        AddNodeMessage(
            node,
            overallResult,
            $"Branch at node {node.Label} on {variableName} = " +
            $"{OutputWriter.Format(branchValue)}. " +
            $"Create {left.Label}: {left.BranchDescription} and " +
            $"{right.Label}: {right.BranchDescription}.");

        // Explore the left branch first
        ExploreNode(
            left,
            simplex,
            overallResult,
            ref bestNode,
            ref incumbent,
            ref bestFinalTableau,
            ref sawUnbounded);

        // Backtrack before moving to the right branch
        AddBacktrackMessage(
            node,
            left,
            overallResult);

        ExploreNode(
            right,
            simplex,
            overallResult,
            ref bestNode,
            ref incumbent,
            ref bestFinalTableau,
            ref sawUnbounded);

        AddBacktrackMessage(
            node,
            right,
            overallResult);
    }

    // A bin restriction only sets IsBinaryMask - no x <= 1 row is ever written into the
    // canonical form - so branching on integrality alone will happily settle a binary
    // variable on any whole number. On the knapsack that returned x3 = 5 and an objective
    // of 19 instead of 15. The upper bounds have to be supplied here, before the root
    // relaxation is solved.
    private static CanonicalMatrix AddBinaryUpperBounds(
        CanonicalMatrix model,
        SolveResult result)
    {
        var working = model.Clone();
        var bounded = new List<string>();

        for (var column = 0; column < model.RhsColumn; column++)
        {
            if (!model.IsBinaryMask[column])
                continue;

            var coefficients = new double[working.RhsColumn];
            coefficients[column] = 1.0;

            working = working.WithExtraConstraint(
                coefficients,
                Relation.LEQ,
                1.0);

            bounded.Add(model.ColumnLabels[column]);
        }

        if (bounded.Count > 0)
        {
            result.Iterations.Add(
                new Tableau(
                    "Canonical Form With Binary Bounds",
                    (double[,])working.Grid.Clone(),
                    (int[])working.BasicVariables.Clone(),
                    new List<string>(working.ColumnLabels))
                {
                    Note =
                        "A bin restriction is only a flag, so an explicit x <= 1 row was added " +
                        "for " + string.Join(", ", bounded.ToArray()) + " before branching. " +
                        "Without these the search can return a whole number outside 0 and 1."
                });
        }

        return working;
    }

    // Add x <= value or x >= value to a child problem
    private static CanonicalMatrix AddVariableBound(
        CanonicalMatrix model,
        int variableColumn,
        Relation relation,
        double rhs)
    {
        var coefficients = new double[model.RhsColumn];

        coefficients[variableColumn] = 1.0;

        return model.WithExtraConstraint(
            coefficients,
            relation,
            rhs);
    }

    // Find the first integer variable that is fractional
    private static bool TryFindFractionalVariable(
        CanonicalMatrix model,
        double[] originalValues,
        out int branchColumn,
        out int originalVariable,
        out double branchValue)
    {
        branchColumn = -1;
        originalVariable = -1;
        branchValue = 0.0;

        if (originalValues == null)
            return false;

        if (model.VariableMap != null)
        {
            for (var variable = 0;
                 variable < model.VariableMap.Length &&
                 variable < originalValues.Length;
                 variable++)
            {
                var column = model.VariableMap[variable].PositiveColumn;

                if (column < 0 ||
                    column >= model.IsIntegerMask.Length ||
                    !model.IsIntegerMask[column])
                {
                    continue;
                }

                var value = originalValues[variable];

                if (!IsInteger(value))
                {
                    branchColumn = column;
                    originalVariable = variable;
                    branchValue = value;
                    return true;
                }
            }

            return false;
        }

        var count = Math.Min(
            originalValues.Length,
            model.RhsColumn);

        for (var column = 0; column < count; column++)
        {
            if (!model.IsIntegerMask[column])
                continue;

            var value = originalValues[column];

            if (!IsInteger(value))
            {
                branchColumn = column;
                originalVariable = column;
                branchValue = value;
                return true;
            }
        }

        return false;
    }

    private static bool IsInteger(double value)
    {
        return Math.Abs(value - Math.Round(value)) <= Tolerance;
    }

    // Check whether a candidate improves the current best solution
    private static bool IsBetter(
        double candidate,
        double incumbent,
        ProblemType type)
    {
        if (type == ProblemType.Max)
            return candidate > incumbent + Tolerance;

        return candidate < incumbent - Tolerance;
    }

    // Check whether a node can still beat the incumbent
    private static bool CannotBeatIncumbent(
        double bound,
        double incumbent,
        ProblemType type)
    {
        if (type == ProblemType.Max)
            return bound <= incumbent + Tolerance;

        return bound >= incumbent - Tolerance;
    }

    // Copy the simplex tableaus into the B&B result
    private static void CopyIterations(
        BranchAndBoundNode node,
        SolveResult relaxation,
        SolveResult overallResult)
    {
        foreach (var iteration in relaxation.Iterations)
        {
            var title =
                $"Sub-problem {node.Label} - {node.BranchDescription} - " +
                iteration.Title;

            var copy = CopyTableau(iteration, title);

            node.Iterations.Add(copy);
            overallResult.Iterations.Add(copy);
        }

        if (node.Iterations.Count > 0)
        {
            var last = node.Iterations[node.Iterations.Count - 1];

            var existing =
                string.IsNullOrWhiteSpace(last.Note)
                    ? string.Empty
                    : last.Note + " ";

            last.Note =
                existing +
                $"Node {node.Label} LP relaxation complete.";
        }
    }

    private static Tableau CopyTableau(
        Tableau source,
        string title)
    {
        return new Tableau(
            title,
            (double[,])source.Grid.Clone(),
            (int[])source.BasicVariables.Clone(),
            new List<string>(source.ColumnLabels))
        {
            Note = source.Note,
            PivotColumn = source.PivotColumn,
            PivotRow = source.PivotRow,
            RowLabels =
                source.RowLabels == null
                    ? null
                    : new List<string>(source.RowLabels)
        };
    }

    // Add an algorithm decision to the output
    private static void AddNodeMessage(
        BranchAndBoundNode node,
        SolveResult result,
        string message)
    {
        var tableau = Tableau.Snapshot(
            $"Sub-problem {node.Label} - Decision",
            node.Model);

        tableau.Note = message;

        node.Iterations.Add(tableau);
        result.Iterations.Add(tableau);
    }

    // Make the backtracking visible in the output
    private static void AddBacktrackMessage(
        BranchAndBoundNode parent,
        BranchAndBoundNode child,
        SolveResult result)
    {
        var tableau = Tableau.Snapshot(
            $"Backtrack {child.Label} -> {parent.Label}",
            parent.Model);

        tableau.Note =
            $"Backtracking from node {child.Label} to node {parent.Label}.";

        result.Iterations.Add(tableau);
    }

    private static CanonicalMatrix FindFinalTableau(
        BranchAndBoundNode node)
    {
        if (node.Iterations.Count == 0)
            return node.Model.Clone();

        var final = node.Iterations[node.Iterations.Count - 1];

        return new CanonicalMatrix(
            (double[,])final.Grid.Clone(),
            (int[])final.BasicVariables.Clone(),
            new List<string>(final.ColumnLabels),
            (bool[])node.Model.IsIntegerMask.Clone(),
            (bool[])node.Model.IsBinaryMask.Clone())
        {
            OriginalObjectiveType = node.Model.OriginalObjectiveType,
            ColumnTypes =
                node.Model.ColumnTypes == null
                    ? null
                    : (VariableType[])node.Model.ColumnTypes.Clone(),

            VariableMap = node.Model.VariableMap
        };
    }

    // Format the winning solution for the output file
    private static string BuildBestCandidateDescription(
        BranchAndBoundNode node)
    {
        var pieces = new List<string>();

        for (var i = 0; i < node.VariableValues.Length; i++)
        {
            pieces.Add(
                $"{GetOriginalVariableName(i)} = " +
                $"{OutputWriter.Format(node.VariableValues[i])}");
        }

        return
            $"Node {node.Label}: " +
            string.Join(", ", pieces) +
            $"; objective = {OutputWriter.Format(node.Bound)}";
    }

    private static string GetOriginalVariableName(int index)
    {
        return "x" + (index + 1);
    }
}

