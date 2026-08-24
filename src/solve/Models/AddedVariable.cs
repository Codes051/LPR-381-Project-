namespace Solve.Models;

/// <summary>
/// A variable added to a constraint because of its relational operator: a slack for &lt;=,
/// a surplus and an artificial for &gt;=, an artificial for =.
/// Created by the <see cref="Solve.Parsing.Canonicalizer"/>, never by the parser.
/// </summary>
public class AddedVariable
{
    public AddedVariable(VariableType type, double coefficient, int columnIndex)
    {
        Type = type;
        Coefficient = coefficient;
        ColumnIndex = columnIndex;
    }

    public VariableType Type { get; }

    /// <summary>+1.0 for slack and artificial, -1.0 for surplus.</summary>
    public double Coefficient { get; }

    /// <summary>Which column of the canonical grid this variable occupies.</summary>
    public int ColumnIndex { get; }
}
