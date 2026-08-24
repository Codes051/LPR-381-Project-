using Solve.Exceptions;
using Solve.Models;

namespace Solve.Parsing;

// ============================================================================
//  OWNER: Person A
//  Marks: Input File (3)
//  Design reference: LP_Parser.pdf, section 3
// ============================================================================

/// <summary>
/// Reads the input text file and builds a <see cref="ParsedLP"/> holding the problem
/// exactly as written. No slack, surplus or artificial variables are added here - that
/// is the job of the canonicalizer.
/// </summary>
public class LpParser
{
    public ParsedLP ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new LpException($"Input file not found: {path}");

        return Parse(File.ReadAllLines(path));
    }

    public ParsedLP Parse(string[] lines)
    {
        // TODO (A): Objective function - first line.
        //   word[0] is "max" or "min", anything else is an error.
        //   Remaining words are signed coefficients (+2, -3, ...). A standard signed
        //   parse handles the +/- automatically; a failed parse is an error.
        //   The number of coefficients is numDecisionVars, and drives the rest of the parse.
        //
        // TODO (A): Constraints - one per line, until the restrictions line.
        //   Read each line in reverse: last word is the RHS, second-to-last is the relation
        //   (<=, >=, =, else error). What remains must be exactly numDecisionVars
        //   coefficients, read left to right. Zero is a valid coefficient. A wrong count
        //   is an error.
        //   Note: the brief prints the example as "+10 +10 <=40" with no space before the
        //   RHS, but that is a typo - the relation and the RHS are always separate words.
        //
        // TODO (A): Restrictions - the final line, identified by peeking ahead.
        //   If there are no lines left after the current one, it is the restrictions line
        //   and not a constraint. One word per decision variable, in objective function
        //   order. Accept +, -, urs, int, bin; anything else is an error.
        //
        // TODO (A): Every failure path above must throw ParseException with the line number.
        //   Malformed input is explicitly part of the Error Handling marks.

        throw new NotImplementedException("LpParser.Parse - Person A");
    }
}
