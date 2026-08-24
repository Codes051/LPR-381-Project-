using Solve.Models;

namespace Solve.Algorithms.BranchAndBound;

// ============================================================================
//  OWNER: Person B
//  Marks: Branch and Bound Simplex Algorithm (20) - the largest algorithm on the sheet.
//
//  Criteria, all four of them explicitly marked:
//    - backtracking must be implemented
//    - all possible sub-problems must be created
//    - all possible nodes must be fathomed
//    - all table iterations of those sub-problems must be displayed
//    - the best candidate must be displayed
//
//  Returning the right answer without printing the whole tree throws away most
//  of these marks.
// ============================================================================

public class BranchAndBoundSimplexSolver : ISolver
{
    public string Name => "Branch and Bound Simplex Algorithm";

    public bool CanSolve(CanonicalMatrix model) => model.IsIntegerProblem;

    public SolveResult Solve(CanonicalMatrix model)
    {
        // TODO (B): depth-first branch and bound over the LP relaxation.
        //   Root: solve the relaxation with PrimalSimplexSolver on a clone of the model.
        //   Branch: pick a column flagged in IsIntegerMask whose value is fractional,
        //     create two children with x <= floor(v) and x >= ceil(v) as extra rows.
        //   Explore depth first and BACKTRACK explicitly - the backtracking is marked,
        //     so make it visible in the output, not just implicit in a recursive call.
        //   Fathom every node and record why (infeasible / bounded out / integer feasible).
        //   Track the incumbent; at the end set BestCandidateDescription on the result.
        //   Flatten every node into result.Iterations in visit order, each tableau titled
        //     with the node label and branch condition, so the output file shows the tree.

        throw new NotImplementedException("BranchAndBoundSimplexSolver.Solve - Person B");
    }
}
