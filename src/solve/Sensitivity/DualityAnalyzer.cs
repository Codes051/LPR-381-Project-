using Solve.Models;

namespace Solve.Sensitivity;

// ============================================================================
//  OWNER: Person C  (part of the Sensitivity Analysis 25 marks)
//
//  Three marked sub-criteria:
//    - apply duality to the programming model
//    - solve the dual programming model
//    - verify whether the model has strong or weak duality
//
//  Works on ParsedLP, not on the canonical matrix - the dual is built from the
//  model as written, then canonicalized and solved like any other problem.
//  Coordinate with Person A on the output format so the dual tableau prints the
//  same way the primal one does.
// ============================================================================

public class DualityAnalyzer
{
    /// <summary>Build the dual of the given model. Max becomes Min, rows become columns.</summary>
    public ParsedLP BuildDual(ParsedLP primal) =>
        throw new NotImplementedException("BuildDual - Person C");

    /// <summary>Canonicalize and solve the dual using the primal simplex.</summary>
    public SolveResult SolveDual(ParsedLP primal) =>
        throw new NotImplementedException("SolveDual - Person C");

    /// <summary>
    /// Compare the primal and dual objective values. Equal means strong duality;
    /// a gap means weak duality. Round before comparing - the values come from
    /// floating point pivots and will not match exactly.
    /// </summary>
    public string VerifyDuality(SolveResult primalResult, SolveResult dualResult) =>
        throw new NotImplementedException("VerifyDuality - Person C");
}
