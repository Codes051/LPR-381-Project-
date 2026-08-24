using Solve.Models;

namespace Solve.Algorithms;

/// <summary>
/// The contract every algorithm implements. The menu discovers solvers through this
/// interface, so adding an algorithm means adding a class and registering it — nothing
/// in the menu, the parser or the output writer needs to change.
/// </summary>
public interface ISolver
{
    /// <summary>Display name shown in the algorithm menu and written to the output file.</summary>
    string Name { get; }

    /// <summary>
    /// True if this solver can handle the given model. Used by the menu to refuse
    /// impossible combinations up front, e.g. plain simplex on a binary knapsack.
    /// </summary>
    bool CanSolve(CanonicalMatrix model);

    /// <summary>
    /// Solve the model, recording every iteration in the result.
    /// Must not mutate the model that was passed in — clone it first.
    /// Throws <see cref="Solve.Exceptions.LpException"/> if the model cannot be solved by this algorithm.
    /// </summary>
    SolveResult Solve(CanonicalMatrix model);
}
