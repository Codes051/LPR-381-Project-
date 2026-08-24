namespace Solve.Models;

/// <summary>
/// One constraint of the model. Coefficients, relation, right-hand side and any variables
/// added for this row all live together, so building the matrix row later only needs this object.
/// </summary>
public class Constraint
{
    public Constraint(List<double> coefficients, Relation relationalOperator, double rhs)
    {
        Coefficients = coefficients;
        RelationalOperator = relationalOperator;
        RHS = rhs;
        GeneratedVariables = new List<AddedVariable>();
    }

    /// <summary>Technological coefficients, in the same order as the objective function.</summary>
    public List<double> Coefficients { get; }

    public Relation RelationalOperator { get; }

    public double RHS { get; }

    /// <summary>
    /// Not set by the constructor. Empty just after parsing; filled during canonicalization.
    /// </summary>
    public List<AddedVariable> GeneratedVariables { get; }
}
