---
name: status
description: Show QA Lab progress against docs/PLAN.md. Done, in progress, the next concrete step, open YOU WRITE tasks and risks.
disable-model-invocation: true
---

Read `docs/PLAN.md`, `docs/LEARNING.md`, the last 15 commits (`git log --oneline -15`), the current branch, and whether the tests pass.

Reply in 15 lines or fewer, with no preamble:
- a milestone table M0–M8 marked ✓ done, … in progress, – not started;
- the single next concrete step;
- open YOU WRITE tasks (`pytest python -m youwrite -rxX`: x = open);
- days left against the PLAN timeline;
- any risk or blocker (for example, a failing test, a missing model or an unanswered decision).
