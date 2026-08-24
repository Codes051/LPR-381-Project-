using Solve.Models;

namespace Solve.Sensitivity;

// ============================================================================
//  OWNER: Person C
//  Marks: Sensitivity Analysis (25) - the single largest item on the mark sheet,
//  worth more than both simplex algorithms, the input file, the output file,
//  error handling and the interface combined.
//
//  Every method here reads SolveResult.FinalTableau. It is only valid when the
//  model solved to Optimal; throw LpException with a clear message otherwise.
//
//  Eleven operations below plus duality in DualityAnalyzer.cs make up the 25.
// ============================================================================

/// <summary>A closed range over which something may vary without changing the basis.</summary>
public class SensitivityRange
{
    public SensitivityRange(string subject, double lower, double upper, double current)
    {
        Subject = subject;
        Lower = lower;
        Upper = upper;
        Current = current;
    }

    public string Subject { get; }
    public double Lower { get; }
    public double Upper { get; }
    public double Current { get; }
}

public class SensitivityAnalyzer
{
    private readonly CanonicalMatrix _optimal;

    public SensitivityAnalyzer(SolveResult result)
    {
        // TODO (C): reject a null or non-optimal result here with a readable LpException
        //   rather than letting a NullReferenceException reach the user.
        _optimal = result?.FinalTableau;
    }

    // --- Non-basic variables -------------------------------------------------

    /// <summary>Display the range of a selected Non-Basic Variable.</summary>
    public SensitivityRange RangeOfNonBasicVariable(int column) =>
        throw new NotImplementedException("RangeOfNonBasicVariable - Person C");

    /// <summary>Apply and display a change of a selected Non-Basic Variable.</summary>
    public SolveResult ChangeNonBasicVariable(int column, double newValue) =>
        throw new NotImplementedException("ChangeNonBasicVariable - Person C");

    // --- Basic variables -----------------------------------------------------

    /// <summary>Display the range of a selected Basic Variable.</summary>
    public SensitivityRange RangeOfBasicVariable(int column) =>
        throw new NotImplementedException("RangeOfBasicVariable - Person C");

    /// <summary>Apply and display a change of a selected Basic Variable.</summary>
    public SolveResult ChangeBasicVariable(int column, double newValue) =>
        throw new NotImplementedException("ChangeBasicVariable - Person C");

    // --- Right-hand sides ----------------------------------------------------

    /// <summary>Display the range of a selected constraint right-hand-side value.</summary>
    public SensitivityRange RangeOfRhs(int constraintRow) =>
        throw new NotImplementedException("RangeOfRhs - Person C");

    /// <summary>Apply and display a change of a selected constraint right-hand-side value.</summary>
    public SolveResult ChangeRhs(int constraintRow, double newValue) =>
        throw new NotImplementedException("ChangeRhs - Person C");

    // --- Coefficients inside a non-basic column ------------------------------

    /// <summary>Display the range of a selected variable in a Non-Basic Variable column.</summary>
    public SensitivityRange RangeOfCoefficientInNonBasicColumn(int column, int constraintRow) =>
        throw new NotImplementedException("RangeOfCoefficientInNonBasicColumn - Person C");

    /// <summary>Apply and display a change of a selected variable in a Non-Basic Variable column.</summary>
    public SolveResult ChangeCoefficientInNonBasicColumn(int column, int constraintRow, double newValue) =>
        throw new NotImplementedException("ChangeCoefficientInNonBasicColumn - Person C");

    // --- Structural additions ------------------------------------------------

    /// <summary>Add a new activity (a new decision variable column) to an optimal solution.</summary>
    public SolveResult AddActivity(double objectiveCoefficient, double[] constraintCoefficients) =>
        throw new NotImplementedException("AddActivity - Person C");

    /// <summary>Add a new constraint to an optimal solution and re-optimise.</summary>
    public SolveResult AddConstraint(double[] coefficients, Relation relation, double rhs) =>
        throw new NotImplementedException("AddConstraint - Person C");

    // --- Shadow prices -------------------------------------------------------

    /// <summary>Display the shadow prices, one per constraint.</summary>
    public double[] ShadowPrices() =>
        throw new NotImplementedException("ShadowPrices - Person C");
}
