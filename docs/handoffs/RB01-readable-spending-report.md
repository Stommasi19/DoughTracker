# RB01 — Make spending reports readable without changing their results

> October 17, 2026: this historical refactor is superseded by the
> [local OSS rebuild](../BACKEND-TASKS.md). Source links point to the pre-reset
> revision because those implementations are intentionally removed from this PR.

> Historical handoff. Follow [the current workflow](../PM-WORKFLOW.md);
> prior owner coding assignments and backend file paths are no longer active.

Status: Superseded by the owner-requested fresh backend rebuild (BR01). Do not implement this legacy refactor.
Roadmap: [incremental backend readability work](../PM-WORKFLOW.md)
Baseline: `main` at `e70dc11`. Preserve the user's local `.gitignore` change.
Prerequisites: existing .NET 10 SDK and Application test project; no provider,
database or hosting credentials needed for this slice.

## Why this task

The current calculator combines filtering, summary figures, grouping and charts
in one compressed method. Both the stored ledger and demo workspace use it.
Make that flow understandable before moving to database and provider code.

Before: a posted purchase of -100 and refund of +30 produce spending of 70,
but the calculations are buried in a long positional constructor expression.
After: the same report values come from clearly named steps and focused methods,
with comments explaining the financial and calendar rules.

## Scope and ownership

Production scope: `DoughTracker/src/Application/SpendingReport.cs` only.
Verification scope: the existing `SpendingReportTests.cs`. Keep the public
`Calculate` signature compatible and `ExpenseReport` fields unchanged.

**Your component: the body of `SpendingReport.Calculate`.** Select the eligible
posted expense transactions and current/previous date windows. Calculate the
seven headline figures: spending, previous spending, percentage change, pending
spending, refunds, daily average and expense count. Call the agent's helpers and
assemble the report with clearly identified fields. Write this part yourself;
ask for explanations or hints and have the agent review your attempt.

**Agent components: four private helpers in the same class**, for category
spending, merchant spending, cumulative daily spending and the six-month trend.
Agents also extend and run the regression tests. Use descriptive helper names
and parameters; no new service hierarchy, public types or dependencies. Agree
on their inputs/outputs before editing the shared file. Agents leave `Calculate`
for you and preserve your work when integrating their helpers.

Use domain names such as `postedExpenseTransactions` and `previousPeriodSpending`
rather than `rows`, `t` and `prior` where the meaning is otherwise unclear.
Give calculations and constructor arguments clear names. Use explicit blocks
when they make the rule easier to follow. Add short comments for business rules,
not a comment for every statement. More lines are acceptable when they help.

Excluded: frontend/API changes, query/store refactors, migrations, renaming domain
DTOs, new features, new validation semantics, performance tuning, provider or
messaging changes, Docker and deployment work. Report suspected bugs separately
rather than changing behavior inside this PR.

## Behavior to preserve

- Use `decimal`. The ledger supplies negative purchases and positive refunds.
  Net spending is the negative sum; refunds can make spending negative.
- Posted totals include only non-removed, non-pending `expense` transactions.
  Income, transfers and card payments stay excluded. Pending spending is a
  separate total of eligible pending expenses in the current window.
- Date windows include their start and exclude their end. The previous window
  starts at `previousFrom` and ends at the current window's start. Callers already
  supply valid ranges and separate currencies; keep those responsibilities there.
- Refunds total positive posted expense amounts in the current window. Expense
  count includes only negative posted expense amounts. Daily average uses the
  number of calendar days and rounds to two decimal places.
- Percentage change is null when previous spending is zero. Otherwise use the
  existing difference divided by the absolute previous amount, rounded to one
  decimal place. Preserve the existing decimal rounding behavior.
- Category and merchant amounts use net posted expenses; group counts include
  refunds. Honor `CategoryId` and `Merchant` from the transaction record, including
  manual overrides and merchant-description fallback. Unknown category names
  remain `Uncategorized`. Sort by amount descending, then ID; keep five merchants.
- Daily values are cumulative. Cap the comparison day's cutoff at the previous
  window length; on the final current day include the entire previous window.
  This matters when calendar months have different lengths.
- Trend includes six calendar months ending with the current window's start
  month. Keep the existing month labels and all report array ordering.

## Code to inspect

- [SpendingReport.cs](https://github.com/Stommasi19/DoughTracker/blob/e70dc11b427b3422c58340a3629ae17ab0e0e385/DoughTracker/src/Application/SpendingReport.cs): the
  current implementation and sole production file to change.
- [DemoLedger.cs](https://github.com/Stommasi19/DoughTracker/blob/e70dc11b427b3422c58340a3629ae17ab0e0e385/DoughTracker/src/Domain/DemoLedger.cs): report fields,
  effective category and merchant properties; read only.
- [LedgerQueries.cs](https://github.com/Stommasi19/DoughTracker/blob/e70dc11b427b3422c58340a3629ae17ab0e0e385/DoughTracker/src/Application/LedgerQueries.cs): `Report`
  selects dates, fetches data and splits currencies; read only.
- [DemoWorkspace.cs](https://github.com/Stommasi19/DoughTracker/blob/e70dc11b427b3422c58340a3629ae17ab0e0e385/DoughTracker/src/Application/DemoWorkspace.cs): the
  other calculator caller; read only.
- [SpendingReportTests.cs](https://github.com/Stommasi19/DoughTracker/blob/e70dc11b427b3422c58340a3629ae17ab0e0e385/DoughTracker/tests/Application.Tests/SpendingReportTests.cs):
  existing refund, exclusion, empty-window and unequal-month examples.

## Learning checkpoint

Focus: translating financial rules into readable, testable pure calculations.

Before implementation, predict the existing fixture's headline totals. Explain
which rows contribute to each figure and which date window contains each row.
Then write your component using the existing implementation as a behavior
reference, without copying its compressed structure.

Afterward, trace the complete calculator, including the agent's helpers, without
reading an AI explanation. Explain why a refund lowers net spending while
remaining positive in the refund total, and why pending spending is separate.
Predict and check a case where refunds exceed purchases. If your implementation
fails a check, state your hypothesis before asking for a fix; otherwise diagnose
a temporary sign or date-boundary mistake locally and restore correct behavior.

Agent practice: assess whether the proposed helper boundaries make the flow
easier to follow. Identify one regression test and explain what incorrect result
would make it fail. Do not accept an agent summary as a substitute for reading
the full small diff.

## Acceptance checks

- [ ] Existing callers compile, report fields stay unchanged, and current tests pass.
- [ ] The entry method reads as filtering → headline figures → helpers → report.
      Names identify the domain meaning; comments explain sign/date rules.
- [ ] Helpers preserve the grouping, sorting, fallback and chart rules above.
- [ ] Focused tests cover missing boundary and refund-exceeds-purchase cases,
      plus relevant gaps in grouping/chart coverage. Keep the existing empty and
      unequal-month checks. Assert observable results rather than helper structure.
- [ ] The owner can explain the complete report flow and their own implementation
      decisions. Record any learning gap separately from passing product checks.

## Verification

Run the focused tests before editing to establish the baseline, then after the
refactor. Run the full Application project after assembling the complete PR:

```sh
dotnet test DoughTracker/tests/Application.Tests/Application.Tests.csproj --filter FullyQualifiedName~SpendingReportTests
dotnet test DoughTracker/tests/Application.Tests/Application.Tests.csproj
```

These commands are required implementation checks, not checks already run by
the PM. Add independent expected-result cases for gaps in existing coverage;
do not copy the old implementation into tests as the expected-value calculator.

## Agent handoff prompt

```text
Follow docs/handoffs/RB01-readable-spending-report.md. I own the Calculate method
body: filtering, headline figures, helper calls and report assembly. You own the
four private grouping/chart helpers and regression tests. Give a short helper
interface plan first. Leave my method for me; offer hints and review when asked.
Preserve current report behavior, my work and the public contracts. Explain your
helpers, run the specified checks, and report actual results and remaining gaps.
```

## Implementation handback

Return the small diff, why each file changed, commands/results and pending checks.
Include a short PR description explaining the readability problem, preserved
behavior and validation. Record what the owner independently explained, wrote
or diagnosed, and what remains unclear. No mastery claim from generated code
alone. Bring the merged PR back to the PM before choosing the next slice.
