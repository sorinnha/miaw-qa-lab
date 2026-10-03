from pathlib import Path

from qalab.rag.code_context import code_context_for, find_line, resolve_source, snippet
from qalab.triage.stack import Frame

SOURCE = """using UnityEngine;

namespace QALab.Sandbox
{
    public class SeededDoor : MonoBehaviour
    {
        private Animator _animator;

        public void Open()
        {
            _animator.SetTrigger("Open"); // line 11
        }
    }
}
""" + "\n".join(f"// filler {i}" for i in range(60))


def _repo(tmp_path: Path) -> Path:
    repo = tmp_path / "game"
    path = repo / "Project" / "Assets" / "Sandbox" / "Scripts" / "SeededDoor.cs"
    path.parent.mkdir(parents=True)
    path.write_text(SOURCE, encoding="utf-8")
    return repo


def test_resolve_source_tries_root_then_nested(tmp_path: Path) -> None:
    repo = _repo(tmp_path)
    nested = resolve_source("Assets/Sandbox/Scripts/SeededDoor.cs", repo)
    assert nested == repo / "Project" / "Assets" / "Sandbox" / "Scripts" / "SeededDoor.cs"
    assert resolve_source("Assets\\Sandbox\\Scripts\\SeededDoor.cs", repo) == nested
    root = repo / "Assets" / "Sandbox" / "Scripts" / "SeededDoor.cs"
    root.parent.mkdir(parents=True)
    root.write_text("x", encoding="utf-8")
    assert resolve_source("Assets/Sandbox/Scripts/SeededDoor.cs", repo) == root
    assert resolve_source("Assets/Nope.cs", repo) is None


def test_snippet_window_marker_and_cap(tmp_path: Path) -> None:
    path = _repo(tmp_path) / "Project" / "Assets" / "Sandbox" / "Scripts" / "SeededDoor.cs"
    text = snippet(path, 11)
    lines = text.splitlines()
    assert lines[0].endswith("SeededDoor.cs:11")
    assert lines[1].startswith("  3 |") and lines[-1].startswith(" 19 |")
    assert ">11 |" in text and 'SetTrigger("Open")' in text
    assert len(snippet(path, 30, radius=50, max_lines=40).splitlines()) == 41
    assert snippet(path, 999).splitlines()[0].endswith(f":{len(SOURCE.splitlines())}")


def test_find_line_by_class_and_method() -> None:
    lines = SOURCE.splitlines()
    assert find_line(lines, "SeededDoor", "Open") == 9
    assert find_line(lines, "SeededDoor", "Missing") == 5
    assert find_line(lines, "Other", "Open") is None


def test_code_context_for_frames(tmp_path: Path) -> None:
    repo = _repo(tmp_path)
    with_line = Frame("QALab.Sandbox.SeededDoor.Open", "Assets/Sandbox/Scripts/SeededDoor.cs", 11)
    assert ">11 |" in code_context_for(with_line, repo)
    release = Frame("QALab.Sandbox.SeededDoor.Open", "Assets/Sandbox/Scripts/SeededDoor.cs")
    assert "> 9 |" in code_context_for(release, repo)
    # Release frame (no "(at file:line)"): found by class name, then class/method lookup.
    assert "> 9 |" in code_context_for(Frame("QALab.Sandbox.SeededDoor.Open"), repo)
    assert code_context_for(Frame("QALab.Sandbox.Nowhere.Run"), repo) is None
    assert code_context_for(with_line, None) is None
    assert code_context_for(None, repo) is None
    missing = Frame("X.Y", "Assets/Missing.cs", 3)
    assert code_context_for(missing, repo) is None
