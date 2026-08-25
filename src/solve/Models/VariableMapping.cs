namespace Solve.Models;

/// <summary>
/// How one variable as the user wrote it maps onto the non-negative columns the simplex
/// actually works with.
/// </summary>
/// <remarks>
/// <para>
/// The simplex requires every variable to be non-negative, but the input file may declare a
/// variable as <c>-</c> (never positive) or <c>urs</c> (unrestricted). Those are handled by
/// substitution during canonicalization, which means the columns in the grid are no longer a
/// one-to-one match for the variables in the model:
/// </para>
/// <list type="bullet">
/// <item><c>+</c>, <c>int</c>, <c>bin</c>: one column, taken as is.</item>
/// <item><c>-</c>: one column holding <c>x' = -x</c>, with every coefficient negated. The
/// reported value is <c>-x'</c>.</item>
/// <item><c>urs</c>: two columns, <c>x = x+ - x-</c>, the second carrying negated
/// coefficients.</item>
/// </list>
/// <para>
/// Without this the substitution would be invisible and a solver would report the value of
/// the stand-in column as though it were the variable itself.
/// </para>
/// </remarks>
public class VariableMapping
{
    public VariableMapping(string name, int positiveColumn, int negativeColumn, double scale)
    {
        Name = name;
        PositiveColumn = positiveColumn;
        NegativeColumn = negativeColumn;
        Scale = scale;
    }

    /// <summary>The variable as the user knows it: x1, x2 and so on.</summary>
    public string Name { get; }

    /// <summary>Column holding the variable, or its positive part when it was split.</summary>
    public int PositiveColumn { get; }

    /// <summary>Column holding the negative part of an unrestricted variable, or -1.</summary>
    public int NegativeColumn { get; }

    /// <summary>-1 for a variable restricted to be non-positive, +1 otherwise.</summary>
    public double Scale { get; }

    /// <summary>True when this variable needed no substitution at all.</summary>
    public bool IsDirect => NegativeColumn < 0 && Scale > 0;
}
