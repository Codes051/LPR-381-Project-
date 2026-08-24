using Solve.Exceptions;
using Solve.Models;
using Solve.Sensitivity;

namespace Solve.UI;

// ============================================================================
//  OWNER: Person C
//
//  One menu entry per marked sensitivity operation. Keep them in this order on
//  video so the marker can tick them off against the brief.
// ============================================================================

public class SensitivityMenu
{
    private readonly ParsedLP _parsed;
    private readonly SolveResult _result;

    public SensitivityMenu(ParsedLP parsed, SolveResult result)
    {
        _parsed = parsed;
        _result = result;
    }

    public void Run()
    {
        if (_result == null || _result.Status != SolutionStatus.Optimal)
            throw new LpException("Sensitivity analysis needs a model that solved to optimality.");

        var analyzer = new SensitivityAnalyzer(_result);
        var duality = new DualityAnalyzer();

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("--- Sensitivity Analysis ---");
            Console.WriteLine("   1. Range of a non-basic variable");
            Console.WriteLine("   2. Change a non-basic variable");
            Console.WriteLine("   3. Range of a basic variable");
            Console.WriteLine("   4. Change a basic variable");
            Console.WriteLine("   5. Range of a constraint right-hand side");
            Console.WriteLine("   6. Change a constraint right-hand side");
            Console.WriteLine("   7. Range of a variable in a non-basic column");
            Console.WriteLine("   8. Change a variable in a non-basic column");
            Console.WriteLine("   9. Add a new activity");
            Console.WriteLine("  10. Add a new constraint");
            Console.WriteLine("  11. Display shadow prices");
            Console.WriteLine("  12. Duality (build, solve, verify strong or weak)");
            Console.WriteLine("   0. Back");
            Console.Write("Select: ");

            var choice = Console.ReadLine();
            if (choice == "0") return;

            try
            {
                // TODO (C): prompt for the column / row / value each option needs, call the
                //   matching analyzer method, and print the result through OutputWriter.Format
                //   so the console and the output file agree to three decimals.
                throw new NotImplementedException($"Sensitivity option {choice} - Person C");
            }
            catch (LpException ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"\nNot built yet: {ex.Message}");
            }
        }
    }
}
