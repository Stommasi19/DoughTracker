# Learning software engineering through DoughTracker

Researched: 2026-10-05. Goal: build an entry-level engineer's independent ability
to understand, change, debug, and verify software while using AI effectively.

Current project workflow: agents write the complete implementation, tests and
documentation. The owner reviews and demonstrates understanding; there is no
required coding portion. The October 5 backend reset supersedes the previous
component-writing assignments. The research below remains background evidence,
while the routine here reflects the current owner/agent agreement.

Keep PRs small as your skills grow. Increase the complexity of the problems and
your ownership of the solution. Larger diffs are not a graduation requirement.

## What the evidence supports

- Google's engineering guidance favors focused changes, keeps relevant tests with
  their behavior, and defines smallness conceptually rather than by line count.
  [Google: Small CLs](https://google.github.io/eng-practices/review/developer/small-cls.html).
- DORA's guidance connects small batches with faster feedback and explicitly
  recommends them as a countermeasure to instability as AI accelerates delivery.
  This is delivery guidance, not a controlled study of beginner learning.
  [DORA: Working in small batches](https://dora.dev/capabilities/working-in-small-batches/).
- Anthropic's January 2026 randomized study recruited 52 mostly junior Python
  engineers learning Trio. Immediate quiz averages were 50% with AI versus 67%
  without it, a 17-percentage-point gap. The largest gap was debugging. Explanation
  and conceptual-question patterns were associated with stronger comprehension;
  those usage patterns were not separately randomized. The small, short study
  does not establish long-term effects or results for every coding tool.
  [Anthropic: Coding skill formation](https://www.anthropic.com/research/AI-assistance-coding-skills).
- A 2025 survey of 319 knowledge workers associated greater confidence in AI with
  less critical thinking. It measures self-reports and associations, not proof
  that AI causes skill loss.
  [Microsoft Research: Critical thinking](https://www.microsoft.com/en-us/research/publication/the-impact-of-generative-ai-on-critical-thinking-self-reported-reductions-in-cognitive-effort-and-confidence-effects-from-a-survey-of-knowledge-workers/).
- Current training examples combine fundamentals with AI: CodePath's pathway
  progresses from CS fundamentals to evaluating generated code and larger systems.
  MIT's 2026 Missing Semester covers shell, debugging, Git, shipping, and agentic
  coding. These are examples of current practice, not evidence that one curriculum
  is the best.
  [CodePath](https://www.codepath.org/courses/applied-ai-engineering),
  [MIT's 2026 syllabus](https://missing.csail.mit.edu/2026/).

The workflow below is our practical adaptation of that evidence. Its timings and
progression criteria are coaching choices, not scientifically established cutoffs.

## The routine for each PR

1. **Predict before generating.** Spend roughly 10–15 minutes reading the relevant
   code and docs. Write the current behavior, desired behavior, likely files,
   and one failure case. Ask AI for hints when stuck; the timer is not a rule to
   keep struggling without help.
2. **Own the review.** Have the agent implement the complete scoped PR. Trace
   one request, calculation or deployment path through every changed file. Ask
   conceptual questions and compare the actual code with the intended behavior.
   Optional small experiments can deepen understanding; no code is reserved for
   you, and agents do not wait for a handwritten portion.
3. **Use feedback while working.** Run the relevant command or test. Read the
   actual error, state a hypothesis, then test it. Ask for an explanation or next
   diagnostic step before asking AI to fix an unfamiliar failure.
4. **Review the actual diff.** Explain why each changed file is necessary. Check
   the important behavior independently of the agent's summary. For new logic,
   inspect that its regression check would fail if the behavior were broken.
   Another AI review is useful feedback; it does not establish your understanding.
5. **Explain without the generated answer.** Close the AI explanation and describe
   the flow, one tradeoff, and one failure case in your own words. Check that
   the agent's PR description matches the actual diff and results. Recognizing
   an explanation while reading it is a weaker check than reconstructing it yourself.
6. **Return to it.** A couple of days later, explain the central idea again and
   predict a small variation's result before consulting the agent. Recall practice has support in
   general learning research; applying it to this coding workflow is our inference.
   [Roediger and Karpicke's retention experiments](https://pubmed.ncbi.nlm.nih.gov/16507066/).

Record only three short notes in the PR or handback: what you now understand,
what failure you diagnosed, and what still needs practice. No separate
tracking system is needed. A passing product check and a learning gap can coexist.

## How we increase difficulty

| Stage | Your responsibility | AI's useful role | Evidence to move forward |
| --- | --- | --- | --- |
| One unfamiliar concept | Trace a complete small flow and its file purposes | Implement, explain concepts and provide diagnostic hints | Explain the result and diagnose a related failure |
| A complete small behavior | Assess the approach, full diff and verification evidence | Implement and verify the behavior; critique reasoning | Predict a variation and explain the check's failure case |
| A broader feature | Decompose into small PRs and reason about boundaries/recovery | Implement each scoped slice and explore tradeoffs | Explain interactions and how to recover from failures |

Advance based on repeated demonstrations, not elapsed weeks, generated line
counts, or a quota of completed PRs. Increase one difficulty at a time. A new
domain, tool, or concurrency problem may need more coaching even after familiar
tasks become independent. Continue small PRs at every stage.

## Apply this to the project

Each handoff has one primary learning target tied to useful project work.

| Topic | DoughTracker practice |
| --- | --- |
| Git and change review | Compare the branch with its base; explain the purpose of each file |
| HTTP and networking | Trace browser → Vite proxy → API, including which process resolves each address |
| Authentication and authorization | Trace a bearer token to the verified owner and an owner-scoped read |
| SQL and persistence | Follow a category override through the update, stored row, and report |
| Financial correctness | Predict spending for a purchase, refund, transfer, and pending transaction; check the result |
| Background work | Follow one sync request through run/outbox/consumer to data and cursor commit |
| Failure recovery | Explain what survives a failed batch, broker outage, or restart |

The backend is restarting. Keep earlier milestone rules and contracts as
references, rather than assigning study of all the deleted code. Start with
[BR01](handoffs/BR01-fresh-backend-foundation.md): explain the Web SDK, executable
startup, endpoint mapping, JSON response, launch profile and port. Distinguish
connection refused from a 404. Then use [BR02](handoffs/BR02-containerize-api.md)
for build/runtime stages and port mapping after reviewing the foundation.

Optional experiments in C#, TypeScript, SQL or checks can support learning. Keep
financial-write and lifecycle experiments in disposable environments. Assess
understanding through independent explanations, predicted results and diagnostic
reasoning; generated code and prompt-writing alone do not establish mastery.
Use relevant study material alongside its task instead of collecting many courses.

## A prompt for learning sessions

```text
I'm learning this concept through DoughTracker. First ask me to explain my
current model and predict the result. Review my attempt and point out gaps.
Give me a diagnostic step or a small hint before a complete solution.
Once I understand it, ask me to predict a related variation and explain one
failure case. Coding experiments are optional; agents implement the complete PR. Distinguish facts from assumptions and cite official docs for
library behavior. Don't mistake my agreement with your explanation for proof
that I can apply it.
```

Use that prompt when tutoring. The implementation handoff still defines the
authorized work for implementation agents; this adds learning practice, not an
extra permission requirement before they can perform that work.
