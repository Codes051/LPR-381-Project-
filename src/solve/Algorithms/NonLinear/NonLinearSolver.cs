using Solve.Models;

namespace Solve.Algorithms.NonLinear;

// ============================================================================
//  OWNER: Person C
//  Marks: Non-linear problem solved (+10 BONUS, on top of the 100)
//
//  Do this LAST. The brief is explicit that this is the one part where the code
//  itself has to be explained on video - everything else is marked on the
//  program working, not on the source.
// ============================================================================

public class NonLinearSolver : ISolver
{
    public string Name => "Non-linear Solver (bonus)";

    public bool CanSolve(CanonicalMatrix model) => false; // TODO (C): detect a non-linear model

    public SolveResult Solve(CanonicalMatrix model)
    {
        // TODO (C): solve a non-linear objective such as f(x) = x^2.
        //   Be ready to explain this code on camera - that is a stated requirement.
        throw new NotImplementedException("NonLinearSolver.Solve - Person C");
    }
}
