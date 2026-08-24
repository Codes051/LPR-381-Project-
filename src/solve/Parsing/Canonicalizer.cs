using Solve.Models;

namespace Solve.Parsing;

// ============================================================================
//  OWNER: Person A
//  Design reference: LP_Parser.pdf, section 4
// ============================================================================

/// <summary>
/// Takes a <see cref="ParsedLP"/> and builds the <see cref="CanonicalMatrix"/> that every
/// solver consumes, adding the slack, surplus and artificial variables the matrix form needs.
/// </summary>
public class Canonicalizer
{
    public CanonicalMatrix ToCanonicalForm(ParsedLP model)
    {
        // TODO (A): Pass 1 - assign added variables, walking constraints in order.
        //   <=  ->  one Slack,     coefficient +1.0
        //   >=  ->  one Surplus (-1.0) and one Artificial (+1.0)
        //   =   ->  one Artificial, coefficient +1.0
        //   Use ONE column counter shared across the whole problem, not one per constraint:
        //   assign the current value then increment. That guarantees no collisions and keeps
        //   the two variables of a >= row on consecutive columns.
        //   Store the column index on the AddedVariable itself - this pass only decides
        //   which column a variable belongs to; the grid is written in a later pass, by
        //   which point the index would otherwise be lost.
        //
        // TODO (A): Allocate the grid. Rows = numConstraints + 1, columns = totalColumns + 1.
        //   Cannot be sized until pass 1 has walked every constraint.
        //
        // TODO (A): Objective row (row 0). Flip the sign of each coefficient for standard
        //   form; if the problem is a Min, flip again to normalise it to a Max internally.
        //   Record OriginalObjectiveType so the caller can flip the final answer back.
        //
        // TODO (A): Constraint rows (1..M). Original coefficients into their original
        //   columns; each added variable into its stored column with its stored coefficient;
        //   RHS into the last column. The basic variable for the row is the Slack or
        //   Artificial column, never the Surplus - a surplus carries -1.0 and cannot seed
        //   an identity basis.
        //
        // TODO (A): Flags and labels. For each decision-variable column, IsIntegerMask is
        //   true for Integer or Binary, IsBinaryMask true only for Binary. Added-variable
        //   columns are false for both. ColumnLabels gets a readable name per column:
        //   x1, x2, ... for decisions, s1 / e2 / a3 style for slack / surplus / artificial.

        throw new NotImplementedException("Canonicalizer.ToCanonicalForm - Person A");
    }
}
