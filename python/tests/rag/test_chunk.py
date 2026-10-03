from pathlib import Path

from qalab.rag.chunk import chunk_file, chunk_markdown, slug, split_long, split_sections

REPO = Path(__file__).resolve().parents[3]
DESIGN_DOC = REPO / "docs" / "sandbox_design.md"


def test_slug() -> None:
    assert slug("Enemy registry") == "enemy-registry"
    assert slug("  HUD / Settings menu!  ") == "hud-settings-menu"
    assert slug("###") == "section"


def test_sections_split_on_h1_to_h3_only() -> None:
    md = "intro\n# Title\nbody1\n## Sub\nbody2\n#### Deep\nstill body2\n### Third\nbody3\n"
    assert split_sections(md) == [
        ("", "intro"),
        ("Title", "body1"),
        ("Sub", "body2\n#### Deep\nstill body2"),
        ("Third", "body3"),
    ]


def test_long_sections_split_at_sentence_boundaries_with_overlap() -> None:
    sentences = [f"Sentence number {i} ends here." for i in range(40)]
    text = " ".join(sentences)
    pieces = split_long(text, chunk_chars=200, overlap_chars=50)
    assert len(pieces) > 1
    assert all(len(p) <= 200 + 60 for p in pieces)  # overlap tail + one sentence slack
    assert all(p.endswith(".") for p in pieces)
    # The second piece starts with the tail of the first (overlap), cut at a word boundary.
    assert pieces[1].startswith("number") or pieces[1].startswith("Sentence")
    assert pieces[1][:40] in pieces[0]
    assert split_long("short", 800, 100) == ["short"]


def test_chunk_ids_and_headings_on_the_design_doc() -> None:
    chunks = chunk_file(DESIGN_DOC)
    ids = [c.chunk_id for c in chunks]
    assert "sandbox_design.md#doors-0" in ids and "sandbox_design.md#enemy-registry-0" in ids
    assert len(ids) == len(set(ids))
    doors = next(c for c in chunks if c.chunk_id == "sandbox_design.md#doors-0")
    assert doors.heading == "Doors" and "SeededDoor" in doors.text
    assert doors.embed_text.startswith("Doors\n")
    assert all(len(c.text) <= 800 + 120 for c in chunks)
    long = chunk_markdown("## A\n" + "x. " * 1000, "a.md", chunk_chars=300, overlap_chars=30)
    assert [c.n for c in long] == list(range(len(long))) and len(long) > 5
