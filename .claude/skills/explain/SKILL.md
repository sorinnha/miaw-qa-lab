---
name: explain
description: Explain a file, function or concept from this repo at Sora's level (comfortable with C#/Unity, new to Python), with a tiny example.
disable-model-invocation: true
---

Explain: $ARGUMENTS

1. What it's for, in 2 lines.
2. Walk through the code top to bottom in plain words. For a long file, cover the main parts and skip the obvious.
3. When it's Python, compare it with the C#/Unity equivalent (for example, list comprehension vs LINQ `Select`, `Protocol` vs interface, `with` vs `using`).
4. Show one tiny runnable example (≤ 10 lines) that Sora can paste into a Python REPL or a Unity script.
5. End with 2 check questions. Don't answer them unless he asks.
