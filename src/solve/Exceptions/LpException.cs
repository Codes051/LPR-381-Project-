namespace Solve.Exceptions;

/// <summary>
/// Thrown for anything the user did wrong: a malformed input file, an algorithm that
/// cannot handle the model that was chosen, a sensitivity request that does not apply.
/// The menu catches this and prints the message instead of showing a stack trace.
/// Error handling is a marked criterion — never let one of these escape to a crash.
/// </summary>
public class LpException : Exception
{
    public LpException(string message) : base(message) { }

    public LpException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>A problem with the input file specifically. Includes the line number when known.</summary>
public class ParseException : LpException
{
    public ParseException(int lineNumber, string message)
        : base($"Input file error on line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    public int LineNumber { get; }
}
