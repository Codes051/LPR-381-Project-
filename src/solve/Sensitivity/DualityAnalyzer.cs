using Solve.Models;
using Solve.Algorithms.Simplex;
using Solve.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Solve.Sensitivity;

/// <summary>
/// Applies duality to the programming model. Builds the dual, solves it,
/// and verifies strong or weak duality.
/// </summary>
public class DualityAnalyzer
{
    /// <summary>
    /// Build the dual of the given model. Max becomes Min, rows become columns.
    /// </summary>
    public ParsedLP BuildDual(ParsedLP primal)
    {
        if (primal == null)
            throw new LpException("Primal model is null.");

        // A maximization problem's dual is a minimization problem and vice versa
        ProblemType dualType = primal.ObjectiveType == ProblemType.Max ? ProblemType.Min : ProblemType.Max;

        int numVars = primal.DecisionVariableCount;
        int numConstraints = primal.Constraints.Count;

        // Build dual objective coefficients from primal RHS values
        var dualObjectiveCoeffs = new List<double>();
        for (int i = 0; i < numConstraints; i++)
        {
            dualObjectiveCoeffs.Add(primal.Constraints[i].RHS);
        }

        // Build dual constraints from primal variables
        var dualConstraints = new List<Constraint>();

        // Each primal variable becomes a dual constraint
        for (int j = 0; j < numVars; j++)
        {
            var dualCoeffs = new List<double>();

            // The coefficients come from the j-th column of primal constraints
            for (int i = 0; i < numConstraints; i++)
            {
                // If primal constraint i has coefficient for variable j
                double coeff = 0;
                if (j < primal.Constraints[i].Coefficients.Count)
                {
                    coeff = primal.Constraints[i].Coefficients[j];
                }
                dualCoeffs.Add(coeff);
            }

            // Determine relation based on primal variable sign restriction
            Relation dualRelation;
            switch (primal.Restrictions[j])
            {
                case SignRestriction.Positive:
                    // Primal variable >= 0 => dual constraint >= (for Max primal)
                    // or <= (for Min primal) depending on primal objective type
                    dualRelation = primal.ObjectiveType == ProblemType.Max ? Relation.GEQ : Relation.LEQ;
                    break;
                case SignRestriction.Negative:
                    dualRelation = primal.ObjectiveType == ProblemType.Max ? Relation.LEQ : Relation.GEQ;
                    break;
                case SignRestriction.Unrestricted:
                    dualRelation = Relation.EQ;
                    break;
                default:
                    dualRelation = primal.ObjectiveType == ProblemType.Max ? Relation.GEQ : Relation.LEQ;
                    break;
            }

            // RHS comes from primal objective coefficient for variable j
            double dualRhs = 0;
            if (j < primal.ObjectiveCoefficients.Count)
            {
                dualRhs = primal.ObjectiveCoefficients[j];

                // No sign flipping here. The transpose is already complete: the relation was
                // chosen above from the primal variable's sign restriction, and the dual
                // variables get their own restrictions from the primal constraint types below.
                // Negating both the coefficients and the right-hand side without also
                // reversing the relation turns A'y <= c into A'y >= c, which is a different
                // problem - it made the dual of a Min model unbounded.
            }

            dualConstraints.Add(new Constraint(dualCoeffs, dualRelation, dualRhs));
        }

        // Build dual sign restrictions from primal constraints
        var dualRestrictions = new List<SignRestriction>();
        foreach (var constraint in primal.Constraints)
        {
            // Each primal constraint becomes a dual variable
            // The sign restriction depends on the constraint type and primal objective
            if (primal.ObjectiveType == ProblemType.Max)
            {
                // For Max primal: 
                // <= constraint -> dual variable >= 0
                // >= constraint -> dual variable <= 0
                // = constraint -> unrestricted
                switch (constraint.RelationalOperator)
                {
                    case Relation.LEQ:
                        dualRestrictions.Add(SignRestriction.Positive);
                        break;
                    case Relation.GEQ:
                        dualRestrictions.Add(SignRestriction.Negative);
                        break;
                    case Relation.EQ:
                        dualRestrictions.Add(SignRestriction.Unrestricted);
                        break;
                }
            }
            else // Min primal
            {
                // For Min primal:
                // <= constraint -> dual variable <= 0
                // >= constraint -> dual variable >= 0
                // = constraint -> unrestricted
                switch (constraint.RelationalOperator)
                {
                    case Relation.LEQ:
                        dualRestrictions.Add(SignRestriction.Negative);
                        break;
                    case Relation.GEQ:
                        dualRestrictions.Add(SignRestriction.Positive);
                        break;
                    case Relation.EQ:
                        dualRestrictions.Add(SignRestriction.Unrestricted);
                        break;
                }
            }
        }

        return new ParsedLP(dualType, dualObjectiveCoeffs, dualConstraints, dualRestrictions);
    }

    /// <summary>
    /// Canonicalize and solve the dual using the primal simplex.
    /// </summary>
    public SolveResult SolveDual(ParsedLP primal)
    {
        if (primal == null)
            throw new LpException("Primal model is null.");

        var dual = BuildDual(primal);
        var canonicalizer = new Parsing.Canonicalizer();
        var canonical = canonicalizer.ToCanonicalForm(dual);

        var solver = new PrimalSimplexSolver();
        if (!solver.CanSolve(canonical))
            throw new LpException("Dual model cannot be solved by Primal Simplex.");

        return solver.Solve(canonical);
    }

    /// <summary>
    /// Compare the primal and dual objective values. Equal means strong duality;
    /// a gap means weak duality. Round before comparing - the values come from
    /// floating point pivots and will not match exactly.
    /// </summary>
    public string VerifyDuality(SolveResult primalResult, SolveResult dualResult)
    {
        if (primalResult == null)
            throw new LpException("Primal result is null.");
        if (dualResult == null)
            throw new LpException("Dual result is null.");

        if (primalResult.Status != SolutionStatus.Optimal)
            throw new LpException("Primal did not solve to optimality.");
        if (dualResult.Status != SolutionStatus.Optimal)
            throw new LpException("Dual did not solve to optimality.");

        // Both should be optimal
        double primalObj = Math.Round(primalResult.ObjectiveValue, 6);
        double dualObj = Math.Round(dualResult.ObjectiveValue, 6);

        const double epsilon = 1e-6;

        string dualityStatus;
        if (Math.Abs(primalObj - dualObj) < epsilon)
        {
            dualityStatus = "Strong Duality";
        }
        else if (primalObj < dualObj && primalResult.AlgorithmName.Contains("Max") ||
                 primalObj > dualObj && primalResult.AlgorithmName.Contains("Min"))
        {
            dualityStatus = "Weak Duality (primal objective is " +
                (primalObj < dualObj ? "less than" : "greater than") + " dual objective)";
        }
        else
        {
            dualityStatus = "No clear duality relationship detected.";
        }

        return $@"=== Duality Verification ===
Primal Objective Value: {Output.OutputWriter.Format(primalObj)}
Dual Objective Value:   {Output.OutputWriter.Format(dualObj)}
Status: {dualityStatus}

{(dualResult.FinalTableau != null ? "Dual model was solved successfully." : "No final tableau available.")}";
    }
}
