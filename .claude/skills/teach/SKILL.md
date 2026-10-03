---
name: teach
description: Teach-back for a QA Lab milestone or topic. Plain-language summary, a 5-question quiz asked one at a time, and results logged to docs/LEARNING.md.
disable-model-invocation: true
---

Topic: $ARGUMENTS (if empty, use the milestone just finished)

1. Explain what was built in ≤ 12 lines, for a smart beginner. Name the 3 most important files and what each one does.
2. Quiz: 5 questions, **one at a time**, waiting for each answer.
   - 2 "what does this do?" questions about real code (give file:line).
   - 2 "why this design / what's the trade-off?" questions.
   - 1 "what breaks if…?" question.
3. After each answer, say: correct / partly / not yet. Give the correct answer in 1–3 lines, and point to file:line.
4. Ask Sora to write 3–5 sentences explaining the milestone in his own words. Suggest one fix if anything in it is wrong.
5. Append to `docs/LEARNING.md` under "Entries":
   - date, topic, score x/5;
   - his explanation (verbatim);
   - gaps.
   Add the gaps to the "Revisit" list.
