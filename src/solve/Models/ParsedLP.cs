namespace Solve.Models;

/// <summary>
/// The model exactly as written in the input file, before any slack, surplus or artificial
/// variables are added. This is what the parser produces and what the duality transform reads.
/// </summary>
public class ParsedLP
{
    public ParsedLP(
        ProblemType objectiveType,
        List<double> objectiveCoefficients,
        List<Constraint> constraints,
        List<SignRestriction> restrictions)
    {
        ObjectiveType = objectiveType;
        ObjectiveCoefficients = objectiveCoefficients;
        Constraints = constraints;
        Restrictions = restrictions;
    }

    public ProblemType ObjectiveType { get; }

    public List<double> ObjectiveCoefficients { get; }

    public List<Constraint> Constraints { get; }

    /// <summary>One entry per decision variable, in objective function order.</summary>
    public List<SignRestriction> Restrictions { get; }

    public int DecisionVariableCount => ObjectiveCoefficients.Count;
}
