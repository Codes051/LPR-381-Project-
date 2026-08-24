using System.Text;
using Solve.Models;

namespace Solve.Output;

// ============================================================================
//  OWNER: Person A
//  Marks: Output File (2)
//  SHARED UTILITY - every workstream writes through this class. Do not print
//  tableaus anywhere else, or the output file ends up inconsistent.
// ============================================================================

/// <summary>
/// Writes the canonical form and every iteration of the chosen algorithm to the output
/// text file. All decimal values are rounded to three places, as the brief requires.
/// </summary>
public class OutputWriter
{
    /// <summary>The rounding the brief mandates. Use this everywhere, never a raw ToString().</summary>
    public const string NumberFormat = "0.000";

    private readonly StringBuilder _buffer = new StringBuilder();

    public static string Format(double value) => value.ToString(NumberFormat);

    public void WriteLine(string text = "") => _buffer.AppendLine(text);

    public void WriteHeading(string text)
    {
        // TODO (A): heading with a rule under it, so the output file reads well on video.
        throw new NotImplementedException("OutputWriter.WriteHeading - Person A");
    }

    /// <summary>Prints the canonical form: column labels, objective row, constraint rows, RHS.</summary>
    public void WriteCanonicalForm(CanonicalMatrix model)
    {
        // TODO (A): column-aligned grid, labels across the top, basic variable per row,
        //   every value through Format().
        throw new NotImplementedException("OutputWriter.WriteCanonicalForm - Person A");
    }

    /// <summary>Prints one captured iteration, including its note and pivot markers.</summary>
    public void WriteTableau(Tableau tableau)
    {
        // TODO (A): same layout as WriteCanonicalForm, plus the title, the pivot row and
        //   column markers, and the note underneath.
        throw new NotImplementedException("OutputWriter.WriteTableau - Person A");
    }

    /// <summary>Prints a full solve: canonical form, every iteration, then the answer.</summary>
    public void WriteResult(SolveResult result)
    {
        // TODO (A): algorithm name, canonical form, all iterations in order, final status.
        //   For Infeasible / Unbounded print the Message instead of a solution.
        //   For branch and bound also print BestCandidateDescription.
        throw new NotImplementedException("OutputWriter.WriteResult - Person A");
    }

    public void Save(string path) => File.WriteAllText(path, _buffer.ToString());

    public override string ToString() => _buffer.ToString();
}
