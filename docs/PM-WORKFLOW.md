# Small-PR planning and handoffs

The user owns implementation and uses other agents to do it. The PM chooses and
explains the next task, then writes a Markdown handoff for one reviewable PR.
Planning a task does not authorize the PM to implement it.

The task should also build the user's independent engineering skills. Follow
[the research-backed learning workflow](LEARNING-WITH-AI.md): keep PRs small as
topic complexity and the user's responsibility grow.

Keep the user's existing agent workflow. Each handoff practices both engineering
and directing an agent: the user explains the intended outcome and constraints,
delegates the scoped work, then assesses the actual diff and verification evidence.
Improve the workflow when results reveal a concrete gap; do not replace it or add
ceremony just because a different prompting style exists.

Agents implement most of the work. The user owns technical decisions and must
understand the complete small PR: trace its behavior, explain each changed file,
assess verification evidence, and diagnose or modify relevant behavior. Assign
one bounded, meaningful production-code portion when hands-on implementation
helps the learning target. Name its scope explicitly; neither token edits nor
writing the entire feature should be the default. There is no manual-writing
quota. Agents can explain, implement surrounding work and review; they give
hints on the learner's reserved portion unless the user delegates it.

## Working loop

1. When the user asks for the next task, read the current code, working-tree
   changes, and relevant project docs. Confirm the previous task's outcome from
   the diff and verification evidence; do not assume a plan was implemented.
2. Choose the smallest useful next slice of the existing roadmap. Write one
   handoff at `docs/handoffs/<task-id>-<short-name>.md` and give the user its link.
   Include one primary learning target, a learner-owned exercise, and a short
   explanation/debugging checkpoint. Use evidence of learning to adjust difficulty.
3. The user takes that handoff to an implementation agent and reviews the PR.
4. When the user returns, reconcile the result with the handoff and roadmap,
   then prepare the next task. Record missing checks as pending.

After each merge, prompt the PM in this chat:

> PR #<number> merged. Review the changes and verification evidence, then write
> the next small Markdown handoff with my learning exercise and agent checkpoint.

This manual trigger uses the existing chat workflow; no API automation is needed.

The [backend checklist](BACKEND-TASKS.md) tracks milestones. A checklist task can
span several PRs: use IDs such as `B07-01`, `B07-02`, and so on. Mark a checklist
item complete only when its behavior and checks are actually delivered.
The [architecture](ARCHITECTURE.md) remains the source for system boundaries.

## What makes a task small

- One purpose, one explainable behavior change, and explicit acceptance checks.
- Include the verification and documentation needed for that behavior in the
  same PR. A migration and its code may belong together.
- Prefer a usable, verifiable slice over empty types or scaffolding for later.
- Name what is excluded so an implementation agent cannot silently absorb the
  rest of a milestone. Keep unrelated cleanup and dependency upgrades separate.
- Reuse existing code and dependencies. Avoid speculative abstractions.
- If the diff gains a second independent purpose, split that purpose into a
  follow-up handoff. Do not compromise ownership, money correctness, or data
  integrity just to reduce the file count.

## Handoff format

Replace every placeholder with concrete project details before handing this to
an implementation agent. Keep it short enough for the user to understand why
each part of the PR exists.

```md
# <task-id> — <one-PR title>

Status: Ready for implementation
Roadmap: <parent task and link>
Baseline: <branch/commit and relevant existing local changes>
Prerequisites: <delivered dependencies or required inputs; none if applicable>

## Why this task
<Current limitation, resulting behavior, and why this is the next step.>
Before: <concrete example of today's behavior>
After: <same example after this PR>

## Scope
- <Required change>

Excluded: <adjacent work reserved for later PRs>

## Code to inspect
- <Existing file/symbol and its role in the flow>

## Learning checkpoint
Focus: <one primary concept>
Before implementation: <what to predict or trace>
Your exercise: <one meaningful part to write/change yourself>
Afterward: <one explanation and one failure/variation to diagnose>
Agent practice: <one scope, direction, or verification decision for the user to assess>

## Acceptance checks
- [ ] <Observable result, including relevant failure/ownership cases>

## Verification
<Exact runnable commands and any manual steps with expected results.>
<A focused regression check for new nontrivial behavior; reuse existing tests.>

## Implementation handback
Report changed behavior, why each file changed, checks run and their results,
and any remaining limitations. Record what the user independently explained or
changed, and any learning gaps; generated work alone is not evidence of mastery.
Include a short PR description explaining the
problem, resulting behavior, and validation. Do not mark pending checks passed.
Keep the PR within this scope; report any necessary scope expansion separately.
```

## Current planning baseline

Milestone 2 is merged into `main` at `70989dd`. Its controlled local checks are
recorded in [BANK_CONNECTIONS.md](BANK_CONNECTIONS.md); live Sandbox and remote
webhook checks remain pending. B12 remains incomplete. The active task is
[R01 — Serve the built frontend](handoffs/R01-serve-built-frontend.md).
The local Compose frontend task is deferred for the hosted release.
These are recorded results, not a claim of fresh runtime verification.

The user is targeting a hosted expense tracker for friends and family by
Saturday, October 10, 2026, with Railway as the likely host. Scope is the existing
expense tracker: sign-in, accounts, transactions, manual categories, and spending
reports. The user will set up Firebase and Plaid; readiness is still unverified.
Prefer changes required for that release over local convenience work.
Keep the learner-owned exercise small enough to support delivery; larger study
sessions and retrospective exercises can follow the release.

## Release sequence

Issue one concrete handoff at a time. The following are planning priorities,
not additional assigned tasks or promises about completion dates:

1. R01: package and serve the built frontend in the existing backend image.
2. Add explicit hosted/real-bank provider configuration with fail-closed checks.
   The current code hard-codes Sandbox and rejects Plaid outside Development;
   changing those boundaries is a separate tested PR. Verify actual Transactions
   and applicable institution/OAuth access in the user's Plaid account.
3. Configure Railway app/PostgreSQL/RabbitMQ, real Firebase auth, explicit database
   migration deployment, HTTPS/webhook and OAuth URLs, and persistent token/key
   storage with working runtime permissions. Hosting setup may need more than
   one PR. PostgreSQL connection strings must use the format Npgsql expects.
4. Run the hosted expense-tracker acceptance checks with two real identities,
   including owner isolation, category persistence, reconnect, restart recovery,
   and successful use from another device. Verify data/token backup recovery
   before inviting users; record any release blocker candidly.

External setup can proceed alongside R01: Firebase web app and sign-in providers,
backend Admin credentials, Railway account/project, and real Plaid Transactions
access. Do not assume Sandbox credentials support real accounts. Railway billing
and the user's Plaid access/limits must be confirmed before provisioning a paid
release. No credential values belong in these documents.
