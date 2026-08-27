using System.Globalization;
using Solve.Exceptions;
using Solve.Models;

namespace Solve.Parsing;

// ============================================================================
//  OWNER: Person A
//  Marks: Input File (3), and a share of Error Handling (5)
//  Design reference: LP_Parser.pdf, section 3
// ============================================================================

/// <summary>
/// Reads the input text file and builds a <see cref="ParsedLP"/> holding the problem
/// exactly as written. No slack, surplus or artificial variables are added here - that
/// is the job of the canonicalizer.
/// </summary>
public class LpParser
{
    /// <summary>
    /// Parsing is culture invariant on purpose. On a machine configured for a comma
    /// decimal separator a plain double.Parse would reject "2.5", which would make the
    /// program work for some of us and fail for others on the same input file.
    /// </summary>
    private const NumberStyles CoefficientStyles = NumberStyles.Float;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public ParsedLP ParseFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new LpException("No input file was given.");

        if (!File.Exists(path))
            throw new LpException($"Input file not found: {path}");

        return Parse(File.ReadAllLines(path));
    }

    public ParsedLP Parse(string[] lines)
    {
        if (lines == null)
            throw new LpException("The input file was empty.");

        // Blank lines are ignored, but the original line numbers travel with each line so
        // an error points at the line the user actually sees in their editor.
        var content = new List<KeyValuePair<int, string>>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
                content.Add(new KeyValuePair<int, string>(i + 1, lines[i].Trim()));
        }

        if (content.Count < 3)
        {
            throw new LpException(
                "The input file needs at least three lines: the objective function, " +
                "at least one constraint, and the sign restrictions.");
        }

        var objectiveLine = content[0];
        ProblemType objectiveType;
        bool nonLinear;
        var objectiveCoefficients = ParseObjective(objectiveLine, out objectiveType, out nonLinear);
        var variableCount = objectiveCoefficients.Count;

        // The last line is always the restrictions, so everything between the objective and
        // it is a constraint. This is the peek-ahead rule from the design document, applied
        // to the collected lines rather than to a StreamReader.
        var constraints = new List<Constraint>();
        for (var i = 1; i < content.Count - 1; i++)
            constraints.Add(ParseConstraint(content[i], variableCount));

        var restrictions = ParseRestrictions(content[content.Count - 1], variableCount);

        return new ParsedLP(objectiveType, objectiveCoefficients, constraints, restrictions)
        {
            IsNonLinear = nonLinear
        };
    }

    /// <summary>
    /// Reads the objective line and reports whether it declared a quadratic objective.
    /// </summary>
    /// <remarks>
    /// <c>maxnl</c> and <c>minnl</c> keep the same shape as <c>max</c> and <c>min</c> - one
    /// coefficient per decision variable - but the coefficients weight a separable quadratic,
    /// f(x) = sum of c_j * x_j squared, rather than a linear sum. That is the smallest change
    /// to the file format that lets a non-linear objective be written down at all, which the
    /// bonus criterion needs and the linear format cannot express.
    /// </remarks>
    private static List<double> ParseObjective(
        KeyValuePair<int, string> line, out ProblemType type, out bool nonLinear)
    {
        var lineNumber = line.Key;
        var words = Split(line.Value);

        switch (words[0].ToLowerInvariant())
        {
            case "max": type = ProblemType.Max; nonLinear = false; break;
            case "min": type = ProblemType.Min; nonLinear = false; break;
            case "maxnl": type = ProblemType.Max; nonLinear = true; break;
            case "minnl": type = ProblemType.Min; nonLinear = true; break;
            default:
                throw new ParseException(lineNumber,
                    $"the objective must start with max, min, maxnl or minnl, found \"{words[0]}\".");
        }

        if (words.Length < 2)
            throw new ParseException(lineNumber, "the objective function has no coefficients.");

        var coefficients = new List<double>();
        for (var i = 1; i < words.Length; i++)
            coefficients.Add(ParseNumber(words[i], lineNumber, "objective function coefficient"));

        return coefficients;
    }

    private static Constraint ParseConstraint(KeyValuePair<int, string> line, int variableCount)
    {
        var lineNumber = line.Key;
        var words = Split(line.Value);

        // Read in reverse: the right-hand-side and the relation are always the last two
        // words, so whatever remains has to be one coefficient per decision variable.
        if (words.Length < variableCount + 2)
        {
            throw new ParseException(lineNumber,
                $"expected {variableCount} coefficients followed by a relation and a " +
                $"right-hand-side, found only {words.Length} values." + GluedRelationHint(words));
        }

        var rhs = ParseNumber(words[words.Length - 1], lineNumber, "right-hand-side");
        var relation = ParseRelation(words[words.Length - 2], lineNumber);

        var coefficientCount = words.Length - 2;
        if (coefficientCount != variableCount)
        {
            throw new ParseException(lineNumber,
                $"expected {variableCount} technological coefficients to match the objective " +
                $"function, found {coefficientCount}.");
        }

        var coefficients = new List<double>();
        for (var i = 0; i < coefficientCount; i++)
            coefficients.Add(ParseNumber(words[i], lineNumber, "technological coefficient"));

        return new Constraint(coefficients, relation, rhs);
    }

    private static List<SignRestriction> ParseRestrictions(KeyValuePair<int, string> line, int variableCount)
    {
        var lineNumber = line.Key;
        var words = Split(line.Value);

        if (words.Length != variableCount)
        {
            throw new ParseException(lineNumber,
                $"expected {variableCount} sign restrictions, one per decision variable, found " +
                $"{words.Length}. If this line was meant to be a constraint it is missing its " +
                "relation and right-hand-side.");
        }

        var restrictions = new List<SignRestriction>();
        foreach (var word in words)
        {
            switch (word.ToLowerInvariant())
            {
                case "+": restrictions.Add(SignRestriction.Positive); break;
                case "-": restrictions.Add(SignRestriction.Negative); break;
                case "urs": restrictions.Add(SignRestriction.Unrestricted); break;
                case "int": restrictions.Add(SignRestriction.Integer); break;
                case "bin": restrictions.Add(SignRestriction.Binary); break;
                default:
                    throw new ParseException(lineNumber,
                        $"\"{word}\" is not a valid sign restriction. Use +, -, urs, int or bin.");
            }
        }

        return restrictions;
    }

    private static Relation ParseRelation(string word, int lineNumber)
    {
        switch (word)
        {
            case "<=": return Relation.LEQ;
            case ">=": return Relation.GEQ;
            case "=": return Relation.EQ;
            default:
                throw new ParseException(lineNumber,
                    $"\"{word}\" is not a valid relation. Use <=, >= or =.");
        }
    }

    private static double ParseNumber(string word, int lineNumber, string what)
    {
        // A leading + or - is handled by NumberStyles.Float, so the sign operator and the
        // magnitude never need to be pulled apart.
        double value;
        if (!double.TryParse(word, CoefficientStyles, Invariant, out value))
            throw new ParseException(lineNumber, $"\"{word}\" is not a valid {what}.");

        return value;
    }

    private static string[] Split(string line) =>
        line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// The project brief prints its example with the relation stuck to the right-hand-side
    /// ("&lt;=40"). That is a typo in the brief, but it is the first thing anyone retyping
    /// the example gets wrong, so name it instead of reporting a bare count mismatch.
    /// </summary>
    private static string GluedRelationHint(string[] words)
    {
        foreach (var word in words)
        {
            if ((word.StartsWith("<=") || word.StartsWith(">=")) && word.Length > 2)
            {
                return $" Did you mean to put a space in \"{word}\"? The relation and the " +
                       "right-hand-side must be separate words.";
            }
        }

        return string.Empty;
    }
}
