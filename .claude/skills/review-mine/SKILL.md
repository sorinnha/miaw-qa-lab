---
name: review-mine
description: Review code Sora wrote himself (YOU WRITE tasks) without rewriting it. Test results, hints, and one interview-style question.
disable-model-invocation: true
---

Target: $ARGUMENTS (if empty, review the uncommitted changes)

1. Run the related tests and lint. Report pass/fail in 2 lines.
2. Feedback in this order, only what matters:
   - correctness bugs;
   - edge cases the tests miss;
   - readability;
   - performance, only if it's relevant.
3. Give **hints, not solutions**: point to the line and ask a leading question. Show at most a 3-line snippet, and only after two attempts on the same problem.
4. When the tests pass, remove the `youwrite` marker (Python) or `[Category("YouWrite")]` (C#) from those tests, and fill in the row in `docs/LEARNING.md` → "Written by me": milestone, file, function, date.
5. Ask one question an interviewer might ask about this code (for example, "why that regex order?" or "what's the complexity?").
