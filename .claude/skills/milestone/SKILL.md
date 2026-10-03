---
name: milestone
description: Start or continue a QA Lab milestone (M0–M8) from docs/PLAN.md. Use when Sora types /milestone M<number>.
disable-model-invocation: true
---

Milestone: $ARGUMENTS

In a cloud session (`CLAUDE_CODE_REMOTE_SESSION_ID` is set), CLAUDE.md → Cloud mode replaces steps 2, 3, 5 and 8.

1. Read the `$ARGUMENTS` section of `docs/PLAN.md`, the specs it points to, and CLAUDE.md's rules. Check `git status` and the current branch.
2. If the branch `m<N>-<slug>` doesn't exist, create it from main.
3. Post a plan of 15 lines at most:
   - goal;
   - files to create or change;
   - steps (≤ 10);
   - the YOU WRITE task(s) and when they come up;
   - risks or spikes;
   - how we'll verify (the acceptance checks).
   Then **wait for Sora to say "go"** or ask for changes.
4. Build in small steps. After each step: run the relevant tests and lint, give a 3-line summary, and commit (Conventional Commits).
5. When you reach a YOU WRITE task:
   - create the signature, docstring and failing tests (marked `youwrite` in Python, `[Category("YouWrite")]` in C#);
   - commit them as `test: ... (YOU WRITE)`;
   - explain what's needed in ≤ 5 bullets with one link to the docs;
   - stop.
   Review his attempt with `/review-mine` style hints. Don't write the solution unless he asks twice.
6. If something in the spec turns out wrong or impossible, stop and propose a change. Once agreed, log it in PLAN.md → Change log and in DECISIONS.md.
7. Finish:
   - run every acceptance check and show the results;
   - update README, DECISIONS, EVAL_RESULTS and CHANGELOG as needed;
   - tick the milestone in PLAN.md;
   - run the `/teach` flow.
8. Ask before merging to main, tagging a release, or pushing.
