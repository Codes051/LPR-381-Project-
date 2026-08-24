using Solve.Models;

namespace Solve.Algorithms.Simplex;

// ============================================================================
//  OWNER: Person A
//  Marks: Primal Simplex Algorithm (4)
//  Criteria: display the canonical form and solve, showing ALL tableau iterations.
// ============================================================================

public class PrimalSimplexSolver : ISolver
{
    public string Name => "Primal Simplex Algorithm";

    public bool CanSolve(CanonicalMatrix model)
    {
        // Plain simplex solves the relaxation only. The menu should refuse an integer or
        // binary model here and say so - that refusal is part of the Error Handling marks.
        return !model.IsIntegerProblem;
    }

    public SolveResult Solve(CanonicalMatrix model)
    {
        // TODO (A): standard tableau simplex.
        //   Snapshot the canonical form as iteration 0, then loop:
        //     pick the entering column, ratio test for the leaving row, pivot,
        //     snapshot a Tableau after every pivot with its pivot row/column recorded.
        //   Detect Unbounded (no positive ratio in the entering column) and Infeasible
        //   (an artificial variable still basic at a non-zero level) and set Status.
        //   Flip ObjectiveValue back if OriginalObjectiveType is Min.
        //   Populate FinalTableau - sensitivity analysis cannot run without it.

        throw new NotImplementedException("PrimalSimplexSolver.Solve - Person A");
    }
}
