namespace Solve.Models;

/// <summary>
/// What every algorithm hands back. This is the integration contract between the three
/// workstreams: the output writer prints it, and sensitivity analysis reads FinalTableau.
/// Do not change the shape of this class without telling the whole group.
/// </summary>
public class SolveResult
{
    public SolveResult(string algorithmName)
    {
        AlgorithmName = algorithmName;
        Iterations = new List<Tableau>();
        VariableValues = Array.Empty<double>();
    }

    /// <summary>Which algorithm produced this, for the output file header.</summary>
    public string AlgorithmName { get; }

    public SolutionStatus Status { get; set; } = SolutionStatus.Optimal;

    /// <summary>Already flipped back to the original sense for Min problems.</summary>
    public double ObjectiveValue { get; set; }

    /// <summary>Values of the decision variables, in objective function order.</summary>
    public double[] VariableValues { get; set; }

    /// <summary>Every tableau / product form step, in the order it was produced.</summary>
    public List<Tableau> Iterations { get; }

    /// <summary>
    /// The optimal tableau. Sensitivity analysis operates on this, so it must be populated
    /// whenever Status is Optimal. Null for infeasible or unbounded models.
    /// </summary>
    public CanonicalMatrix FinalTableau { get; set; }

    /// <summary>Free-text explanation shown to the user, e.g. why a model was declared infeasible.</summary>
    public string Message { get; set; }

    /// <summary>Set by branch and bound: the best integer-feasible candidate found.</summary>
    public string BestCandidateDescription { get; set; }
}
