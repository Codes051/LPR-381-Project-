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
| `unbounded.txt` | Must be reported as unbounded, not crash |
| `infeasible.txt` | Must be reported as infeasible, not crash |

The last two are there for the Error Handling marks — demo both on video.

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

## Status

Scaffolding only — every algorithm currently throws `NotImplementedException`. The menu
runs, so you can navigate the whole application and see exactly where your piece plugs in.
