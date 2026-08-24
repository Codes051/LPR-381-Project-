using Solve.Algorithms;
using Solve.Algorithms.BranchAndBound;
using Solve.Algorithms.CuttingPlane;
using Solve.Algorithms.NonLinear;
using Solve.Algorithms.Simplex;
using Solve.Exceptions;
using Solve.Models;
using Solve.Output;
using Solve.Parsing;

namespace Solve.UI;

// ============================================================================
//  OWNER: Person C
//  Marks: Interface presentation (5)
//
//  This shell runs today with every algorithm stubbed, so the whole group can
//  see integration working from day one. Polish is yours; the wiring is here.
// ============================================================================

public class MainMenu
{
    private readonly List<ISolver> _solvers = new List<ISolver>
    {
        new PrimalSimplexSolver(),
        new RevisedPrimalSimplexSolver(),
        new BranchAndBoundSimplexSolver(),
        new CuttingPlaneSolver(),
        new KnapsackBranchAndBoundSolver(),
        new NonLinearSolver()
    };

    private ParsedLP _parsed;
    private CanonicalMatrix _canonical;
    private SolveResult _result;
    private string _inputPath;

    public void Run()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== LPR381 Solver ===");
            Console.WriteLine($"  Model loaded : {(_parsed == null ? "(none)" : _inputPath)}");
            Console.WriteLine($"  Solved       : {(_result == null ? "no" : _result.AlgorithmName)}");
            Console.WriteLine();
            Console.WriteLine("  1. Load a model from an input file");
            Console.WriteLine("  2. Choose an algorithm and solve");
            Console.WriteLine("  3. Sensitivity analysis");
            Console.WriteLine("  4. Write results to an output file");
            Console.WriteLine("  0. Exit");
            Console.Write("Select: ");

            var choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    case "1": LoadModel(); break;
                    case "2": SolveModel(); break;
                    case "3": new SensitivityMenu(_parsed, _result).Run(); break;
                    case "4": WriteOutput(); break;
                    case "0": return;
                    default: Console.WriteLine("Not a valid option."); break;
                }
            }
            catch (LpException ex)
            {
                // Expected, user-facing failures. Error handling is a marked criterion:
                // print the message, never a stack trace, and never fall over.
                Console.WriteLine($"\nError: {ex.Message}");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"\nNot built yet: {ex.Message}");
            }
        }
    }

    private void LoadModel()
    {
        Console.Write("Path to input file: ");
        var path = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');

        _parsed = new LpParser().ParseFile(path);
        _canonical = new Canonicalizer().ToCanonicalForm(_parsed);
        _inputPath = path;
        _result = null;

        Console.WriteLine($"Loaded {_parsed.DecisionVariableCount} variables, {_parsed.Constraints.Count} constraints.");
    }

    private void SolveModel()
    {
        if (_canonical == null)
            throw new LpException("Load a model first.");

        Console.WriteLine();
        for (var i = 0; i < _solvers.Count; i++)
        {
            var suitable = _solvers[i].CanSolve(_canonical) ? "" : "   (not suitable for this model)";
            Console.WriteLine($"  {i + 1}. {_solvers[i].Name}{suitable}");
        }
        Console.Write("Algorithm: ");

        if (!int.TryParse(Console.ReadLine(), out var pick) || pick < 1 || pick > _solvers.Count)
            throw new LpException("Not a valid algorithm choice.");

        var solver = _solvers[pick - 1];
        if (!solver.CanSolve(_canonical))
            throw new LpException($"{solver.Name} cannot solve this model.");

        _result = solver.Solve(_canonical);

        Console.WriteLine($"\nStatus: {_result.Status}");
        if (_result.Status == SolutionStatus.Optimal)
            Console.WriteLine($"Objective: {OutputWriter.Format(_result.ObjectiveValue)}");
        else
            Console.WriteLine(_result.Message);
    }

    private void WriteOutput()
    {
        if (_result == null)
            throw new LpException("Solve a model first.");

        Console.Write("Path to output file: ");
        var path = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');

        var writer = new OutputWriter();
        writer.WriteResult(_result);
        writer.Save(path);

        Console.WriteLine($"Written to {path}");
    }
}
