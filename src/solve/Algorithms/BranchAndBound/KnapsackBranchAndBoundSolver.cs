using Solve.Models;

namespace Solve.Algorithms.BranchAndBound;

// ============================================================================
//  OWNER: Person B
//  Marks: Branch and Bound Knapsack Algorithm (16)
//
//  Same four marked criteria as the simplex version: backtracking, all
//  sub-problems, all nodes fathomed, all table iterations, best candidate.
//
//  Needs nothing from Person A - it reads IsBinaryMask and works directly on the
//  ratios. START HERE on day one rather than waiting for the parser; feed it a
//  hand-built CanonicalMatrix until the parser lands.
//
//  This is the algorithm the example file in the brief is built for, so it will
//  be the centrepiece of the demo video.
// ============================================================================

public class KnapsackBranchAndBoundSolver : ISolver
{
    public string Name => "Branch and Bound Knapsack Algorithm";

    public bool CanSolve(CanonicalMatrix model)
    {
        // A knapsack is a single <= constraint over binary variables.
        return model.ConstraintCount == 1 && model.IsBinaryMask.Any(f => f);
    }

    public SolveResult Solve(CanonicalMatrix model)
    {
        // TODO (B): knapsack branch and bound.
        //   Rank the items by objective coefficient / constraint coefficient (value per unit
        //     of weight) and remember the original variable order for reporting.
        //   Bound each node by filling greedily down the ranking and taking a fractional
        //     part of the first item that does not fit.
        //   Branch on the first fractional item: one child takes it (x = 1), one leaves it
        //     (x = 0). Explore depth first with explicit backtracking.
        //   Fathom on: weight exceeded (infeasible), bound no better than the incumbent,
        //     or an all-integer fill (candidate).
        //   Record a table per node showing the ranking, the fill and the bound, and set
        //     BestCandidateDescription to the winning selection.

        throw new NotImplementedException("KnapsackBranchAndBoundSolver.Solve - Person B");
    }
}
