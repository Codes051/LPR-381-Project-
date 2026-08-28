using System.Globalization;
using Solve.Exceptions;
using Solve.Models;
using Solve.Sensitivity;
using Solve.Output;
using System;

namespace Solve.UI;

/// <summary>
/// One menu entry per marked sensitivity operation. Keep them in this order on
/// video so the marker can tick them off against the brief.
/// </summary>
public class SensitivityMenu
{
    private readonly ParsedLP _parsed;
    private readonly SolveResult _result;
    private readonly SensitivityAnalyzer _analyzer;
    private readonly DualityAnalyzer _duality;

    public SensitivityMenu(ParsedLP parsed, SolveResult result)
    {
        _parsed = parsed;
        _result = result;
        // The model is passed through so the "apply a change" operations can rebuild it with
        // the change made and solve it again, rather than patching the optimal tableau.
        _analyzer = new SensitivityAnalyzer(result, parsed);
        _duality = new DualityAnalyzer();
    }

    public void Run()
    {
        if (_result == null || _result.Status != SolutionStatus.Optimal)
            throw new LpException("Sensitivity analysis needs a model that solved to optimality.");

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("--- Sensitivity Analysis ---");
            Console.WriteLine(" 1. Range of a non-basic variable");
            Console.WriteLine(" 2. Change a non-basic variable");
            Console.WriteLine(" 3. Range of a basic variable");
            Console.WriteLine(" 4. Change a basic variable");
            Console.WriteLine(" 5. Range of a constraint right-hand side");
            Console.WriteLine(" 6. Change a constraint right-hand side");
            Console.WriteLine(" 7. Range of a variable in a non-basic column");
            Console.WriteLine(" 8. Change a variable in a non-basic column");
            Console.WriteLine(" 9. Add a new activity");
            Console.WriteLine("10. Add a new constraint");
            Console.WriteLine("11. Display shadow prices");
            Console.WriteLine("12. Duality (build, solve, verify strong or weak)");
            Console.WriteLine(" 0. Back");
            Console.Write("Select: ");

            var choice = Console.ReadLine();
            if (choice == "0") return;

            try
            {
                switch (choice)
                {
                    case "1":
                        HandleRangeOfNonBasicVariable();
                        break;
                    case "2":
                        HandleChangeNonBasicVariable();
                        break;
                    case "3":
                        HandleRangeOfBasicVariable();
                        break;
                    case "4":
                        HandleChangeBasicVariable();
                        break;
                    case "5":
                        HandleRangeOfRhs();
                        break;
                    case "6":
                        HandleChangeRhs();
                        break;
                    case "7":
                        HandleRangeOfCoefficientInNonBasicColumn();
                        break;
                    case "8":
                        HandleChangeCoefficientInNonBasicColumn();
                        break;
                    case "9":
                        HandleAddActivity();
                        break;
                    case "10":
                        HandleAddConstraint();
                        break;
                    case "11":
                        HandleShadowPrices();
                        break;
                    case "12":
                        HandleDuality();
                        break;
                    default:
                        Console.WriteLine("Not a valid option.");
                        break;
                }
            }
            catch (LpException ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"\nNot built yet: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nUnexpected error: {ex.Message}");
            }
        }
    }


    /// <summary>
    /// Reads a number typed at a prompt, accepting either decimal separator.
    /// </summary>
    /// <remarks>
    /// Every number this program prints uses a point, because the tableaus and the input files
    /// do. Parsing with the machine culture alone rejects "2.5" on a machine configured for a
    /// comma decimal separator, so the user reads 2.500 on screen, types it back, and is told
    /// it is invalid. Invariant is tried first, then the local culture, so both are accepted.
    /// </remarks>
    private static bool TryReadNumber(string text, out double value)
    {
        text = (text ?? string.Empty).Trim();

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private void HandleRangeOfNonBasicVariable()
    {
        Console.Write("Enter column index of non-basic variable: ");
        if (!int.TryParse(Console.ReadLine(), out int column))
        {
            Console.WriteLine("Invalid column index.");
            return;
        }

        var range = _analyzer.RangeOfNonBasicVariable(column);
        DisplayRange(range);
    }

    private void HandleChangeNonBasicVariable()
    {
        Console.Write("Enter column index of non-basic variable: ");
        if (!int.TryParse(Console.ReadLine(), out int column))
        {
            Console.WriteLine("Invalid column index.");
            return;
        }

        Console.Write("Enter new value: ");
        if (!TryReadNumber(Console.ReadLine(), out double newValue))
        {
            Console.WriteLine("Invalid value.");
            return;
        }

        var result = _analyzer.ChangeNonBasicVariable(column, newValue);
        DisplaySolveResult(result);
    }

    private void HandleRangeOfBasicVariable()
    {
        Console.Write("Enter column index of basic variable: ");
        if (!int.TryParse(Console.ReadLine(), out int column))
        {
            Console.WriteLine("Invalid column index.");
            return;
        }

        var range = _analyzer.RangeOfBasicVariable(column);
        DisplayRange(range);
    }

    private void HandleChangeBasicVariable()
    {
        Console.Write("Enter column index of basic variable: ");
        if (!int.TryParse(Console.ReadLine(), out int column))
        {
            Console.WriteLine("Invalid column index.");
            return;
        }

        Console.Write("Enter new value: ");
        if (!TryReadNumber(Console.ReadLine(), out double newValue))
        {
            Console.WriteLine("Invalid value.");
            return;
        }

        var result = _analyzer.ChangeBasicVariable(column, newValue);
        DisplaySolveResult(result);
    }

    private void HandleRangeOfRhs()
    {
        // Numbered from 1, matching how the shadow price display and the tableau row labels
        // name constraints. Asking for a 0-based index here while printing 1-based labels
        // everywhere else is how someone changes the wrong constraint on camera.
        Console.Write("Enter constraint number (1-based): ");
        if (!int.TryParse(Console.ReadLine(), out int constraintNumber))
        {
            Console.WriteLine("Invalid constraint number.");
            return;
        }

        int constraintRow = constraintNumber - 1;

        var range = _analyzer.RangeOfRhs(constraintRow);
        DisplayRange(range);
    }

    private void HandleChangeRhs()
    {
        // Numbered from 1, matching how the shadow price display and the tableau row labels
        // name constraints. Asking for a 0-based index here while printing 1-based labels
        // everywhere else is how someone changes the wrong constraint on camera.
        Console.Write("Enter constraint number (1-based): ");
        if (!int.TryParse(Console.ReadLine(), out int constraintNumber))
        {
            Console.WriteLine("Invalid constraint number.");
            return;
        }

        int constraintRow = constraintNumber - 1;

        Console.Write("Enter new RHS value: ");
        if (!TryReadNumber(Console.ReadLine(), out double newValue))
        {
            Console.WriteLine("Invalid value.");
            return;
        }

        var result = _analyzer.ChangeRhs(constraintRow, newValue);
        DisplaySolveResult(result);
    }

    private void HandleRangeOfCoefficientInNonBasicColumn()
    {
        Console.Write("Enter column index of non-basic variable: ");
        if (!int.TryParse(Console.ReadLine(), out int column))
        {
            Console.WriteLine("Invalid column index.");
            return;
        }

        // Numbered from 1, matching how the shadow price display and the tableau row labels
        // name constraints. Asking for a 0-based index here while printing 1-based labels
        // everywhere else is how someone changes the wrong constraint on camera.
        Console.Write("Enter constraint number (1-based): ");
        if (!int.TryParse(Console.ReadLine(), out int constraintNumber))
        {
            Console.WriteLine("Invalid constraint number.");
            return;
        }

        int constraintRow = constraintNumber - 1;

        var range = _analyzer.RangeOfCoefficientInNonBasicColumn(column, constraintRow);
        DisplayRange(range);
    }

    private void HandleChangeCoefficientInNonBasicColumn()
    {
        Console.Write("Enter column index of non-basic variable: ");
        if (!int.TryParse(Console.ReadLine(), out int column))
        {
            Console.WriteLine("Invalid column index.");
            return;
        }

        // Numbered from 1, matching how the shadow price display and the tableau row labels
        // name constraints. Asking for a 0-based index here while printing 1-based labels
        // everywhere else is how someone changes the wrong constraint on camera.
        Console.Write("Enter constraint number (1-based): ");
        if (!int.TryParse(Console.ReadLine(), out int constraintNumber))
        {
            Console.WriteLine("Invalid constraint number.");
            return;
        }

        int constraintRow = constraintNumber - 1;

        Console.Write("Enter new coefficient value: ");
        if (!TryReadNumber(Console.ReadLine(), out double newValue))
        {
            Console.WriteLine("Invalid value.");
            return;
        }

        var result = _analyzer.ChangeCoefficientInNonBasicColumn(column, constraintRow, newValue);
        DisplaySolveResult(result);
    }

    private void HandleAddActivity()
    {
        if (_parsed == null)
        {
            Console.WriteLine("No parsed model available for structural changes.");
            return;
        }

        Console.Write("Enter objective coefficient for new variable: ");
        if (!TryReadNumber(Console.ReadLine(), out double objCoeff))
        {
            Console.WriteLine("Invalid coefficient.");
            return;
        }

        int numConstraints = _parsed.Constraints.Count;
        var constraintCoeffs = new double[numConstraints];

        for (int i = 0; i < numConstraints; i++)
        {
            Console.Write($"Enter coefficient for constraint {i + 1}: ");
            if (!TryReadNumber(Console.ReadLine(), out constraintCoeffs[i]))
            {
                Console.WriteLine("Invalid coefficient.");
                return;
            }
        }

        var result = _analyzer.AddActivity(objCoeff, constraintCoeffs);
        DisplaySolveResult(result);
    }

    private void HandleAddConstraint()
    {
        if (_parsed == null)
        {
            Console.WriteLine("No parsed model available for structural changes.");
            return;
        }

        int numVars = _parsed.DecisionVariableCount;
        var coeffs = new double[numVars];

        for (int i = 0; i < numVars; i++)
        {
            Console.Write($"Enter coefficient for variable {i + 1}: ");
            if (!TryReadNumber(Console.ReadLine(), out coeffs[i]))
            {
                Console.WriteLine("Invalid coefficient.");
                return;
            }
        }

        Console.Write("Enter relation (<=, >=, =): ");
        string relationStr = Console.ReadLine()?.Trim() ?? "";
        Relation relation;
        switch (relationStr)
        {
            case "<=": relation = Relation.LEQ; break;
            case ">=": relation = Relation.GEQ; break;
            case "=": relation = Relation.EQ; break;
            default:
                Console.WriteLine("Invalid relation.");
                return;
        }

        Console.Write("Enter RHS value: ");
        if (!TryReadNumber(Console.ReadLine(), out double rhs))
        {
            Console.WriteLine("Invalid RHS.");
            return;
        }

        var result = _analyzer.AddConstraint(coeffs, relation, rhs);
        DisplaySolveResult(result);
    }

    private void HandleShadowPrices()
    {
        var shadowPrices = _analyzer.ShadowPrices();

        Console.WriteLine("\n=== Shadow Prices ===");
        for (int i = 0; i < shadowPrices.Length; i++)
        {
            Console.WriteLine($"Constraint {i + 1}: {OutputWriter.Format(shadowPrices[i])}");
        }
        Console.WriteLine();
    }

    private void HandleDuality()
    {
        if (_parsed == null)
        {
            Console.WriteLine("No parsed model available for duality analysis.");
            return;
        }

        Console.WriteLine("\n--- Building Dual Model ---");
        var dual = _duality.BuildDual(_parsed);

        Console.WriteLine($"Dual model built with {dual.DecisionVariableCount} variables and {dual.Constraints.Count} constraints.");
        Console.WriteLine($"Dual objective type: {dual.ObjectiveType}");
        Console.WriteLine();

        Console.WriteLine("--- Solving Dual Model ---");
        var dualResult = _duality.SolveDual(_parsed);
        Console.WriteLine($"Dual status: {dualResult.Status}");
        if (dualResult.Status == SolutionStatus.Optimal)
        {
            Console.WriteLine($"Dual objective: {OutputWriter.Format(dualResult.ObjectiveValue)}");
        }
        Console.WriteLine();

        Console.WriteLine("--- Verifying Duality ---");
        var verification = _duality.VerifyDuality(_result, dualResult);
        Console.WriteLine(verification);
    }

    private void DisplayRange(SensitivityRange range)
    {
        if (range == null)
        {
            Console.WriteLine("No range information available.");
            return;
        }

        Console.WriteLine($"\n=== Range for {range.Subject} ===");
        Console.WriteLine($"Current value: {OutputWriter.Format(range.Current)}");
        Console.WriteLine($"Lower bound:   {(range.Lower == double.NegativeInfinity ? "-∞" : OutputWriter.Format(range.Lower))}");
        Console.WriteLine($"Upper bound:   {(range.Upper == double.PositiveInfinity ? "+∞" : OutputWriter.Format(range.Upper))}");
        Console.WriteLine();
    }

    private void DisplaySolveResult(SolveResult result)
    {
        if (result == null)
        {
            Console.WriteLine("No result available.");
            return;
        }

        Console.WriteLine($"\n=== Modified Solution ===");
        Console.WriteLine($"Status: {result.Status}");
        if (result.Status == SolutionStatus.Optimal)
        {
            Console.WriteLine($"Objective value: {OutputWriter.Format(result.ObjectiveValue)}");
            if (result.VariableValues != null)
            {
                Console.WriteLine("Variable values:");
                for (int i = 0; i < result.VariableValues.Length; i++)
                {
                    Console.WriteLine($"  x{i + 1}: {OutputWriter.Format(result.VariableValues[i])}");
                }
            }
        }
        if (!string.IsNullOrEmpty(result.Message))
        {
            Console.WriteLine($"Note: {result.Message}");
        }
        Console.WriteLine();
    }
}
