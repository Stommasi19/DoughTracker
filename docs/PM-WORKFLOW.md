# Small-PR planning and handoffs

The current objective is a fresh C# backend built in small, understandable PRs
toward a locally installed, free OSS DoughTracker release.
The owner explicitly requested removing the previous implementation on October
5, 2026. This supersedes the incremental readability refactor and RB01. Retain
useful architecture, contracts and financial rules as references, and keep old
verification separate from rebuild progress.

The October 17, 2026 product revision is integrated into the existing
[rebuild checklist](BACKEND-TASKS.md) and [architecture](ARCHITECTURE.md): local
release identity, installer-owned Plaid credentials, outbound synchronization,
full local packaging and backup/upgrade/OSS distribution work. Continue the same
BR01 → BR02 path; this is not another reset or a parallel roadmap. The separate
shared-expense product remains out of scope. Budgets and broader personal finance
remain future DoughTracker capabilities.

Agents write implementation, tests and documentation. The owner chooses and
reviews the work, explains every changed file and traces the behavior that
ships. Do not reserve a required coding portion for the owner. Optional local
experiments can support learning, but do not make implementation depend on them.
Planning a feature or preparing a handoff does not authorize implementation.

## What makes a PR small

- One purpose, one observable behavior or deployment boundary, and one primary
  learning concept. The reset is a one-time exception in deletion size; its
  replacement implementation should be tiny.
- Explain the complete changed flow in one review. If understanding needs several
  separate sessions or the PR gains another purpose, split the scope further.
- Keep necessary verification and documentation with their behavior. Ownership,
  money correctness and data integrity must not be weakened to reduce file count.
- Add projects, packages and abstractions when an implemented behavior needs
  them. Do not rebuild the old project hierarchy as empty scaffolding.
- Preserve financial DTOs and rules as the target. The mandatory Firebase
  sign-in path is explicitly superseded by local identity and needs a frontend
  compatibility slice. State unavailable routes instead of implying the UI works.
- Keep dependency upgrades, UI changes, hosting and unrelated cleanup separate.

## C# readability

Use descriptive domain names, explicit blocks and intermediate results where
these help someone follow the code. Separate validation, data access, business
calculations and response construction when their flow becomes easier to trace.
Prefer focused methods with clear purposes; extra layers are not a requirement.
Comment financial and non-obvious rules: amount signs/refunds, owner scope, date
boundaries, cursor commits, synchronization locks and retries. Explain why the
rule exists rather than paraphrasing the next statement. Minimizing lines is not
the goal. Review readability as part of acceptance.

The eventual project boundaries stay in [ARCHITECTURE.md](ARCHITECTURE.md).
Introduce them progressively with the actual use cases that need them.
The [learning guide](LEARNING-WITH-AI.md) supplies comprehension techniques.

## Working loop

1. Read current code, working-tree changes, the active handoff and only relevant
   reference docs. Verify previous delivery from the diff and actual evidence.
2. Choose the smallest useful next slice of [BACKEND-TASKS.md](BACKEND-TASKS.md).
   Write one concrete handoff in `docs/handoffs/`, including exclusions, checks
   and an owner review checkpoint. State how it advances local OSS delivery and
   whether it needs external credentials; keep them out of source/images. Do not
   implement from a planning-only request.
3. An implementation agent writes the complete scoped change and handback.
   The owner reviews the entire diff and explains startup/request/data flow,
   the relevant rule, and one failure case. No mandatory handwritten component.
4. Record product checks and learning evidence separately. Missing evidence is
   pending, even when generated code or an agent explanation appears correct.
5. After owner review/merge, reconcile the result and prepare the next handoff.
   Adjust difficulty based on demonstrated understanding; keep PRs small throughout.

After a merge, use this prompt in this or a fresh repository conversation:

> PR #<number> merged. Review the diff and actual verification evidence, update
> the rebuild status, and write the next small handoff with a comprehension checkpoint.

Use root `AGENTS.md`, this workflow and the active handoff to recover context.
No previous chat or missing project-local PM skill is required.

## Mentor support for this rebuild

The owner requested the sibling Mentor plugin for implementation planning on
October 17, 2026. Use its explicitly invoked `mentor-interview` workflow for the
current requirements-only plan. It reuses this checklist and the BR02 handoff;
it does not start a second roadmap or authorize the implementation.

Keep the established allocation: agents implement scoped changes and checks;
the owner reviews and explains the complete flow. No required coding portion
or automatic switch to manual coding. Mentor currently resolves Junior coaching
with assisted coding; no preference has been saved or inferred from ability.
Offer explanations, predictions and review support proportionate to each PR.

The concrete next plan is recorded in BR02 under Implementation approach. Its
Mentor confirmation is pending, independently of this PR's authorized commit and
publication. BR01 owner review/merge remains the delivery prerequisite. After
confirmation, finalize the plugin's private interview/plan artifacts outside Git.
Do not publish the private transcript, preference files or machine-specific paths.

Mentor is a development aid, not a runtime dependency. Public repository plans
must remain usable without that plugin. Future full interviews run only when
explicitly requested; ordinary explanations and review need no special invocation.

## Handoff format

Replace placeholders with concrete details. Each handoff should be short enough
that the owner can explain why every part belongs in this PR.

```md
# <task-id> — <one-PR title>

Status: Ready for implementation
Roadmap: <link to the current rebuild checklist>
Baseline: <branch/commit and relevant existing local changes>
Prerequisites: <delivered dependencies or required inputs>

## Why this task
Before: <concrete current limitation>
After: <one observable result>

## Scope
- <Required implementation, relevant checks and documentation>
Excluded: <adjacent work reserved for later PRs>

## Code and references to inspect
- <Relevant existing file/symbol or durable contract>

## Learning checkpoint
Focus: <one primary concept>
Before review: <what to predict>
Afterward: <flow and file purposes to explain; one failure to diagnose>
Agent practice: <scope or verification decision to assess>
Agents implement everything; owner coding is optional.

## Acceptance checks
- [ ] <Observable result, including relevant failure/ownership cases>
- [ ] <Owner comprehension; record independently of product checks>

## Verification
<Exact commands, expected results and necessary focused regression checks>
<Unavailable external checks stay pending>

## Implementation handback
Report why each file changed, actual commands/results and remaining limitations.
Include a short PR description around the final change. Record what the owner
explained or diagnosed and any learning gap. Do not infer mastery from generated
work. Report required scope expansion separately instead of absorbing another task.
```

## Current planning baseline

Baseline: `main` at `e70dc11`, inspected October 5, 2026, followed by the local
BR01 reset. The owner's existing `.gitignore` excludes `docs/`, `skills/` and root
`AGENTS.md`. The October 17 request to commit and PR the reset and plan authorizes
explicitly tracking the current Markdown references and AGENTS.md for review;
leave the ignore rules intact for other local files. Private Mentor artifacts
remain outside Git.

Current task: [BR01 — reset and API foundation](handoffs/BR01-fresh-backend-foundation.md).
Implementation, Release build/publish and HTTP checks passed using the pinned
SDK in Docker. Owner review/comprehension and merge remain pending. Actual
commands and limitations are recorded in that handoff. The next scoped
handoff is [BR02 — containerize only the API](handoffs/BR02-containerize-api.md).
Preparing BR02 does not implement it or establish owner understanding of BR01.

The old local API/PostgreSQL Compose services were stopped before their
configuration was removed. Their saved volumes and local credentials were not
deleted. Future storage work uses a fresh rebuild volume/database explicitly.

Milestones 1 and 2 are historical references. Their old source and tests are gone;
Git history retains them. Their checks are not checks of the replacement. RB01
is superseded. R01 and B12-01 are superseded as implementation handoffs: their
useful packaging goals are incorporated into the current full-local-package
slices, with built assets and real local identity rather than Vite/emulators.
Do not execute their old owner coding assignments, Firebase setup or hosted
release assumptions. The earlier Saturday deadline and Railway/Azure release
path are not active. Current architecture/checklist requirements take precedence
over historical reference assumptions.

Track the selected plan/reference Markdown files for this PR under the owner's
explicit commit request. Do not force-add unrelated local files or alter the
ignore rules. Before OSS distribution, implement the roadmap's public
README/configuration/operations documentation and license slice so installation
does not depend on development planning documents. No feature, license choice, live Plaid
verification or owner comprehension is completed by this planning revision.

## Current learning baseline

BR01 startup/request comprehension is pending owner review. Previous component
assignments are no longer active. Do not infer lack of ability from choosing a
reset, and do not infer mastery from the generated starter or passing checks.

For each reviewed PR, record briefly what the owner can explain, what failure
case they can diagnose and what still needs practice. Use those observations to
choose the next concept and scope; no manual-writing quota or PR-count graduation.
