# Start here

**What this is:** a ready-made project kit. Claude Code on your PC builds **Miaw QA Lab**, an AI-assisted QA toolkit for Unity, one milestone at a time. You learn and own every line. Everything maps to the Ubisoft Pune Junior R&D (Gen AI/ML) job description.

**Do this first, today:** apply to the Junior R&D role. Don't wait for the project. The note to send is in `docs/OUTREACH.md` (template 1).

## What you'll build (5 weeks at 2–3 h/day)

1. **Unity package (C#):** logs, metrics and screenshots, plus a seeded bot that plays your game and detectors for falls, stuck players, frame spikes and tunnelling.
2. **Triage (Python):** turns thousands of log lines into ranked, unique bugs, with a local LLM and RAG writing Jira-ready reports that cite evidence.
3. **Vision (Python):** finds missing textures, black screens and broken UI in screenshots using heuristics and a VLM.
4. **Benchmark:** 16 seeded bugs, so you can say real numbers in the interview.

| Week | You'll have |
|---|---|
| 1 | Unity writes logs + Python validates them |
| 2 | **v0.1.0**: AI bug reports. Send the link to recruiters |
| 3 | **v0.2.0**: one command runs a bot playtest and produces a report |
| 4 | Measured results (triage + vision) |
| 5 | **v1.0.0**: runs on your real game, demo video, mock interviews |

## What you need on your PC

- Windows 10/11, 16 GB RAM recommended (8 GB works with small models)
- Git, Python 3.12, VS Code or Rider
- Unity Hub + the Unity version your games use (Unity 6 LTS is fine for the sandbox)
- **Ollama** (free, runs models locally). Or an API key for a hosted model, kept in `.env`
- Your Crimson Tactics or Dead District project (week 5)
- An empty **public** GitHub repo named `miaw-qa-lab`

No decisions are needed upfront. In the first session Claude Code checks your PC and recommends the model setup.

## Steps

1. Unzip. Move the `miaw-qa-lab` folder wherever you keep projects.
2. Open a terminal **in `miaw-qa-lab`** and start Claude Code.
3. Paste prompt **P0** from `docs/PROMPTS.md`. Answer its questions and approve the model choice.
4. Type `/milestone M0`. Read the plan, then say **go**.
5. Repeat for M1 to M8. After each one, run `/teach`. Use `/status` any time.
6. Do every **YOU WRITE** task yourself (8 small ones). Then run `/review-mine`.

## Faster option: build in the cloud

Claude Code cloud sessions (claude.ai/code, included in Pro) can build and test the code for M0–M6 while your PC stays free. Your PC still runs Unity, Ollama and the PowerShell scripts, and you still do the YOU WRITE tasks and `/teach`. Setup and prompts: `docs/PROMPTS.md` → Cloud sessions.

## Files in the kit

| File | For |
|---|---|
| `CLAUDE.md` | Rules Claude Code follows every session (teaching mode, tests, honesty, no secrets) |
| `docs/PLAN.md` | Milestones M0–M8, what "done" means, what to cut if you're short on time |
| `docs/specs/00–04` | Exact designs: data formats, Unity package, triage, vision, CI |
| `docs/INTERVIEW_PREP.md` | 60-second pitch, JD → evidence, 30 questions with hints, patterns you used |
| `docs/OUTREACH.md` | Application note, LinkedIn messages, email to jobsindia@ubisoft.com |
| `docs/PROMPTS.md` | Everything you paste into Claude Code |
| `schemas/`, `samples/` | Data contracts + a sample run, already validated |
| `.claude/skills/` | Your commands: `/milestone` `/teach` `/explain` `/review-mine` `/mock` `/status` |

## Rules

- Never paste API keys into chat, and never commit `.env`.
- If you can't explain a file, run `/explain` on it before moving on.
- Each day: 1.5–2 h building, 30 min Python practice, 5 min in `docs/LEARNING.md`.
