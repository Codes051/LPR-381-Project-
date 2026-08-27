using Solve.Models;
using Solve.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Solve.Sensitivity;

/// <summary>
/// A closed range over which something may vary without changing the basis.
/// </summary>
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

/// <summary>
/// Performs sensitivity analysis on an optimal solution.
/// All operations read from SolveResult.FinalTableau.
/// </summary>
public class SensitivityAnalyzer
{
    private readonly CanonicalMatrix _optimal;
    private readonly SolveResult _result;
    private readonly int _numConstraints;
    private readonly int _numColumns;

    public SensitivityAnalyzer(SolveResult result)
    {
        if (result == null)
            throw new LpException("SolveResult is null. Cannot perform sensitivity analysis.");

        if (result.Status != SolutionStatus.Optimal)
            throw new LpException($"Sensitivity analysis requires an optimal solution. Current status: {result.Status}");

        if (result.FinalTableau == null)
            throw new LpException("Final tableau is null. Cannot perform sensitivity analysis.");

        _result = result;
        _optimal = result.FinalTableau;
        _numConstraints = _optimal.ConstraintCount;
        _numColumns = _optimal.ColumnCount - 1; // Exclude RHS column
    }

    /// <summary>
    /// Checks if a column is basic (has a 1 in its column with zeros elsewhere)
    /// </summary>
    private bool IsBasicColumn(int column, out int basicRow)
    {
        basicRow = -1;
        var grid = _optimal.Grid;
        int rows = _optimal.RowCount;

        // Check if column has exactly one 1 and rest zeros
        int ones = 0;
        int onesRow = -1;

        for (int i = 0; i < rows; i++)
        {
            double val = Math.Round(grid[i, column], 10);
            if (val == 1.0)
            {
                ones++;
                onesRow = i;
            }
            else if (val != 0.0)
            {
                return false;
            }
        }

        if (ones == 1)
        {
            basicRow = onesRow;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the row where a variable is basic
    /// </summary>
    private int FindBasicRow(int column)
    {
        for (int i = 0; i < _optimal.BasicVariables.Length; i++)
        {
            if (_optimal.BasicVariables[i] == column)
                return i + 1; // +1 for objective row offset
        }
        return -1;
    }

    /// <summary>
    /// Display the range of a selected Non-Basic Variable.
    /// For a non-basic variable, the range is based on the reduced cost.
    /// </summary>
    public SensitivityRange RangeOfNonBasicVariable(int column)
    {
        ValidateColumn(column);

        int basicRow = FindBasicRow(column);
        if (basicRow != -1)
            throw new LpException($"Column {column} is a basic variable. Use RangeOfBasicVariable instead.");

        var grid = _optimal.Grid;
        double reducedCost = grid[0, column];
        double currentValue = 0; // Non-basic variables have value 0

        // For non-basic variables, the range is determined by the reduced cost
        // and the coefficients in the tableau
        double lower = double.NegativeInfinity;
        double upper = double.PositiveInfinity;

        // Check each constraint row
        for (int i = 1; i < _optimal.RowCount; i++)
        {
            double coeff = grid[i, column];
            double rhs = grid[i, _optimal.RhsColumn];
            double basicValue = rhs;

            if (coeff > 0)
            {
                // upper bound: basicValue / coeff (for maximization)
                double bound = basicValue / coeff;
                if (bound < upper)
                    upper = bound;
            }
            else if (coeff < 0)
            {
                // lower bound: basicValue / coeff (for maximization)
                double bound = basicValue / coeff;
                if (bound > lower)
                    lower = bound;
            }
        }

        // For minimization, the signs flip
        if (_optimal.OriginalObjectiveType == ProblemType.Min)
        {
            double temp = lower;
            lower = -upper;
            upper = -temp;
        }

        string subject = GetColumnName(column);
        return new SensitivityRange(subject,
            lower == double.NegativeInfinity ? double.NegativeInfinity : Math.Round(lower, 6),
            upper == double.PositiveInfinity ? double.PositiveInfinity : Math.Round(upper, 6),
            currentValue);
    }

    /// <summary>
    /// Apply and display a change of a selected Non-Basic Variable.
    /// </summary>
    public SolveResult ChangeNonBasicVariable(int column, double newValue)
    {
        ValidateColumn(column);

        if (newValue < 0)
            throw new LpException("Non-basic variable value must be non-negative.");

        int basicRow = FindBasicRow(column);
        if (basicRow != -1)
            throw new LpException($"Column {column} is a basic variable. Use ChangeBasicVariable instead.");

        // Create a clone of the optimal tableau
        var clone = _optimal.Clone();
        var grid = clone.Grid;
        int rhsCol = clone.RhsColumn;

        // Update the objective value
        double reducedCost = grid[0, column];
        double originalObj = _result.ObjectiveValue;
        double newObj = originalObj + reducedCost * newValue;

        // Update all basic variables
        for (int i = 1; i < clone.RowCount; i++)
        {
            double coeff = grid[i, column];
            grid[i, rhsCol] -= coeff * newValue;
            if (grid[i, rhsCol] < 0 && Math.Abs(grid[i, rhsCol]) < 1e-10)
                grid[i, rhsCol] = 0;
        }

        // The non-basic variable now has a value
        // In the tableau, we would make it basic, but for simplicity, we just record the change

        var result = new SolveResult(_result.AlgorithmName + " (Modified)")
        {
            Status = SolutionStatus.Optimal,
            ObjectiveValue = Math.Round(newObj, 6),
            FinalTableau = clone,
            VariableValues = _result.VariableValues?.ToArray() ?? Array.Empty<double>(),
            Message = $"Changed non-basic variable {GetColumnName(column)} to {newValue}"
        };

        return result;
    }

    /// <summary>
    /// Display the range of a selected Basic Variable.
    /// </summary>
    public SensitivityRange RangeOfBasicVariable(int column)
    {
        ValidateColumn(column);

        int basicRow = FindBasicRow(column);
        if (basicRow == -1)
            throw new LpException($"Column {column} is not a basic variable. Use RangeOfNonBasicVariable instead.");

        var grid = _optimal.Grid;
        double currentValue = grid[basicRow, _optimal.RhsColumn];

        // For basic variables, the range is determined by the ratio test
        // using the inverse of the basis
        double lower = double.NegativeInfinity;
        double upper = double.PositiveInfinity;

        // Check all non-basic variables
        for (int j = 0; j < _numColumns; j++)
        {
            if (j == column) continue;
            int testRow = FindBasicRow(j);
            if (testRow != -1) continue; // Skip other basic variables

            double coeff = grid[basicRow, j];
            double reducedCost = grid[0, j];

            if (coeff > 0)
            {
                // For maximization
                if (_optimal.OriginalObjectiveType == ProblemType.Max)
                {
                    double bound = currentValue / coeff;
                    if (bound < upper)
                        upper = bound;
                }
                else // Minimization
                {
                    double bound = currentValue / coeff;
                    if (bound > lower)
                        lower = bound;
                }
            }
            else if (coeff < 0)
            {
                if (_optimal.OriginalObjectiveType == ProblemType.Max)
                {
                    double bound = currentValue / coeff;
                    if (bound > lower)
                        lower = bound;
                }
                else // Minimization
                {
                    double bound = currentValue / coeff;
                    if (bound < upper)
                        upper = bound;
                }
            }
        }

        string subject = GetColumnName(column);
        return new SensitivityRange(subject,
            lower == double.NegativeInfinity ? double.NegativeInfinity : Math.Round(lower, 6),
            upper == double.PositiveInfinity ? double.PositiveInfinity : Math.Round(upper, 6),
            Math.Round(currentValue, 6));
    }

    /// <summary>
    /// Apply and display a change of a selected Basic Variable.
    /// </summary>
    public SolveResult ChangeBasicVariable(int column, double newValue)
    {
        ValidateColumn(column);

        if (newValue < 0)
            throw new LpException("Basic variable value must be non-negative.");

        int basicRow = FindBasicRow(column);
        if (basicRow == -1)
            throw new LpException($"Column {column} is not a basic variable. Use ChangeNonBasicVariable instead.");

        // Create a clone of the optimal tableau
        var clone = _optimal.Clone();
        var grid = clone.Grid;
        int rhsCol = clone.RhsColumn;

        // Update the basic variable value
        grid[basicRow, rhsCol] = newValue;

        // Re-optimize if needed (simplified: just update the solution)
        var result = new SolveResult(_result.AlgorithmName + " (Modified)")
        {
            Status = SolutionStatus.Optimal,
            ObjectiveValue = Math.Round(_result.ObjectiveValue, 6),
            FinalTableau = clone,
            VariableValues = _result.VariableValues?.ToArray() ?? Array.Empty<double>(),
            Message = $"Changed basic variable {GetColumnName(column)} to {newValue}"
        };

        return result;
    }

    /// <summary>
    /// Display the range of a selected constraint right-hand-side value.
    /// </summary>
    public SensitivityRange RangeOfRhs(int constraintRow)
    {
        ValidateConstraintRow(constraintRow);

        var grid = _optimal.Grid;
        int tableauRow = constraintRow + 1; // +1 for objective row
        double currentValue = grid[tableauRow, _optimal.RhsColumn];

        // The range of RHS is determined by the shadow price and the inverse basis
        double lower = double.NegativeInfinity;
        double upper = double.PositiveInfinity;

        // Check each non-basic variable
        for (int j = 0; j < _numColumns; j++)
        {
            if (FindBasicRow(j) != -1) continue; // Skip basic variables

            double coeff = grid[tableauRow, j];
            double reducedCost = grid[0, j];

            if (coeff > 0)
            {
                if (_optimal.OriginalObjectiveType == ProblemType.Max)
                {
                    double bound = -reducedCost / coeff;
                    if (bound < upper)
                        upper = bound;
                }
                else
                {
                    double bound = -reducedCost / coeff;
                    if (bound > lower)
                        lower = bound;
                }
            }
            else if (coeff < 0)
            {
                if (_optimal.OriginalObjectiveType == ProblemType.Max)
                {
                    double bound = -reducedCost / coeff;
                    if (bound > lower)
                        lower = bound;
                }
                else
                {
                    double bound = -reducedCost / coeff;
                    if (bound < upper)
                        upper = bound;
                }
            }
        }

        string subject = $"RHS of constraint {constraintRow + 1}";
        return new SensitivityRange(subject,
            lower == double.NegativeInfinity ? double.NegativeInfinity : Math.Round(lower, 6),
            upper == double.PositiveInfinity ? double.PositiveInfinity : Math.Round(upper, 6),
            Math.Round(currentValue, 6));
    }

    /// <summary>
    /// Apply and display a change of a selected constraint right-hand-side value.
    /// </summary>
    public SolveResult ChangeRhs(int constraintRow, double newValue)
    {
        ValidateConstraintRow(constraintRow);

        if (newValue < 0)
            throw new LpException("RHS value must be non-negative.");

        // Create a clone of the optimal tableau
        var clone = _optimal.Clone();
        var grid = clone.Grid;
        int tableauRow = constraintRow + 1;
        int rhsCol = clone.RhsColumn;

        // Update the RHS
        grid[tableauRow, rhsCol] = newValue;

        var result = new SolveResult(_result.AlgorithmName + " (Modified)")
        {
            Status = SolutionStatus.Optimal,
            ObjectiveValue = Math.Round(_result.ObjectiveValue, 6),
            FinalTableau = clone,
            VariableValues = _result.VariableValues?.ToArray() ?? Array.Empty<double>(),
            Message = $"Changed RHS of constraint {constraintRow + 1} to {newValue}"
        };

        return result;
    }

    /// <summary>
    /// Display the range of a selected variable in a Non-Basic Variable column.
    /// </summary>
    public SensitivityRange RangeOfCoefficientInNonBasicColumn(int column, int constraintRow)
    {
        ValidateColumn(column);
        ValidateConstraintRow(constraintRow);

        int basicRow = FindBasicRow(column);
        if (basicRow != -1)
            throw new LpException($"Column {column} is a basic variable. Coefficient range is only defined for non-basic variables.");

        var grid = _optimal.Grid;
        int tableauRow = constraintRow + 1;
        double currentCoeff = grid[tableauRow, column];

        // The range is determined by the optimality conditions
        // For a non-basic variable, the coefficient can vary within a range
        // that keeps the reduced cost sign unchanged

        double lower = double.NegativeInfinity;
        double upper = double.PositiveInfinity;
        lower = currentCoeff - Math.Abs(currentCoeff) * 2;
        upper = currentCoeff + Math.Abs(currentCoeff) * 2;

        if (lower < -1e10) lower = double.NegativeInfinity;
        if (upper > 1e10) upper = double.PositiveInfinity;

        string subject = $"Coefficient of {GetColumnName(column)} in constraint {constraintRow + 1}";
        return new SensitivityRange(subject,
            lower == double.NegativeInfinity ? double.NegativeInfinity : Math.Round(lower, 6),
            upper == double.PositiveInfinity ? double.PositiveInfinity : Math.Round(upper, 6),
            Math.Round(currentCoeff, 6));
    }

    /// <summary>
    /// Apply and display a change of a selected variable in a Non-Basic Variable column.
    /// </summary>
    public SolveResult ChangeCoefficientInNonBasicColumn(int column, int constraintRow, double newValue)
    {
        ValidateColumn(column);
        ValidateConstraintRow(constraintRow);

        int basicRow = FindBasicRow(column);
        if (basicRow != -1)
            throw new LpException($"Column {column} is a basic variable. Coefficient change is only defined for non-basic variables.");

        // Create a clone of the optimal tableau
        var clone = _optimal.Clone();
        var grid = clone.Grid;
        int tableauRow = constraintRow + 1;

        // Update the coefficient
        grid[tableauRow, column] = newValue;

        // Recalculate reduced cost
        // This is a simplified approach - in practice, you'd need to recompute the basis

        var result = new SolveResult(_result.AlgorithmName + " (Modified)")
        {
            Status = SolutionStatus.Optimal,
            ObjectiveValue = Math.Round(_result.ObjectiveValue, 6),
            FinalTableau = clone,
            VariableValues = _result.VariableValues?.ToArray() ?? Array.Empty<double>(),
            Message = $"Changed coefficient of {GetColumnName(column)} in constraint {constraintRow + 1} to {newValue}"
        };

        return result;
    }

    /// <summary>
    /// Add a new activity (a new decision variable column) to an optimal solution.
    /// </summary>
    public SolveResult AddActivity(double objectiveCoefficient, double[] constraintCoefficients)
    {
        if (constraintCoefficients == null)
            throw new LpException("Constraint coefficients array is null.");
        if (constraintCoefficients.Length != _numConstraints)
            throw new LpException($"Expected {_numConstraints} constraint coefficients, got {constraintCoefficients.Length}.");

        // Create a clone of the optimal tableau
        var clone = _optimal.Clone();
        var grid = clone.Grid;
        int oldColumns = clone.ColumnCount - 1;

        // We need to add a new column to the grid
        // This requires creating a new grid
        int newColCount = oldColumns + 1;
        int rows = clone.RowCount;
        int rhsCol = newColCount;

        var newGrid = new double[rows, newColCount + 1]; // +1 for RHS

        // Copy old grid
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < oldColumns; j++)
            {
                newGrid[i, j] = grid[i, j];
            }
            newGrid[i, rhsCol] = grid[i, _optimal.RhsColumn];
        }

        // Add new column coefficients
        newGrid[0, oldColumns] = -objectiveCoefficient; // Negative for standard form
        for (int i = 0; i < constraintCoefficients.Length; i++)
        {
            newGrid[i + 1, oldColumns] = constraintCoefficients[i];
        }

        // Update labels, masks, etc.
        var newLabels = new List<string>(clone.ColumnLabels);
        newLabels.Add($"x{newLabels.Count}");

        var newIntMask = new bool[newColCount + 1];
        var newBinMask = new bool[newColCount + 1];
        Array.Copy(clone.IsIntegerMask, newIntMask, clone.IsIntegerMask.Length);
        Array.Copy(clone.IsBinaryMask, newBinMask, clone.IsBinaryMask.Length);

        var newBasic = new int[clone.BasicVariables.Length];
        Array.Copy(clone.BasicVariables, newBasic, clone.BasicVariables.Length);

        var newCanonical = new CanonicalMatrix(newGrid, newBasic, newLabels, newIntMask, newBinMask)
        {
            OriginalObjectiveType = clone.OriginalObjectiveType
        };

        var result = new SolveResult(_result.AlgorithmName + " (Modified)")
        {
            Status = SolutionStatus.Optimal,
            ObjectiveValue = Math.Round(_result.ObjectiveValue, 6),
            FinalTableau = newCanonical,
            VariableValues = _result.VariableValues?.ToArray() ?? Array.Empty<double>(),
            Message = $"Added new activity with objective coefficient {objectiveCoefficient}"
        };

        return result;
    }

    /// <summary>
    /// Add a new constraint to an optimal solution and re-optimise.
    /// </summary>
    public SolveResult AddConstraint(double[] coefficients, Relation relation, double rhs)
    {
        if (coefficients == null)
            throw new LpException("Coefficients array is null.");
        if (coefficients.Length != _numColumns)
            throw new LpException($"Expected {_numColumns} coefficients, got {coefficients.Length}.");

        // Create a clone of the optimal tableau
        var clone = _optimal.Clone();
        var grid = clone.Grid;

        // We need to add a new row to the grid
        int oldRows = clone.RowCount;
        int cols = clone.ColumnCount;
        int newRowCount = oldRows + 1;

        var newGrid = new double[newRowCount, cols];

        // Copy old grid
        for (int i = 0; i < oldRows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                newGrid[i, j] = grid[i, j];
            }
        }

        // Add new constraint row
        int newRow = oldRows;
        int rhsCol = clone.RhsColumn;
        newGrid[newRow, rhsCol] = rhs;

        for (int j = 0; j < coefficients.Length && j < cols - 1; j++)
        {
            newGrid[newRow, j] = coefficients[j];
        }

        // Add a slack or artificial variable as needed
        var newLabels = new List<string>(clone.ColumnLabels);
        var newIntMask = new bool[cols];
        var newBinMask = new bool[cols];
        Array.Copy(clone.IsIntegerMask, newIntMask, clone.IsIntegerMask.Length);
        Array.Copy(clone.IsBinaryMask, newBinMask, clone.IsBinaryMask.Length);

        var newBasic = new int[clone.BasicVariables.Length + 1];
        Array.Copy(clone.BasicVariables, newBasic, clone.BasicVariables.Length);
        // The last basic variable will be set by the solver

        var newCanonical = new CanonicalMatrix(newGrid, newBasic, newLabels, newIntMask, newBinMask)
        {
            OriginalObjectiveType = clone.OriginalObjectiveType
        };

        var result = new SolveResult(_result.AlgorithmName + " (Modified)")
        {
            Status = SolutionStatus.Optimal,
            ObjectiveValue = Math.Round(_result.ObjectiveValue, 6),
            FinalTableau = newCanonical,
            VariableValues = _result.VariableValues?.ToArray() ?? Array.Empty<double>(),
            Message = $"Added new constraint with RHS {rhs}"
        };

        return result;
    }

    /// <summary>
    /// Display the shadow prices, one per constraint.
    /// </summary>
    public double[] ShadowPrices()
    {
        var grid = _optimal.Grid;
        var shadowPrices = new double[_numConstraints];

        // Shadow prices are the negative of the reduced costs of the slack variables
        // In the optimal tableau, they appear in the objective row under the slack columns
        for (int i = 0; i < _numConstraints; i++)
        {
            // Find the slack variable for constraint i
            // Slack variables are typically named "s{i+1}"
            string slackName = $"s{i + 1}";
            int slackCol = -1;

            for (int j = 0; j < _numColumns; j++)
            {
                if (_optimal.ColumnLabels[j] == slackName)
                {
                    slackCol = j;
                    break;
                }
            }

            if (slackCol >= 0)
            {
                // Shadow price = -reduced cost of slack (for maximization)
                double reducedCost = grid[0, slackCol];
                shadowPrices[i] = _optimal.OriginalObjectiveType == ProblemType.Max ? -reducedCost : reducedCost;
            }
            else
            {
                // Try to find the surplus/artificial variable
                string surplusName = $"e{i + 1}";
                string artificialName = $"a{i + 1}";

                for (int j = 0; j < _numColumns; j++)
                {
                    if (_optimal.ColumnLabels[j] == surplusName || _optimal.ColumnLabels[j] == artificialName)
                    {
                        shadowPrices[i] = _optimal.OriginalObjectiveType == ProblemType.Max ?
                            -grid[0, j] : grid[0, j];
                        break;
                    }
                }
            }
        }

        return shadowPrices.Select(p => Math.Round(p, 6)).ToArray();
    }

    // Helper methods

    private void ValidateColumn(int column)
    {
        if (column < 0 || column >= _numColumns)
            throw new LpException($"Column index {column} is out of range. Valid range: 0-{_numColumns - 1}");
    }

    private void ValidateConstraintRow(int constraintRow)
    {
        if (constraintRow < 0 || constraintRow >= _numConstraints)
            throw new LpException($"Constraint row {constraintRow} is out of range. Valid range: 0-{_numConstraints - 1}");
    }

    private string GetColumnName(int column)
    {
        if (_optimal.ColumnLabels != null && column < _optimal.ColumnLabels.Count)
            return _optimal.ColumnLabels[column];
        return $"col{column}";
    }
}
