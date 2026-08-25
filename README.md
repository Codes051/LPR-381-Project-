# LPR381 Project — `solve.exe`

A menu-driven .NET console application that solves Linear and Integer Programming models,
displays every algorithm iteration, and performs sensitivity analysis on the optimal solution.

Work split across the three of us: see [WORK_SPLIT.md](WORK_SPLIT.md).

## Prerequisites

The .NET 8 SDK. Either:

- **Visual Studio 2022 or 2026** with the **.NET desktop development** workload (this
  installs the SDK for you), or
- the standalone **.NET 8 SDK** from https://dotnet.microsoft.com/download/dotnet/8.0

> Only the Visual Studio **Build Tools** are currently installed on this machine, which
> ships MSBuild but no .NET SDK. Install one of the two above before building.

## Build and run

Open `solve.sln` in Visual Studio and press F5, or from a terminal:

```bash
dotnet run --project src/solve/solve.csproj
```

To produce the executable the brief asks for:

```bash
dotnet build -c Release
```

The output lands at `src/solve/bin/Release/net8.0/solve.exe`, with the sample models
copied alongside it.

## Project layout

```
solve.sln
src/solve/
  Program.cs                  entry point, thin by design
  Models/                     shared contracts — everyone reads these       [A owns]
    Enums.cs                  ProblemType, Relation, SignRestriction, VariableType
    ParsedLP.cs               the model as written in the input file
    Constraint.cs             coefficients + relation + RHS + added variables
    AddedVariable.cs          a slack / surplus / artificial and its column
    CanonicalMatrix.cs        the finished grid handed to solvers
    Tableau.cs                one captured iteration, for the output file
    SolveResult.cs            what every algorithm returns
  Parsing/
    LpParser.cs               input file -> ParsedLP                        [A]
    Canonicalizer.cs          ParsedLP -> CanonicalMatrix                   [A]
  Output/
    OutputWriter.cs           shared writer, 3-decimal rounding             [A]
  Algorithms/
    ISolver.cs                the contract every algorithm implements
    Simplex/
      PrimalSimplexSolver.cs                                                [A]
      RevisedPrimalSimplexSolver.cs                                         [A]
    CuttingPlane/
      CuttingPlaneSolver.cs                                                 [A]
    BranchAndBound/
      BranchAndBoundNode.cs   shared tree machinery for both B&B algorithms [B]
      BranchAndBoundSimplexSolver.cs                                        [B]
      KnapsackBranchAndBoundSolver.cs                                       [B]
    NonLinear/
      NonLinearSolver.cs      bonus                                         [C]
  Sensitivity/
    SensitivityAnalyzer.cs    the eleven range/change operations            [C]
    DualityAnalyzer.cs        build, solve and verify the dual              [C]
  UI/
    MainMenu.cs               load / solve / analyse / export               [C]
    SensitivityMenu.cs        one entry per marked operation                [C]
  Exceptions/
    LpException.cs            user-facing failures, caught by the menu
samples/                      test models, copied next to the exe
```

## Sample models

| File | Purpose |
|---|---|
| `knapsack_ip.txt` | The binary knapsack example from the brief |
| `lp_max.txt` | Plain max LP, all `<=`, exercises slack variables |
| `lp_min_mixed.txt` | Min problem with `=`, `>=` and `<=` — surplus and artificial variables |
| `ip_integer.txt` | Integer (not binary) model for branch & bound and cutting plane |
| `lp_max_4var.txt` | 4 variables, 4 constraints — a wider model for the "random amount of variables" criterion (z = 113.846) |
| `lp_min_5con.txt` | 4 variables, 5 constraints, all three relations, Min objective — the heaviest two-phase case (z = 22) |
| `t_urs.txt` | An `urs` variable whose optimum is negative (z = 8 at x2 = −3) |
| `t_negative.txt` | A `-` variable, required to be non-positive (z = 17 at x2 = −7) |
| `t_degenerate.txt` | Two constraints tight at the optimum — exercises the anti-cycling tie-break (z = 18) |
| `t_equalities.txt` | Every row an `=`, so the basis is entirely artificial (z = 23) |
| `t_altoptima.txt` | Objective parallel to a constraint, so a whole edge is optimal (z = 16) |
| `unbounded.txt` | Must be reported as unbounded, not crash |
| `infeasible.txt` | Must be reported as infeasible, not crash |

The last two are there for the Error Handling marks — demo both on video. The optimal
values quoted above were cross-checked against brute-force enumeration of every basic
feasible solution, so they are safe to assert against in a test.

## Ground rules for the group

1. **Nobody edits another person's files.** Ownership is marked in a header comment at the
   top of every file and in the table above.
2. **`Models/`, `ISolver.cs` and `OutputWriter.cs` are frozen contracts.** Changing the
   shape of `SolveResult` or `CanonicalMatrix` breaks all three workstreams — raise it with
   the group first.
3. **Branch per person** (`person-a`, `person-b`, `person-c`), merge to `main` at each
   checkpoint in WORK_SPLIT.md. Never commit straight to `main`.
4. **Every algorithm records every iteration.** The brief marks the displayed working, not
   just the answer. A solver that returns the right number with an empty `Iterations` list
   is worth close to nothing.
5. **Never let an exception reach the user.** Throw `LpException` with a readable message;
   the menu catches it. Crashes cost the Error Handling marks.

## Contract details that are easy to get wrong

Six things about the shared types that aren't obvious from their signatures. Each one fails
silently rather than loudly, so read them before writing a solver.

**Your first recorded tableau must be the canonical form.** `OutputWriter.WriteResult`
prints `SolveResult.Iterations` in order and nothing else — that list is the only thing
that puts the canonical form in the output file. Every algorithm criterion in the brief
starts with "display the canonical form", so a solver that starts recording at its first
pivot silently loses those marks:

```csharp
var result = new SolveResult(Name);
result.Iterations.Add(Tableau.Snapshot("Canonical Form", model));   // <- before the loop
```

**Indexing is not uniform across `CanonicalMatrix`.** `ColumnLabels`, `IsIntegerMask` and
`IsBinaryMask` are all indexed by *grid column* and all have length `ColumnCount`, so index
`j` means the same thing in all three. `BasicVariables` is the exception — it has one entry
per *constraint*, so `BasicVariables[i]` is the basic column of grid row `i + 1`:

```csharp
for (var row = 1; row < model.RowCount; row++)
{
    var basicColumn = model.BasicVariables[row - 1];      // note the -1
    var name = model.ColumnLabels[basicColumn];
}
```

**Never call `ToString()` on a double — always `OutputWriter.Format(value)`.** Our machines
are set to a locale that uses a comma as the decimal separator, so a raw `ToString("0.000")`
renders `4.000` as `4,000`. In a tableau that reads as four thousand, and it contradicts the
input file format, which uses a point. `Format()` pins invariant culture and does the
three-decimal rounding the brief requires, in one place.

**Ask `ColumnTypes`, never the column label.** `CanonicalMatrix.ColumnTypes[j]` says whether
a column is a decision, slack, surplus or artificial variable, with `IsArtificial(j)` and
`DecisionVariableCount` as shortcuts. The labels (`x1`, `s1`, `e2`, `a3`) encode the same
thing, but they exist to be read by a human — branching on a display string breaks silently
the first time one is reworded.

**Grid columns are not a one-to-one match for the variables in the input file.** A variable
declared `urs` is split into two columns (`x2+` and `x2-`) and one declared `-` is stored as
its own negation (`x2'`), because the simplex can only handle non-negative variables. Read
`CanonicalMatrix.VariableMap` to get back from columns to variables, or better, call
`RecoverOriginalValues(columnValues)` and let it do it:

```csharp
var columnValues = new double[model.ColumnCount];
for (var r = 1; r < model.RowCount; r++)
    columnValues[model.BasicVariables[r - 1]] = model.Grid[r, model.RhsColumn];

var answer = model.RecoverOriginalValues(columnValues);   // indexed by variable, not column
```

Assuming column j is variable j gives a wrong answer with no error, and only on models that
use `urs` or `-`.

**A binary model has no `x <= 1` rows in its canonical form.** `bin` sets `IsBinaryMask`, and
nothing else — the upper bound is never written into the grid. So the LP relaxation of
`knapsack_ip.txt` is *not* the knapsack relaxation you want: it puts `x3 = 6.667` and reports
`z = 20`, because nothing stops a variable exceeding 1. Person B: whichever branch-and-bound
you point at a binary model has to supply those bounds itself, either as explicit rows or in
the bounding rule.

The cutting plane solves this by calling `CanonicalMatrix.WithExtraConstraint(...)` once per
binary column before its first solve. That method is general — it appends a row and
whatever slack, surplus or artificial columns the relation needs, returning a new model
and leaving the original alone. Person C: that is exactly what the "add a new constraint
to an optimal solution" sensitivity operation needs, so use it rather than writing a
second one.

## Status

| Component | State |
|---|---|
| Parser, canonicalizer, output writer | **Done** — all samples parse, canonical grids hand-checked, malformed input reports readable errors |
| Primal simplex (two-phase) | **Done** — verified against hand-worked answers, including infeasible and unbounded detection |
| Revised primal simplex | **Done** — product form and price out displayed each iteration; agrees with the tableau simplex and with brute force |
| Cutting plane (revised, Gomory) | **Done** — agrees with exhaustive integer search on both integer samples; adds the missing binary bounds itself |
| Branch & bound simplex, branch & bound knapsack | Not started (Person B) |
| Sensitivity analysis, duality, menus, non-linear bonus | Not started (Person C) |

The menu runs. Unimplemented algorithms report `Not built yet` instead of crashing, so you
can navigate the whole application and see exactly where your piece plugs in.

### Building without the .NET SDK

If you have the SDK, use `dotnet build` and ignore this. If you only have Visual Studio
**Build Tools**, `dotnet` does not exist on your machine at all — but the project still
compiles and runs, because we use no SDK-only APIs. `tools/build-check.ps1` drives the
Roslyn compiler directly against the .NET Framework reference assemblies:

```bash
powershell -NoProfile -File tools/build-check.ps1
```

It also accepts piped menu keystrokes, which is the quickest way to exercise a solver
without clicking through the menu every time — here, load the knapsack and solve it with
menu option 5:

```bash
powershell -NoProfile -File tools/build-check.ps1 -Run -StdIn "1|samples/knapsack_ip.txt|2|5|0"
```
