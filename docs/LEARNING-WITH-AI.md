# Learning software engineering through DoughTracker

Researched: 2026-10-05. Goal: build an entry-level engineer's independent ability
to understand, change, debug, and verify software while using AI effectively.

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
2. **Own one part.** Write or modify one meaningful piece yourself: a Dockerfile,
   query, validation branch, or regression check. Use official docs and ask AI
   conceptual questions. Keep this small enough to finish; AI can implement the
   familiar surrounding work. If it already generated the change, make a related
   small variation yourself.
3. **Use feedback while working.** Run the relevant command or test. Read the
   actual error, state a hypothesis, then test it. Ask for an explanation or next
   diagnostic step before asking AI to fix an unfamiliar failure.
4. **Review the actual diff.** Explain why each changed file is necessary. Check
   the important behavior independently of the agent's summary. For new logic,
   inspect that its regression check would fail if the behavior were broken.
   Another AI review is useful feedback; it does not establish your understanding.
5. **Explain without the generated answer.** Close the AI explanation and describe
   the flow, one tradeoff, and one failure case in your own words. Write the PR
   description yourself; AI may edit it afterward. Recognizing an explanation
   while reading it is a weaker check than reconstructing it yourself.
6. **Return to it.** A couple of days later, explain the central idea again and
   try a small variation without code generation. Recall practice has support in
   general learning research; applying it to this coding workflow is our inference.
   [Roediger and Karpicke's retention experiments](https://pubmed.ncbi.nlm.nih.gov/16507066/).

Record only three short notes in the PR or handback: what you now understand,
what you personally changed/debugged, and what still needs practice. No separate
tracking system is needed. A passing product check and a learning gap can coexist.

## How we increase difficulty

| Stage | Your responsibility | AI's useful role | Evidence to move forward |
| --- | --- | --- | --- |
| One unfamiliar concept | Trace a small flow and write one focused part | Explain concepts, provide hints, review your attempt | Explain the result and diagnose a related failure |
| A complete small behavior | Draft the approach, implement the main change, choose checks | Critique the plan, help with familiar repetition | Make a related variation and explain the test's failure case |
| A broader feature | Decompose it into small PRs, reason about boundaries and recovery | Explore alternatives and implement scoped pieces | Explain interactions, tradeoffs, and how to recover from failures |

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

For earlier large milestone PRs, learn one existing flow at a time. You do not
need to understand the whole system in one sitting, and we will not assume an
agent-built feature demonstrates your mastery of it.

For the current R01 release handoff, agents implement most of the changes while
you wire the public Firebase build settings. Trace the complete React-to-runtime
flow, review each changed file, and verify public frontend access and protected
API access. Understanding, debugging and assessing the solution matter more than
the number of lines you type. Leave AI-free exercises on financial writes or
lifecycle behavior in disposable test environments.

For extra study, use the relevant MIT Missing Semester lecture alongside its
matching task rather than collecting many courses. Keep some hands-on practice
in the project's C#, TypeScript, SQL, and tests; prompt-writing alone cannot
demonstrate those abilities.

## A prompt for learning sessions

```text
I'm learning this concept through DoughTracker. First ask me to explain my
current model and predict the result. Review my attempt and point out gaps.
Give me a diagnostic step or a small hint before a complete solution.
Once I understand it, ask me to make a related variation and explain one
failure case. Distinguish facts from assumptions and cite official docs for
library behavior. Don't mistake my agreement with your explanation for proof
that I can apply it.
```

Use that prompt when tutoring. The implementation handoff still defines the
authorized work for implementation agents; this adds learning practice, not an
extra permission requirement before they can perform that work.
