namespace Solve.Models;

/// <summary>Whether the objective function is being maximised or minimised.</summary>
public enum ProblemType
{
    Max,
    Min
}

/// <summary>The relational operator of a constraint.</summary>
public enum Relation
{
    /// <summary>&lt;=</summary>
    LEQ,
    /// <summary>&gt;=</summary>
    GEQ,
    /// <summary>=</summary>
    EQ
}

/// <summary>The sign restriction placed on a decision variable (last line of the input file).</summary>
public enum SignRestriction
{
    /// <summary>+</summary>
    Positive,
    /// <summary>-</summary>
    Negative,
    /// <summary>urs</summary>
    Unrestricted,
    /// <summary>int</summary>
    Integer,
    /// <summary>bin</summary>
    Binary
}

/// <summary>What role a column of the canonical matrix plays.</summary>
public enum VariableType
{
    Decision,
    Slack,
    Surplus,
    Artificial
}

/// <summary>How a solve attempt ended. Every solver must report one of these.</summary>
public enum SolutionStatus
{
    Optimal,
    Infeasible,
    Unbounded
}
