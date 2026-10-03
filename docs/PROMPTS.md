# Prompts to paste into Claude Code

Open a terminal in the `miaw-qa-lab` folder and start Claude Code. It reads `CLAUDE.md` automatically. The `/commands` below come from `.claude/skills/`. If one doesn't show up, paste the plain-text version instead.

## P0: first session (paste once)

```
Read CLAUDE.md, docs/PLAN.md and docs/specs/00_contracts.md.
This is the first session: check my machine (OS, CPU, RAM, GPU, Python, Git, Unity editors in Unity Hub, Ollama and models),
find my Unity game projects (ask me if you can't), and write docs/ENVIRONMENT.md.
Recommend which local model setup fits this PC for text, embeddings and vision, with reasons, and wait for my OK.
Then init git if needed and show me the M0 plan. Don't start coding until I say go.
```

## Milestones

| When | Type this | Do this yourself first |
|---|---|---|
| Day 1 | `/milestone M0` | Install Git, Python 3.12, Ollama (or have an API key ready). Create an empty public GitHub repo `miaw-qa-lab` |
| Day 3 | `/milestone M1` | In Unity Hub create `unity/QALabSandbox` (Universal 3D template, same version as your games) |
| Day 6 | `/milestone M2` | — |
| Day 9 | `/milestone M3` | `ollama pull` the models Claude Code recommended; put any API key in `.env` yourself |
| Day 13 | `/milestone M4` | Close the Unity Editor before running batch-mode builds |
| Day 18 | `/milestone M5` | Leave the PC free for about an hour while the benchmark runs |
| Day 21 | `/milestone M6` | — |
| Day 26 | `/milestone M7` | Back up your game project; work on a new branch in it |
| Day 28+ | `/mock unity`, `/mock triage`, … then `/mock full` | — |

Plain-text version of `/milestone`:

```
Start milestone M<N> from docs/PLAN.md. Read its section and the specs it references, create branch m<N>-<slug>,
show me a plan (files, steps, my YOU WRITE tasks, risks, how we verify) and wait for "go".
```

## Every day

```
/status
```

## After each milestone

```
/teach
```

Plain text: "Teach me what we built in this milestone: a short summary, then 5 quiz questions one at a time, then log the result in docs/LEARNING.md."

## When you don't understand something

```
/explain python/src/qalab/triage/normalize.py
/explain why the log callback can't call Unity APIs
```

## After you finish a YOU WRITE task

```
/review-mine python/src/qalab/triage/normalize.py
```

## Useful one-liners

- "Run all tests and lint and tell me what's failing, in 5 lines."
- "Something broke. Reproduce it with a failing test first, then fix it, and explain the cause in 3 lines."
- "Before we continue: is anything in this milestone different from the spec? Update PLAN.md change log if so."
- "Show me the git diff of this milestone, grouped by file, with one line per file explaining why it changed."
- "Pretend you're the interviewer: what are 3 questions you'd ask about the code we wrote today?"
- "Write the commit message for these changes (Conventional Commits) and commit."
- "We're behind schedule. Using the 'If you're short on time' rules in PLAN.md, what should we cut?"

## Cloud sessions (claude.ai/code)

Claude Code builds one milestone per session on a cloud VM while your PC stays free. Its rules are in CLAUDE.md → Cloud mode. Needs a Pro, Max, Team or Enterprise plan.

**Once**
1. Push this repo to GitHub (public `miaw-qa-lab`).
2. Open claude.ai/code, connect GitHub, and install the Claude GitHub App on `miaw-qa-lab` (it lets Claude fix CI failures on its own PRs). The Default environment is fine.
3. Optional: paste the setup script at the bottom into the environment's settings, so C# sessions start with .NET ready.

**Each milestone**, in order M0 → M6 (M7 and M8 are PC-only). Before M1, create `unity/QALabSandbox` in Unity Hub on your PC (PLAN.md → M1) and push it.
1. New session on `main`. Mode: **Auto** (or **Accept edits**).
2. Model: on **Pro**, keep the default (Opus 5.5, included in your plan). Fable isn't included in Pro; it bills pay-as-you-go usage credits, so don't type `/model fable` unless you mean to pay. If you hit your plan's limit, the session stops; type `continue` after the reset.
3. Paste C1 with the milestone number.
4. When the PR is up: turn on **Auto-fix** in the CI bar, read the PR, and merge once CI is green.
5. On your PC: `git pull`, start Claude Code, paste C2.

Faster: after M0 is merged, M1 and M2 can run as two sessions at once, then M3 + M4, then M5 + M6. That uses your limits faster.

### C1: build a milestone in the cloud (change M0 to the milestone)

```
Cloud build session: milestone M0 of Miaw QA Lab.

Read CLAUDE.md first; its "Cloud mode" section overrides the local workflow. Then read docs/PLAN.md → M0, docs/ARCHITECTURE.md and every spec section M0 points to, in full, before writing code.

Goal: build everything in M0 that can be built and verified on this VM, exactly to spec, as a senior engineer's PR that a Python beginner can learn from. Take the time you need; correctness beats speed. I'm not watching; I'll review the PR and do the PC steps later.

1. Plan in ≤ 10 lines (files, steps, YOU WRITE stubs, PC-only items). Turn the spec into a checklist in your task list, then start without waiting for me.
2. Build in small vertical slices. After each one: ruff check, ruff format --check, pytest (plus cs-check when there's C#), then a Conventional Commit ending in "(M0)". Push every few commits.
3. Tests run offline and are deterministic. Where samples/sample_run/EXPECTED.md has numbers for this milestone, assert them.
4. When it's built, have a fresh-context subagent review the whole diff against the spec and CLAUDE.md: bugs, spec gaps, missing tests, Windows paths, label leakage, any YOU WRITE solution left in. Fix what it finds and re-run everything.
5. Run every M0 acceptance check that can run here and paste the real output into the PR. Anything that needs Unity, Ollama, PowerShell or Windows goes in the PC checklist with exact commands. Never claim a result you didn't get.
6. Open a draft PR to main titled "M0: <short title>" with the body from Cloud mode. If you can't open PRs, tell me to press Create PR. Never merge, tag or push to main.

Stop to ask me only for a schema change, a dependency outside spec 02/03, or a spec error that blocks the milestone. Decide everything else, log it in docs/DECISIONS.md, and keep going. Final message: ≤ 10 lines.
```

### C2: on your PC, after merging a cloud PR

```
I merged the cloud PR for M<N>. Pull main and install anything new (venv, packages).
Then take me through the PR's PC checklist one step at a time. Run each step yourself where you can (scripts, Unity batch mode, Ollama); tell me when a step needs my hands in the Unity Editor.
Fix what fails, failing test first. Then list my open YOU WRITE tasks (pytest python -m youwrite -rxX) and help me do them with hints only.
When we're done, remind me to run /review-mine and /teach.
```

### Optional environment setup script

```bash
#!/bin/bash
# .NET 8 SDK for tools/cs-check (M1, M4). Cached after the first session.
apt-get update -qq && apt-get install -y -qq dotnet-sdk-8.0 || true
```

## Rules for you (Sora)

- Never paste API keys into the chat. Put them in `.env` yourself.
- Read every plan before typing "go". Ask "why?" whenever a step isn't clear. That's what makes it your project.
- Do every YOU WRITE task yourself, even if it's slow.
