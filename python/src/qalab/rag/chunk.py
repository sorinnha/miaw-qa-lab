"""Spec 02 §10: split markdown into heading-aware chunks of about 800 characters."""

from __future__ import annotations

import re
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

# "## Enemy registry" → level 2, title "Enemy registry"
_HEADING = re.compile(r"^(?P<hashes>#{1,3})\s+(?P<title>.+?)\s*#*\s*$")
# "Enemy registry / IDs" → "enemy-registry-ids"
_NON_SLUG = re.compile(r"[^a-z0-9]+")
# "...at y = −10. The player respawns." → split after ". " (also "? " and "! " and newlines)
_SENTENCE_END = re.compile(r"(?<=[.!?])\s+|\n+")

DEFAULT_CHUNK_CHARS = 800
DEFAULT_OVERLAP_CHARS = 100


@dataclass(frozen=True)
class Chunk:
    chunk_id: str  # "sandbox_design.md#doors-0"
    source: str  # file name
    heading: str
    text: str
    n: int  # piece number within the section

    @property
    def embed_text(self) -> str:
        """What gets embedded / indexed: heading first, so short sections still carry their name."""
        return f"{self.heading}\n{self.text}"

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def slug(heading: str) -> str:
    return _NON_SLUG.sub("-", heading.lower()).strip("-") or "section"


def split_sections(markdown: str) -> list[tuple[str, str]]:
    """``[(heading, body)]`` split on H1–H3. Text before the first heading gets heading ""."""
    sections: list[tuple[str, list[str]]] = [("", [])]
    for line in markdown.splitlines():
        match = _HEADING.match(line)
        if match:
            sections.append((match.group("title"), []))
        else:
            sections[-1][1].append(line)
    return [(h, "\n".join(body).strip()) for h, body in sections if "\n".join(body).strip()]


def split_long(text: str, chunk_chars: int, overlap_chars: int) -> list[str]:
    """Pieces of ≤ ``chunk_chars`` cut at sentence ends, each starting with ~``overlap`` of tail."""
    if len(text) <= chunk_chars:
        return [text]
    sentences = [s for s in _SENTENCE_END.split(text) if s and s.strip()]
    pieces: list[str] = []
    current = ""
    for sentence in sentences:
        candidate = f"{current} {sentence}".strip() if current else sentence
        if len(candidate) > chunk_chars and current:
            pieces.append(current)
            current = f"{_tail(current, overlap_chars)} {sentence}".strip()
        else:
            current = candidate
    if current:
        pieces.append(current)
    return pieces


def _tail(text: str, overlap_chars: int) -> str:
    """The last ``overlap_chars`` of ``text``, extended left to a word boundary."""
    if overlap_chars <= 0 or len(text) <= overlap_chars:
        return text if overlap_chars > 0 else ""
    cut = text.rfind(" ", 0, len(text) - overlap_chars)
    return text[cut + 1 :] if cut >= 0 else text[-overlap_chars:]


def chunk_markdown(
    markdown: str,
    source: str,
    chunk_chars: int = DEFAULT_CHUNK_CHARS,
    overlap_chars: int = DEFAULT_OVERLAP_CHARS,
) -> list[Chunk]:
    chunks: list[Chunk] = []
    for heading, body in split_sections(markdown):
        for n, piece in enumerate(split_long(body, chunk_chars, overlap_chars)):
            chunks.append(
                Chunk(
                    chunk_id=f"{source}#{slug(heading)}-{n}",
                    source=source,
                    heading=heading,
                    text=piece,
                    n=n,
                )
            )
    return chunks


def chunk_file(
    path: Path, chunk_chars: int = DEFAULT_CHUNK_CHARS, overlap_chars: int = DEFAULT_OVERLAP_CHARS
) -> list[Chunk]:
    return chunk_markdown(path.read_text(encoding="utf-8"), path.name, chunk_chars, overlap_chars)
