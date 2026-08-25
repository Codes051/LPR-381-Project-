using Solve.Models;
using Solve.Output;

namespace Solve.Algorithms.BranchAndBound;

// Branch and Bound solver for binary knapsack problems.
public class KnapsackBranchAndBoundSolver : ISolver
{
    private const double Tolerance = 1e-9;

    public string Name => "Branch and Bound Knapsack Algorithm";

    public bool CanSolve(CanonicalMatrix model)
    {
        if (model == null ||
            model.ConstraintCount != 1 ||
            model.OriginalObjectiveType != ProblemType.Max)
        {
            return false;
        }

        var variableCount =
            model.VariableMap != null
                ? model.VariableMap.Length
                : model.DecisionVariableCount;

        if (variableCount == 0)
            return false;

        // A normal knapsack has a <= constraint with a slack variable
        if (model.BasicVariables == null ||
            model.BasicVariables.Length == 0 ||
            model.ColumnTypes == null)
        {
            return false;
        }

        var basic = model.BasicVariables[0];

        if (basic < 0 ||
            basic >= model.ColumnTypes.Length ||
            model.ColumnTypes[basic] != VariableType.Slack)
        {
            return false;
        }

        if (model.Grid[1, model.RhsColumn] < -Tolerance)
            return false;

        for (var i = 0; i < variableCount; i++)
        {
            var column = GetVariableColumn(model, i);

            if (column < 0 ||
                column >= model.IsBinaryMask.Length ||
                !model.IsBinaryMask[column])
            {
                return false;
            }

            if (model.Grid[1, column] <= Tolerance)
                return false;
        }

        return true;
    }

    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        if (!CanSolve(model))
        {
            throw new InvalidOperationException(
                "Branch and Bound Knapsack requires a maximisation problem " +
                "with one <= constraint and binary decision variables.");
        }

        var result = new SolveResult(Name);

        // The canonical form must be first in the output
        result.Iterations.Add(
            Tableau.Snapshot("Canonical Form", model));

        var items = ReadItems(model);
        var rankedItems = items
            .OrderByDescending(item => item.Ratio)
            .ThenBy(item => item.OriginalIndex)
            .ToList();

        var capacity = model.Grid[1, model.RhsColumn];

        var decisions = Enumerable
            .Repeat(-1, items.Count)
            .ToArray();

        var root = new BranchAndBoundNode(
            "1",
            model.Clone())
        {
            BranchDescription = "Root knapsack problem"
        };

        BranchAndBoundNode bestNode = null;
        var incumbent = double.NegativeInfinity;

        ExploreNode(
            root,
            decisions,
            rankedItems,
            capacity,
            result,
            ref bestNode,
            ref incumbent);

        if (bestNode == null)
        {
            result.Status = SolutionStatus.Infeasible;
            result.Message =
                "No feasible binary knapsack solution was found.";

            result.BestCandidateDescription =
                "No feasible candidate was found.";

            return result;
        }

        result.Status = SolutionStatus.Optimal;
        result.ObjectiveValue = bestNode.Bound;
        result.VariableValues =
            (double[])bestNode.VariableValues.Clone();

        result.FinalTableau = model.Clone();

        result.BestCandidateDescription =
            BuildBestCandidateDescription(
                bestNode,
                items,
                model);

        result.Message =
            $"Knapsack Branch and Bound completed. " +
            $"Best candidate was found at node {bestNode.Label}.";

        return result;
    }

    // Explore the current node and then move down the tree
    private static void ExploreNode(
        BranchAndBoundNode node,
        int[] decisions,
        List<Item> rankedItems,
        double capacity,
        SolveResult result,
        ref BranchAndBoundNode bestNode,
        ref double incumbent)
    {
        var evaluation = EvaluateNode(
            decisions,
            rankedItems,
            capacity);

        node.VariableValues =
            (double[])evaluation.Fill.Clone();

        // Included items already exceed capacity
        if (!evaluation.Feasible)
        {
            node.Status = SolutionStatus.Infeasible;
            node.Fathomed = FathomReason.Infeasible;

            var table = CreateNodeTable(
                node,
                decisions,
                rankedItems,
                evaluation);

            table.Note =
                $"Node {node.Label} fathomed: selected items weigh " +
                $"{OutputWriter.Format(evaluation.FixedWeight)}, which exceeds " +
                $"capacity {OutputWriter.Format(capacity)}.";

            AddTable(node, result, table);
            return;
        }

        node.Status = SolutionStatus.Optimal;
        node.Bound = evaluation.Bound;

        // This branch cannot improve the best solution
        if (bestNode != null &&
            node.Bound <= incumbent + Tolerance)
        {
            node.Fathomed = FathomReason.BoundedOut;

            var table = CreateNodeTable(
                node,
                decisions,
                rankedItems,
                evaluation);

            table.Note =
                $"Node {node.Label} fathomed by bound. " +
                $"Bound = {OutputWriter.Format(node.Bound)}, " +
                $"incumbent = {OutputWriter.Format(incumbent)}.";

            AddTable(node, result, table);
            return;
        }

        // No fractional item means the greedy fill is binary
        if (evaluation.FractionalItem < 0)
        {
            node.Fathomed = FathomReason.IntegerFeasible;

            var newIncumbent =
                bestNode == null ||
                node.Bound > incumbent + Tolerance;

            if (newIncumbent)
            {
                incumbent = node.Bound;
                bestNode = node;
            }

            var table = CreateNodeTable(
                node,
                decisions,
                rankedItems,
                evaluation);

            table.Note =
                $"Node {node.Label} fathomed: integer-feasible candidate " +
                $"with objective {OutputWriter.Format(node.Bound)}." +
                (newIncumbent
                    ? $" New incumbent = {OutputWriter.Format(incumbent)}."
                    : string.Empty);

            AddTable(node, result, table);
            return;
        }

        // Branch on the first fractional item in the ranking
        var branchItem = evaluation.FractionalItem;
        var variableName = "x" + (branchItem + 1);

        var takeDecisions = (int[])decisions.Clone();
        takeDecisions[branchItem] = 1;

        var leaveDecisions = (int[])decisions.Clone();
        leaveDecisions[branchItem] = 0;

        var takeNode = new BranchAndBoundNode(
            node.Label + ".1",
            node.Model.Clone(),
            node)
        {
            BranchDescription = $"{variableName} = 1"
        };

        var leaveNode = new BranchAndBoundNode(
            node.Label + ".2",
            node.Model.Clone(),
            node)
        {
            BranchDescription = $"{variableName} = 0"
        };

        node.Children.Add(takeNode);
        node.Children.Add(leaveNode);

        var branchTable = CreateNodeTable(
            node,
            decisions,
            rankedItems,
            evaluation);

        branchTable.Note =
            $"Node {node.Label} bound = {OutputWriter.Format(node.Bound)}. " +
            $"Branch on {variableName}, which has fractional fill " +
            $"{OutputWriter.Format(evaluation.Fill[branchItem])}. " +
            $"Create {takeNode.Label}: {takeNode.BranchDescription} and " +
            $"{leaveNode.Label}: {leaveNode.BranchDescription}.";

        AddTable(node, result, branchTable);

        // Take the item first
        ExploreNode(
            takeNode,
            takeDecisions,
            rankedItems,
            capacity,
            result,
            ref bestNode,
            ref incumbent);

        AddBacktrackTable(
            node,
            takeNode,
            result);

        // Then try leaving it out
        ExploreNode(
            leaveNode,
            leaveDecisions,
            rankedItems,
            capacity,
            result,
            ref bestNode,
            ref incumbent);

        AddBacktrackTable(
            node,
            leaveNode,
            result);
    }

    // Calculate the fractional-knapsack bound for a node
    private static NodeEvaluation EvaluateNode(
        int[] decisions,
        List<Item> rankedItems,
        double capacity)
    {
        var fill = new double[decisions.Length];

        var fixedWeight = 0.0;
        var fixedValue = 0.0;

        foreach (var item in rankedItems)
        {
            if (decisions[item.OriginalIndex] != 1)
                continue;

            fill[item.OriginalIndex] = 1.0;
            fixedWeight += item.Weight;
            fixedValue += item.Value;
        }

        if (fixedWeight > capacity + Tolerance)
        {
            return new NodeEvaluation
            {
                Feasible = false,
                Fill = fill,
                FixedWeight = fixedWeight,
                FixedValue = fixedValue,
                Bound = double.NegativeInfinity,
                FractionalItem = -1
            };
        }

        var remaining = capacity - fixedWeight;
        var bound = fixedValue;
        var fractionalItem = -1;

        foreach (var item in rankedItems)
        {
            var index = item.OriginalIndex;

            if (decisions[index] != -1)
                continue;

            // An item with no positive value does not help a max problem
            if (item.Value <= Tolerance)
            {
                fill[index] = 0.0;
                continue;
            }

            if (item.Weight <= remaining + Tolerance)
            {
                fill[index] = 1.0;
                remaining -= item.Weight;
                bound += item.Value;
                continue;
            }

            if (remaining > Tolerance)
            {
                var fraction = remaining / item.Weight;

                fill[index] = fraction;
                bound += fraction * item.Value;
                fractionalItem = index;
            }

            break;
        }

        return new NodeEvaluation
        {
            Feasible = true,
            Fill = fill,
            FixedWeight = fixedWeight,
            FixedValue = fixedValue,
            Bound = bound,
            FractionalItem = fractionalItem
        };
    }

    // Read profit and weight values from the canonical model
    private static List<Item> ReadItems(CanonicalMatrix model)
    {
        var count =
            model.VariableMap != null
                ? model.VariableMap.Length
                : model.DecisionVariableCount;

        var items = new List<Item>();

        for (var i = 0; i < count; i++)
        {
            var column = GetVariableColumn(model, i);

            // Max objectives are stored negated in row 0
            var value = -model.Grid[0, column];
            var weight = model.Grid[1, column];

            items.Add(new Item
            {
                OriginalIndex = i,
                ColumnIndex = column,
                Value = value,
                Weight = weight,
                Ratio = value / weight
            });
        }

        return items;
    }

    private static int GetVariableColumn(
        CanonicalMatrix model,
        int originalIndex)
    {
        if (model.VariableMap != null &&
            originalIndex < model.VariableMap.Length)
        {
            return model.VariableMap[originalIndex].PositiveColumn;
        }

        return originalIndex;
    }

    // Create the ranking/fill table shown for each node
    private static Tableau CreateNodeTable(
        BranchAndBoundNode node,
        int[] decisions,
        List<Item> rankedItems,
        NodeEvaluation evaluation)
    {
        var grid = new double[rankedItems.Count, 5];
        var rows = new List<string>();

        for (var row = 0; row < rankedItems.Count; row++)
        {
            var item = rankedItems[row];
            var index = item.OriginalIndex;

            rows.Add("x" + (index + 1));

            grid[row, 0] = item.Value;
            grid[row, 1] = item.Weight;
            grid[row, 2] = item.Ratio;
            grid[row, 3] = evaluation.Fill[index];
            grid[row, 4] =
                evaluation.Fill[index] * item.Value;
        }

        var table = new Tableau(
            $"Knapsack Node {node.Label} - {node.BranchDescription}",
            grid,
            Array.Empty<int>(),
            new List<string>
            {
                "value",
                "weight",
                "ratio",
                "fill",
                "contribution"
            });

        table.RowLabels = rows;

        return table;
    }

    private static void AddTable(
        BranchAndBoundNode node,
        SolveResult result,
        Tableau table)
    {
        node.Iterations.Add(table);
        result.Iterations.Add(table);
    }

    // Show when depth-first search returns to the parent
    private static void AddBacktrackTable(
        BranchAndBoundNode parent,
        BranchAndBoundNode child,
        SolveResult result)
    {
        var bound =
            double.IsFinite(child.Bound)
                ? child.Bound
                : 0.0;

        var table = new Tableau(
            $"Backtrack {child.Label} -> {parent.Label}",
            new double[,] { { bound } },
            Array.Empty<int>(),
            new List<string> { "child bound" })
        {
            RowLabels = new List<string> { child.Label },
            Note =
                $"Backtracking from node {child.Label} to node {parent.Label}."
        };

        result.Iterations.Add(table);
    }

    private static string BuildBestCandidateDescription(
        BranchAndBoundNode node,
        List<Item> items,
        CanonicalMatrix model)
    {
        var parts = new List<string>();
        var totalWeight = 0.0;

        for (var i = 0; i < node.VariableValues.Length; i++)
        {
            var value = Math.Round(node.VariableValues[i]);

            parts.Add(
                $"x{i + 1} = {OutputWriter.Format(value)}");

            if (value > 0.5)
                totalWeight += items[i].Weight;
        }

        return
            $"Node {node.Label}: " +
            string.Join(", ", parts) +
            $"; weight = {OutputWriter.Format(totalWeight)}" +
            $"; objective = {OutputWriter.Format(node.Bound)}";
    }

    private sealed class Item
    {
        public int OriginalIndex { get; set; }

        public int ColumnIndex { get; set; }

        public double Value { get; set; }

        public double Weight { get; set; }

        public double Ratio { get; set; }
    }

    private sealed class NodeEvaluation
    {
        public bool Feasible { get; set; }

        public double[] Fill { get; set; }

        public double FixedWeight { get; set; }

        public double FixedValue { get; set; }

        public double Bound { get; set; }

        public int FractionalItem { get; set; }
    }
}
