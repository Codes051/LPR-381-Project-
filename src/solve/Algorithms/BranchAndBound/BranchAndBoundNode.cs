using Solve.Models;

namespace Solve.Algorithms.BranchAndBound;

// ============================================================================
//  OWNER: Person B
//  Shared between BOTH branch and bound algorithms (36 marks combined).
//  Build the tree machinery once here, then plug in a different bounding rule
//  for the simplex version and the knapsack version.
// ============================================================================

/// <summary>Why a node stopped being explored. The brief requires every node to be fathomed.</summary>
public enum FathomReason
{
    /// <summary>Still being explored.</summary>
    None,
    /// <summary>Relaxation was infeasible.</summary>
    Infeasible,
    /// <summary>Bound was no better than the incumbent.</summary>
    BoundedOut,
    /// <summary>Relaxation was already integer-feasible - a candidate.</summary>
    IntegerFeasible
}

/// <summary>
/// One sub-problem in the branch and bound tree.
/// </summary>
public class BranchAndBoundNode
{
    public BranchAndBoundNode(string label, CanonicalMatrix model, BranchAndBoundNode parent = null)
    {
        Label = label;
        Model = model;
        Parent = parent;
        Children = new List<BranchAndBoundNode>();
    }

    /// <summary>Hierarchical label used in the output: "1", "1.1", "1.2.1" and so on.</summary>
    public string Label { get; }

    /// <summary>The sub-problem model, with this branch bound applied. Always a clone of the parent.</summary>
    public CanonicalMatrix Model { get; }

    public BranchAndBoundNode Parent { get; }

    public List<BranchAndBoundNode> Children { get; }

    /// <summary>Objective value of this relaxation, used as the bound.</summary>
    public double Bound { get; set; }

    public SolutionStatus Status { get; set; }

    public FathomReason Fathomed { get; set; } = FathomReason.None;

    /// <summary>Human-readable branch condition, e.g. "x2 &lt;= 1". Printed above the sub-problem tableau.</summary>
    public string BranchDescription { get; set; }

    public double[] VariableValues { get; set; }

    /// <summary>Every tableau produced while solving this sub-problem. All of these go in the output file.</summary>
    public List<Tableau> Iterations { get; } = new List<Tableau>();
}
