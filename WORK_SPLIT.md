# LPR381 Project — Work Split (3 people)

Deliverable: `solve.exe`, a menu-driven .NET console app that reads an LP/IP text file,
solves it with a chosen algorithm, runs sensitivity analysis, and writes everything to
an output text file (3 decimal places).

## Confirmed mark allocation

| Criteria | Weight |
|---|---|
| Outline | 2 |
| Input File | 3 |
| Output File | 2 |
| Primal Simplex Algorithm | 4 |
| Revised Primal Simplex Algorithm | 4 |
| Branch & Bound Simplex (or Revised) | **20** |
| Branch & Bound Knapsack | **16** |
| Cutting Plane (or Revised) | **14** |
| Sensitivity Analysis | **25** |
| Error Handling + special cases | 5 |
| Interface presentation | 5 |
| **Total** | **100** |
| Non-linear problem solved | +10 bonus |

**Read this before splitting anything.** The two simplex algorithms are worth 8 marks
combined. Sensitivity Analysis alone is worth more than both simplex algorithms, the
input file, the output file, error handling and the interface *put together*. The four
big-ticket items — Sensitivity 25, B&B Simplex 20, Knapsack 16, Cutting Plane 14 — are
75% of the mark. Effort should follow that, not follow what feels like "the hard part".

---

## 0. Shared foundation — agree on this BEFORE anyone writes solver code

The data model is already designed in `LP_Parser.pdf`. Person A owns it, but all three
must agree on these contracts in the first sitting, because everything else plugs in here:

```csharp
// Owned by A, consumed by B and C
ProblemType, Relation, SignRestriction, VariableType
AddedVariable(VariableType Type, double Coefficient, int ColumnIndex)
Constraint(List<double> Coefficients, Relation RelationalOperator, double RHS)
ParsedLP(ProblemType, List<double> ObjectiveCoefficients, List<Constraint>, List<SignRestriction>)
CanonicalMatrix(double[,] Grid, int[] BasicVariables, List<string> ColumnLabels,
                bool[] IsIntegerMask, bool[] IsBinaryMask)

// The contract every algorithm implements
interface ISolver {
    string Name { get; }
    SolveResult Solve(CanonicalMatrix model);
}

// What every algorithm hands back
class SolveResult {
    SolutionStatus Status;          // Optimal | Infeasible | Unbounded
    double ObjectiveValue;          // A flips the sign back for Min problems
    double[] VariableValues;
    List<Tableau> Iterations;       // every tableau / product-form step, in order
    CanonicalMatrix FinalTableau;   // what sensitivity analysis reads
}
```

Rules: nobody edits another person's files; everything goes through `ISolver`,
`SolveResult`, and A's output writer. Branch per person, merge at each checkpoint.

---

## Person A — Pipeline + Simplex engine + Cutting Plane · **34 marks**

Owns the backbone. The parser and canonicalizer must land first — B and C are blocked
on them — but note the simplex work itself is cheap in marks, so **get it working, not
perfect, then move to Cutting Plane, which is worth 3.5× as much.**

| Task | Marks |
|---|---|
| **Parser** — first line max/min + signed coefficients; constraint lines parsed in reverse (RHS, relation, coefficients); restrictions line found via `reader.Peek() == -1` | 3 |
| **Canonicalizer** — slack/surplus/artificial, shared column counter, objective row sign flip, Min→Max, masks + labels | — |
| **Output file writer** — shared utility everyone calls; canonical form + all iterations, 3 dp | 2 |
| **Primal Simplex** — canonical form + all tableau iterations | 4 |
| **Revised Primal Simplex** — all Product Form and Price Out iterations | 4 |
| **Cutting Plane (Revised)** — Gomory cuts on the revised engine; all product form + price out iterations | **14** |
| **Error handling** — infeasible / unbounded detected and reported, not crashed | 5 |
| Outline / documentation criteria | 2 |

*Why this grouping:* Cutting Plane is a direct extension of the revised simplex engine.
Same person, same code, 14 extra marks for comparatively little extra work — this is the
best value in the project.

---

## Person B — Everything Branch & Bound · **36 marks**

| Task | Marks |
|---|---|
| **B&B Simplex** — backtracking; generate *all* sub-problems; fathom *all* nodes; print every sub-problem's tableau iterations; display best candidate | **20** |
| **B&B Knapsack** — same requirements: backtracking, all sub-problems, all nodes fathomed, all table iterations, best candidate. Reads `IsBinaryMask` | **16** |

*Why this grouping:* both are branch & bound. One shared node/tree/branching/fathoming/
backtracking framework serves both, with a different bounding routine plugged in — LP
relaxation via A's simplex for one, ratio-ordered knapsack bound for the other. Written
by two different people this framework gets built twice and diverges.

Only two items, but they are the two heaviest algorithms and the marking explicitly
demands the full tree, not just the answer. Start on Knapsack first — it needs nothing
from A, so B is never idle waiting on the parser, and it is the algorithm the brief's
example file is built for.

---

## Person C — Sensitivity Analysis + Interface + Submission · **30 marks + 10 bonus**

The single largest line item on the sheet, and twelve distinct operations to build.

| Task | Marks |
|---|---|
| **Sensitivity Analysis** — reads `SolveResult.FinalTableau`: range + apply-change for (a) non-basic variable, (b) basic variable, (c) constraint RHS, (d) a variable inside a non-basic column; add a new activity; add a new constraint; display shadow prices; **duality** — build the dual, solve it, verify strong vs weak | **25** |
| **Menu-driven interface** — file load → algorithm choice → solve → sensitivity submenu → write output | 5 |
| **Non-linear bonus** — e.g. f(x)=x²; be ready to *explain the code* on video (the brief requires that for this part only) | +10 |
| **Video + submission PDF** — demonstrate every criterion, fast; plus the non-contribution PDF if needed | — |

C can start on the interface shell and the sensitivity maths on paper from day one, then
wire up to `FinalTableau` once A's primal simplex produces one.

---

## Checkpoints

| # | Milestone | Who |
|---|---|---|
| 1 | Data model + `ISolver`/`SolveResult` agreed and committed | All three |
| 2 | Knapsack B&B solving the brief's example IP | B (needs nobody) |
| 3 | Parser + canonicalizer working on the example file | A (unblocks everyone) |
| 4 | Primal + Revised Primal solving end-to-end, output file written | A |
| 5 | B&B Simplex with full tree and fathoming | B |
| 6 | Cutting Plane on the revised engine | A |
| 7 | Full 12-operation sensitivity suite + duality | C |
| 8 | Menu polish, error handling, non-linear bonus | C, A |
| 9 | Integration freeze — record the video | All three |

---

## If time runs short

Protect in this order: **Sensitivity Analysis (25) → B&B Simplex (20) → Knapsack (16) →
Cutting Plane (14)**. Everything else on the sheet combined is 21 marks. Do not let
anyone spend a week perfecting the Revised Primal Simplex — it is worth 4.
