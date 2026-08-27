using Solve.Algorithms.Simplex;
using Solve.Models;
using Solve.Exceptions;
using Solve.Parsing;
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

    /// <summary>
    /// The model as the user wrote it, when the caller supplied it.
    /// </summary>
    /// <remarks>
    /// Every "apply and display a change" operation rebuilds this model with the change made
    /// and solves it again. Editing the optimal tableau in place is not enough: changing a
    /// right-hand side or adding a constraint can make the current basis infeasible or
    /// non-optimal, so the answer has to be re-derived rather than patched.
    /// </remarks>
    private readonly ParsedLP _model;

    public SensitivityAnalyzer(SolveResult result) : this(result, null)
    {
    }

    public SensitivityAnalyzer(SolveResult result, ParsedLP model)
    {
        _model = model;
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

    // ----------------------------------------------------------------------
    //  Re-solving support
    //
    //  An "apply a change" operation cannot be done by editing the optimal
    //  tableau in place. Changing a right-hand side moves the basic values and
    //  can make the basis infeasible; changing an objective coefficient can make
    //  it non-optimal; adding a row or a column changes the shape of the problem
    //  altogether. Each of these rebuilds the model with the change applied and
    //  solves it again, which is correct by construction and also produces a full
    //  set of tableau iterations for the output file.
    // ----------------------------------------------------------------------

    private ParsedLP RequireModel(string operation)
    {
        if (_model == null)
        {
            throw new LpException(
                $"{operation} needs the original model. Construct the analyzer with " +
                "new SensitivityAnalyzer(result, parsedModel) so the change can be applied " +
                "and re-solved.");
        }

        return _model;
    }

    /// <summary>
    /// Deep copy of the model, with fresh constraint objects.
    /// </summary>
    /// <remarks>
    /// The copies must be new <see cref="Constraint"/> instances: canonicalization appends to
    /// each constraint's GeneratedVariables list, so reusing the originals would add a second
    /// set of slack and artificial columns the next time the model is canonicalized.
    /// </remarks>
    private ParsedLP CloneModel()
    {
        var source = RequireModel("This operation");
        var constraints = new List<Constraint>();

        foreach (var constraint in source.Constraints)
        {
            constraints.Add(new Constraint(
                new List<double>(constraint.Coefficients),
                constraint.RelationalOperator,
                constraint.RHS));
        }

        return new ParsedLP(
            source.ObjectiveType,
            new List<double>(source.ObjectiveCoefficients),
            constraints,
            new List<SignRestriction>(source.Restrictions));
    }

    private static SolveResult ReSolve(ParsedLP modified, string description)
    {
        var canonical = new Canonicalizer().ToCanonicalForm(modified);
        var result = new PrimalSimplexSolver().Solve(canonical);

        result.Message = string.IsNullOrWhiteSpace(result.Message)
            ? description
            : description + " " + result.Message;

        return result;
    }

    /// <summary>
    /// Maps a grid column back to the variable it represents in the model as written.
    /// </summary>
    /// <remarks>
    /// Not the identity: a variable declared urs occupies two columns and one declared - is
    /// stored as its own negation, so VariableMap is the only reliable way back.
    /// </remarks>
    private int VariableIndexForColumn(int column)
    {
        var map = _optimal.VariableMap;

        if (map == null)
            return column;

        for (var i = 0; i < map.Length; i++)
        {
            if (map[i].PositiveColumn == column || map[i].NegativeColumn == column)
                return i;
        }

        throw new LpException(
            $"Column {ColumnName(column)} is not one of the decision variables, so it has no " +
            "objective coefficient to change.");
    }

    private string ColumnName(int column) =>
        column >= 0 && column < _optimal.ColumnLabels.Count
            ? _optimal.ColumnLabels[column]
            : "column " + column;

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

        if (FindBasicRow(column) != -1)
            throw new LpException($"{ColumnName(column)} is basic. Use RangeOfBasicVariable instead.");

        var variable = RangeableVariable(column);
        var current = RequireModel("A coefficient range").ObjectiveCoefficients[variable];

        // The basis stays optimal while every reduced cost stays non-negative. Raising the
        // objective coefficient of a NON-BASIC column lowers only its own reduced cost, one
        // for one, so the slack available is exactly that reduced cost. Lowering it can never
        // make the column attractive, so that direction is unbounded.
        var slack = _optimal.Grid[0, column];

        return _optimal.OriginalObjectiveType == ProblemType.Max
            ? new SensitivityRange(CoefficientSubject(variable), double.NegativeInfinity,
                                   Math.Round(current + slack, 6), Math.Round(current, 6))
            // A Min model was normalised by negating the objective, so the direction flips.
            : new SensitivityRange(CoefficientSubject(variable), Math.Round(current - slack, 6),
                                   double.PositiveInfinity, Math.Round(current, 6));
    }

    /// <summary>
    /// Apply and display a change of a selected Non-Basic Variable.
    /// </summary>
    public SolveResult ChangeNonBasicVariable(int column, double newValue)
    {
        ValidateColumn(column);

        if (FindBasicRow(column) != -1)
            throw new LpException($"{ColumnName(column)} is basic. Use ChangeBasicVariable instead.");

        // The paired range operation reports the range of this variable's OBJECTIVE
        // COEFFICIENT, so the change has to be the same quantity or the two do not describe
        // the same thing. An objective coefficient may be negative.
        return ChangeObjectiveCoefficient(column, newValue, "Non-basic");
    }

    /// <summary>
    /// Display the range of a selected Basic Variable.
    /// </summary>
    public SensitivityRange RangeOfBasicVariable(int column)
    {
        ValidateColumn(column);

        var basicRow = FindBasicRow(column);
        if (basicRow == -1)
            throw new LpException($"{ColumnName(column)} is not basic. Use RangeOfNonBasicVariable instead.");

        var variable = RangeableVariable(column);
        var current = RequireModel("A coefficient range").ObjectiveCoefficients[variable];
        var grid = _optimal.Grid;

        // Changing the cost of a BASIC variable moves the reduced cost of every non-basic
        // column, each by the entry of this variable row in that column. The basis survives
        // while all of them stay non-negative, so each non-basic column contributes one limit.
        var lowerDelta = double.NegativeInfinity;
        var upperDelta = double.PositiveInfinity;

        for (var j = 0; j < _numColumns; j++)
        {
            if (j == column || FindBasicRow(j) != -1)
                continue;

            var rate = grid[basicRow, j];
            var reduced = grid[0, j];

            if (rate > 1e-9)
            {
                var limit = -reduced / rate;
                if (limit > lowerDelta)
                    lowerDelta = limit;
            }
            else if (rate < -1e-9)
            {
                var limit = -reduced / rate;
                if (limit < upperDelta)
                    upperDelta = limit;
            }
        }

        // The deltas are in the internally normalised maximisation. A Min model had its
        // objective negated, so the interval has to be reflected before it is reported.
        double lower;
        double upper;

        if (_optimal.OriginalObjectiveType == ProblemType.Max)
        {
            lower = double.IsNegativeInfinity(lowerDelta) ? double.NegativeInfinity : current + lowerDelta;
            upper = double.IsPositiveInfinity(upperDelta) ? double.PositiveInfinity : current + upperDelta;
        }
        else
        {
            lower = double.IsPositiveInfinity(upperDelta) ? double.NegativeInfinity : current - upperDelta;
            upper = double.IsNegativeInfinity(lowerDelta) ? double.PositiveInfinity : current - lowerDelta;
        }

        return new SensitivityRange(
            CoefficientSubject(variable),
            double.IsInfinity(lower) ? lower : Math.Round(lower, 6),
            double.IsInfinity(upper) ? upper : Math.Round(upper, 6),
            Math.Round(current, 6));
    }

    /// <summary>
    /// Apply and display a change of a selected Basic Variable.
    /// </summary>
    public SolveResult ChangeBasicVariable(int column, double newValue)
    {
        ValidateColumn(column);

        if (FindBasicRow(column) == -1)
            throw new LpException($"{ColumnName(column)} is not basic. Use ChangeNonBasicVariable instead.");

        return ChangeObjectiveCoefficient(column, newValue, "Basic");
    }

    /// <summary>
    /// Changes the objective coefficient of the variable occupying the given column, then
    /// re-solves. Shared by the basic and non-basic cases, which differ only in validation.
    /// </summary>
    private SolveResult ChangeObjectiveCoefficient(int column, double newValue, string role)
    {
        var variable = VariableIndexForColumn(column);
        var modified = CloneModel();
        var previous = modified.ObjectiveCoefficients[variable];

        modified.ObjectiveCoefficients[variable] = newValue;

        return ReSolve(
            modified,
            $"{role} variable x{variable + 1}: objective coefficient changed from " +
            $"{previous:0.###} to {newValue:0.###}.");
    }

    /// <summary>
    /// Display the range of a selected constraint right-hand-side value.
    /// </summary>
    public SensitivityRange RangeOfRhs(int constraintRow)
    {
        ValidateConstraintRow(constraintRow);

        var grid = _optimal.Grid;
        var rhsColumn = _optimal.RhsColumn;

        // The relevant column of the basis inverse is the column of whichever variable STARTED
        // basic in this row: the slack of a <= row, or the artificial of a >= or = row. In the
        // optimal tableau that column holds B^-1 times a unit vector, which is exactly the
        // column of B^-1 for this constraint.
        var sign = 1.0;
        var column = FindColumnByLabel("s" + (constraintRow + 1));

        if (column < 0)
            column = FindColumnByLabel("a" + (constraintRow + 1));

        if (column < 0)
        {
            // A surplus carries -1 rather than +1, so its tableau column is the negation.
            column = FindColumnByLabel("e" + (constraintRow + 1));
            sign = -1.0;
        }

        if (column < 0)
        {
            throw new LpException(
                $"Cannot locate the basis column for constraint {constraintRow + 1}, so its " +
                "right-hand-side range cannot be computed.");
        }

        // Raising this right-hand side by delta moves every basic value by delta times that
        // column. The basis stays optimal for as long as they all stay non-negative, so the
        // range runs from the tightest lower limit to the tightest upper one.
        var lowerDelta = double.NegativeInfinity;
        var upperDelta = double.PositiveInfinity;

        for (var r = 1; r < _optimal.RowCount; r++)
        {
            var direction = sign * grid[r, column];
            var basicValue = grid[r, rhsColumn];

            if (direction > 1e-9)
            {
                var limit = -basicValue / direction;
                if (limit > lowerDelta)
                    lowerDelta = limit;
            }
            else if (direction < -1e-9)
            {
                var limit = -basicValue / direction;
                if (limit < upperDelta)
                    upperDelta = limit;
            }
        }

        // The current value is the right-hand side the user wrote, not the basic variable
        // sitting in that row of the final tableau - those are different numbers.
        var current = _model != null
            ? _model.Constraints[constraintRow].RHS
            : grid[constraintRow + 1, rhsColumn];

        return new SensitivityRange(
            $"RHS of constraint {constraintRow + 1}",
            double.IsNegativeInfinity(lowerDelta) ? double.NegativeInfinity : Math.Round(current + lowerDelta, 6),
            double.IsPositiveInfinity(upperDelta) ? double.PositiveInfinity : Math.Round(current + upperDelta, 6),
            Math.Round(current, 6));
    }

    /// <summary>
    /// Apply and display a change of a selected constraint right-hand-side value.
    /// </summary>
    public SolveResult ChangeRhs(int constraintRow, double newValue)
    {
        ValidateConstraintRow(constraintRow);

        // A negative right-hand side is legitimate; the canonicalizer multiplies such a row
        // through by -1 and flips its relation.
        var modified = CloneModel();
        var original = modified.Constraints[constraintRow];

        modified.Constraints[constraintRow] =
            new Constraint(original.Coefficients, original.RelationalOperator, newValue);

        return ReSolve(
            modified,
            $"Right-hand side of constraint {constraintRow + 1} changed from " +
            $"{original.RHS:0.###} to {newValue:0.###}.");
    }

    /// <summary>
    /// Display the range of a selected variable in a Non-Basic Variable column.
    /// </summary>
    public SensitivityRange RangeOfCoefficientInNonBasicColumn(int column, int constraintRow)
    {
        ValidateColumn(column);
        ValidateConstraintRow(constraintRow);

        if (FindBasicRow(column) != -1)
        {
            throw new LpException(
                $"{ColumnName(column)} is basic. This range is only defined for a coefficient " +
                "inside a non-basic column.");
        }

        var variable = RangeableVariable(column);
        var current = RequireModel("A coefficient range").Constraints[constraintRow].Coefficients[variable];

        // A non-basic column is not part of the basis, so changing an entry in it leaves B and
        // therefore the simplex multipliers untouched. Only this column reduced cost moves,
        // by the dual value of that row for every unit the coefficient changes.
        var dual = InternalDualValue(constraintRow);
        var reduced = _optimal.Grid[0, column];

        if (Math.Abs(dual) < 1e-9)
        {
            // The row is not binding, so this coefficient does not affect optimality at all.
            return new SensitivityRange(
                $"Coefficient of x{variable + 1} in constraint {constraintRow + 1}",
                double.NegativeInfinity, double.PositiveInfinity, Math.Round(current, 6));
        }

        var limit = current - reduced / dual;

        return dual > 0
            ? new SensitivityRange($"Coefficient of x{variable + 1} in constraint {constraintRow + 1}",
                                   Math.Round(limit, 6), double.PositiveInfinity, Math.Round(current, 6))
            : new SensitivityRange($"Coefficient of x{variable + 1} in constraint {constraintRow + 1}",
                                   double.NegativeInfinity, Math.Round(limit, 6), Math.Round(current, 6));
    }

    /// <summary>
    /// The variable behind a column, rejecting the cases where one coefficient spans two
    /// columns and the single-column reasoning above would not hold.
    /// </summary>
    private int RangeableVariable(int column)
    {
        var variable = VariableIndexForColumn(column);
        var map = _optimal.VariableMap;

        if (map != null && variable < map.Length && map[variable].NegativeColumn >= 0)
        {
            throw new LpException(
                $"x{variable + 1} is unrestricted and is split across two columns, so its " +
                "coefficient cannot be ranged one column at a time.");
        }

        return variable;
    }

    private string CoefficientSubject(int variable) => $"Objective coefficient of x{variable + 1}";

    /// <summary>
    /// The dual value of a row in the internally normalised maximisation, without the flip
    /// that <see cref="ShadowPrices"/> applies before showing it to the user.
    /// </summary>
    private double InternalDualValue(int constraintRow)
    {
        var sign = 1.0;
        var column = FindColumnByLabel("s" + (constraintRow + 1));

        if (column < 0)
            column = FindColumnByLabel("a" + (constraintRow + 1));

        if (column < 0)
        {
            column = FindColumnByLabel("e" + (constraintRow + 1));
            sign = -1.0;
        }

        return column < 0 ? 0.0 : sign * _optimal.Grid[0, column];
    }

    /// <summary>
    /// Apply and display a change of a selected variable in a Non-Basic Variable column.
    /// </summary>
    public SolveResult ChangeCoefficientInNonBasicColumn(int column, int constraintRow, double newValue)
    {
        ValidateColumn(column);
        ValidateConstraintRow(constraintRow);

        if (FindBasicRow(column) != -1)
        {
            throw new LpException(
                $"{ColumnName(column)} is basic. A technological coefficient change is only " +
                "defined for a non-basic column.");
        }

        var variable = VariableIndexForColumn(column);
        var modified = CloneModel();
        var previous = modified.Constraints[constraintRow].Coefficients[variable];

        modified.Constraints[constraintRow].Coefficients[variable] = newValue;

        return ReSolve(
            modified,
            $"Coefficient of x{variable + 1} in constraint {constraintRow + 1} changed from " +
            $"{previous:0.###} to {newValue:0.###}.");
    }

    /// <summary>
    /// Add a new activity (a new decision variable column) to an optimal solution.
    /// </summary>
    public SolveResult AddActivity(double objectiveCoefficient, double[] constraintCoefficients)
    {
        if (constraintCoefficients == null)
            throw new LpException("Constraint coefficients array is null.");

        var modified = CloneModel();

        if (constraintCoefficients.Length != modified.Constraints.Count)
        {
            throw new LpException(
                $"Expected {modified.Constraints.Count} constraint coefficients, one per " +
                $"constraint, got {constraintCoefficients.Length}.");
        }

        // A new activity is a new decision variable: one more objective coefficient, one more
        // entry in every constraint, and one more sign restriction.
        modified.ObjectiveCoefficients.Add(objectiveCoefficient);

        for (var i = 0; i < modified.Constraints.Count; i++)
            modified.Constraints[i].Coefficients.Add(constraintCoefficients[i]);

        modified.Restrictions.Add(SignRestriction.Positive);

        return ReSolve(
            modified,
            $"New activity x{modified.ObjectiveCoefficients.Count} added with objective " +
            $"coefficient {objectiveCoefficient:0.###}.");
    }

    /// <summary>
    /// Add a new constraint to an optimal solution and re-optimise.
    /// </summary>
    public SolveResult AddConstraint(double[] coefficients, Relation relation, double rhs)
    {
        if (coefficients == null)
            throw new LpException("Coefficients array is null.");

        var modified = CloneModel();
        var variableCount = modified.DecisionVariableCount;

        // One coefficient per DECISION VARIABLE, not per grid column. The user adding a
        // constraint thinks in terms of x1..xn; slack and artificial columns are ours, not
        // theirs, and canonicalization will create whatever the new row needs.
        if (coefficients.Length != variableCount)
        {
            throw new LpException(
                $"Expected {variableCount} coefficients, one per decision variable, got " +
                $"{coefficients.Length}.");
        }

        modified.Constraints.Add(new Constraint(new List<double>(coefficients), relation, rhs));

        return ReSolve(
            modified,
            $"New constraint added as constraint {modified.Constraints.Count}.");
    }

    /// <summary>
    /// Display the shadow prices, one per constraint.
    /// </summary>
    public double[] ShadowPrices()
    {
        var grid = _optimal.Grid;
        var prices = new double[_numConstraints];

        for (var i = 0; i < _numConstraints; i++)
        {
            // The dual value of a row is the reduced cost of whichever added column carries
            // +1 in that row and zero elsewhere: the slack of a <= row, or the artificial of
            // a >= or = row. The surplus of a >= row carries -1, so its reduced cost is the
            // negation - the two are not interchangeable.
            var sign = 1.0;
            var column = FindColumnByLabel("s" + (i + 1));

            if (column < 0)
                column = FindColumnByLabel("a" + (i + 1));

            if (column < 0)
            {
                column = FindColumnByLabel("e" + (i + 1));
                sign = -1.0;
            }

            if (column < 0)
                continue;

            // Row 0 holds z_j - c_j, which for such a column is the dual value itself, with no
            // negation. For a Min problem the canonicalizer normalised the objective by
            // negating it, so the dual has to be negated back the same way the objective is.
            var dual = sign * grid[0, column];
            prices[i] = _optimal.OriginalObjectiveType == ProblemType.Min ? -dual : dual;
        }

        return prices.Select(p => Math.Round(p, 6)).ToArray();
    }

    private int FindColumnByLabel(string label)
    {
        for (var j = 0; j < _numColumns; j++)
        {
            if (_optimal.ColumnLabels[j] == label)
                return j;
        }

        return -1;
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
