from pathlib import Path

import pytest

from qalab.config import Config, load_config


def test_defaults_match_spec() -> None:
    config = Config()
    assert (
        config.triage.context_chars == 10_000 and config.triage.merge.frame_embed_threshold == 0.8
    )
    assert (config.rank.p1, config.rank.p2, config.rank.p3) == (15.0, 8.0, 3.0)
    assert config.llm.max_retries == 2 and config.rag.top_k == 3 and config.rag.min_score == 0.25


def test_repo_qalab_toml_loads_cleanly(caplog: pytest.LogCaptureFixture) -> None:
    repo_toml = Path(__file__).resolve().parents[2] / "qalab.toml"
    config = load_config(repo_toml)
    assert config == Config()  # the committed file holds exactly the spec defaults
    assert "unknown key" not in caplog.text


def test_nested_tables_and_unknown_keys(tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    toml = tmp_path / "qalab.toml"
    toml.write_text(
        "[triage]\nmax_reports = 3\n[triage.merge]\nframe_tfidf_threshold = 0.7\n"
        "[rank]\np1 = 20\n[llm]\nmax_retries = 0\nmystery = 1\n",
        encoding="utf-8",
    )
    with caplog.at_level("WARNING"):
        config = load_config(toml)
    assert config.triage.max_reports == 3 and config.triage.merge.frame_tfidf_threshold == 0.7
    assert config.rank.p1 == 20 and config.llm.max_retries == 0
    assert config.rag == Config().rag
    assert "unknown key 'mystery'" in caplog.text


def test_missing_explicit_file_raises(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(FileNotFoundError):
        load_config(tmp_path / "nope.toml")
    monkeypatch.chdir(tmp_path)  # no qalab.toml here → defaults, no error
    assert load_config() == Config()
