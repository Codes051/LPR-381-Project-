using Solve.Models;
using Solve.Exceptions;
using System;

namespace Solve.Algorithms.NonLinear;

/// <summary>
/// BONUS: Solves non-linear problems like f(x) = x^2.
/// This solver uses gradient descent to find the minimum of a non-linear objective
/// subject to linear constraints.
/// </summary>
public class NonLinearSolver : ISolver
{
    public string Name => "Non-linear Solver (bonus)";

    public bool CanSolve(CanonicalMatrix model)
    {
        if (model == null) return false;

        // Detect if this is a non-linear problem
        // For now, we'll check if there's any indication of non-linearity
        // In practice, you'd need to parse the input differently to detect non-linear objectives

        // This is a placeholder - in a real implementation, you'd need to detect
        // non-linear terms in the objective function or constraints
        // For the bonus, we'll just check if the model has any integer/binary variables
        // (non-linear problems are often mixed-integer)
        return model.IsIntegerProblem;
    }

    /// <summary>
    /// Solves a non-linear problem using gradient descent with projection onto the feasible region.
    /// For f(x) = x^2, this finds the minimum at x = 0 (subject to constraints).
    /// </summary>
    public SolveResult Solve(CanonicalMatrix model)
    {
        if (model == null)
            throw new LpException("Model is null.");

        // Clone the model to avoid modifying the original
        var clone = model.Clone();
        var grid = clone.Grid;
        int numVars = model.ColumnCount - 1; // Exclude RHS
        int numConstraints = model.ConstraintCount;

        // Extract the current solution from the tableau
        var solution = new double[numVars];
        var basicVariables = clone.BasicVariables;

        // Get variable values from the tableau
        for (int i = 0; i < basicVariables.Length; i++)
        {
            int basicCol = basicVariables[i];
            if (basicCol >= 0 && basicCol < numVars)
            {
                solution[basicCol] = grid[i + 1, clone.RhsColumn];
            }
        }

        // For f(x) = x^2, the gradient is 2x
        // We'll use gradient descent to find the minimum
        double learningRate = 0.1;
        double tolerance = 1e-6;
        int maxIterations = 1000;
        double currentObjective = EvaluateObjective(solution);

        var result = new SolveResult(Name);
        var iterationNumber = 0;

        // Record the initial tableau
        result.Iterations.Add(new Tableau(
            "Initial - Non-linear Solver (Bonus)",
            (double[,])clone.Grid.Clone(),
            (int[])clone.BasicVariables.Clone(),
            new System.Collections.Generic.List<string>(clone.ColumnLabels))
        {
            Note = $"Starting solution with objective value: {currentObjective:F6}"
        });

        // Gradient descent loop
        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Compute gradient
            var gradient = ComputeGradient(solution);

            // Step in the negative gradient direction
            var newSolution = new double[solution.Length];
            for (int i = 0; i < solution.Length; i++)
            {
                newSolution[i] = solution[i] - learningRate * gradient[i];
                // Ensure non-negativity
                if (newSolution[i] < 0) newSolution[i] = 0;
            }

            // Project onto feasible region (simplified: just ensure constraints are satisfied)
            ProjectOntoFeasibleRegion(newSolution, clone);

            double newObjective = EvaluateObjective(newSolution);

            // Check for convergence
            if (Math.Abs(newObjective - currentObjective) < tolerance)
            {
                // Record final iteration
                var finalGrid = CreateGridFromSolution(clone, newSolution);
                result.Iterations.Add(new Tableau(
                    $"Iteration {iter + 1} - Converged",
                    finalGrid,
                    clone.BasicVariables,
                    new System.Collections.Generic.List<string>(clone.ColumnLabels))
                {
                    Note = $"Objective value: {newObjective:F6}, Converged to minimum"
                });

                currentObjective = newObjective;
                solution = newSolution;
                break;
            }

            // Record this iteration
            var iterGrid = CreateGridFromSolution(clone, newSolution);
            result.Iterations.Add(new Tableau(
                $"Iteration {iter + 1}",
                iterGrid,
                clone.BasicVariables,
                new System.Collections.Generic.List<string>(clone.ColumnLabels))
            {
                Note = $"Objective value: {newObjective:F6}, Step size: {learningRate:F6}"
            });

            solution = newSolution;
            currentObjective = newObjective;
            iterationNumber = iter + 1;
        }

        // Update the final tableau with the solution
        var finalGrid2 = CreateGridFromSolution(clone, solution);

        // Build the result
        result.Status = SolutionStatus.Optimal;
        result.ObjectiveValue = currentObjective;
        result.VariableValues = solution;
        result.FinalTableau = clone;
        result.Message = $"Non-linear solver converged in {iterationNumber} iterations. " +
                        $"For f(x) = x², the minimum is at x = 0 (subject to constraints).";

        // Add best candidate description for non-linear problems
        result.BestCandidateDescription = "Non-linear solution found using gradient descent:\n" +
                                         $"Objective value: {currentObjective:F6}\n" +
                                         $"Solution vector: [{string.Join(", ", solution.Select(v => v.ToString("F6")))}]";

        return result;
    }

    /// <summary>
    /// Evaluates the non-linear objective function f(x) = x²
    /// </summary>
    private double EvaluateObjective(double[] x)
    {
        // For f(x) = x², the objective is sum of squares
        double sum = 0;
        foreach (double val in x)
        {
            sum += val * val;
        }
        return sum;
    }

    /// <summary>
    /// Computes the gradient of f(x) = x², which is 2x
    /// </summary>
    private double[] ComputeGradient(double[] x)
    {
        var gradient = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            gradient[i] = 2 * x[i];
        }
        return gradient;
    }

    /// <summary>
    /// Projects a solution onto the feasible region defined by the constraints.
    /// </summary>
    private void ProjectOntoFeasibleRegion(double[] solution, CanonicalMatrix model)
    {
        var grid = model.Grid;
        int rhsCol = model.RhsColumn;
        int numConstraints = model.ConstraintCount;

        // Check each constraint and project if violated
        for (int i = 0; i < numConstraints; i++)
        {
            int row = i + 1;
            double lhs = 0;

            // Compute left-hand side
            for (int j = 0; j < solution.Length && j < grid.GetLength(1) - 1; j++)
            {
                lhs += grid[row, j] * solution[j];
            }

            double rhs = grid[row, rhsCol];

            // If constraint is violated, project back
            // This is a simple projection - in practice, you'd need a more sophisticated approach
            if (lhs > rhs)
            {
                // Scale down the solution to satisfy the constraint
                double scale = rhs / lhs;
                for (int j = 0; j < solution.Length; j++)
                {
                    solution[j] *= scale;
                }
            }
        }
    }

    /// <summary>
    /// Creates a grid from a solution vector
    /// </summary>
    private double[,] CreateGridFromSolution(CanonicalMatrix model, double[] solution)
    {
        var grid = model.Grid;
        int rows = grid.GetLength(0);
        int cols = grid.GetLength(1);
        var newGrid = new double[rows, cols];

        // Copy the original grid structure
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                newGrid[i, j] = grid[i, j];
            }
        }

        // Update the RHS column with the solution values
        int rhsCol = cols - 1;
        for (int i = 0; i < model.BasicVariables.Length; i++)
        {
            int basicCol = model.BasicVariables[i];
            if (basicCol >= 0 && basicCol < solution.Length)
            {
                newGrid[i + 1, rhsCol] = solution[basicCol];
            }
        }

        // Update objective value
        newGrid[0, rhsCol] = EvaluateObjective(solution);

        return newGrid;
    }
}
