import pytest

from qalab.models.event import Event
from qalab.triage.signature import (
    cell_of,
    detector_signature,
    exception_type_of,
    is_candidate,
    log_signature,
)
from tests.triage.fixtures import load_sample


@pytest.fixture(scope="module")
def events() -> dict[int, Event]:
    return {e.seq: e for e in load_sample().events}


def test_exception_type() -> None:
    assert exception_type_of("KeyNotFoundException: The given key") == "KeyNotFoundException"
    assert exception_type_of("Inventory slot 7 out of range") == ""
    assert exception_type_of("ArgumentError: x") == "ArgumentError"
    assert exception_type_of(None) == ""


def test_candidates(events: dict[int, Event]) -> None:
    assert is_candidate(events[8]) and is_candidate(events[13]) and is_candidate(events[23])
    assert not is_candidate(events[34])  # info log
    assert not is_candidate(events[4]) and not is_candidate(events[3])
    assert not is_candidate(events[13], min_level="error")


def test_detector_signature_cells(events: dict[int, Event]) -> None:
    signature, detector, cell = detector_signature(events[23])
    assert detector == "fell_out_of_world" and cell == (8, 0)  # pos (35.2, -12, 1.1) / 4
    assert len(signature) == 12 and int(signature, 16) >= 0
    assert detector_signature(events[23])[0] == signature  # stable
    assert detector_signature(events[21])[0] != signature
    assert cell_of(None, 4.0) == (0, 0) and cell_of((-0.1, 0, 7.9), 4.0) == (-1, 1)


@pytest.mark.youwrite
def test_log_signatures(events: dict[int, Event]) -> None:
    sb01, *_ = log_signature(events[8])
    sb04, *_ = log_signature(events[18])
    assert sb01 != sb04  # same message, different frames
    assert log_signature(events[11])[0] == log_signature(events[15])[0]  # SB02: 7 vs 9
    sig19, exc, normalized, frames = log_signature(events[19])
    assert sig19 == log_signature(events[26])[0]  # SB03: different quoted ids
    assert exc == "KeyNotFoundException" and "<str>" in normalized
    assert frames[0] == "QALab.Sandbox.SeededEnemyRegistry.Get"  # System.* frame skipped
    assert all(not f.startswith("MiawWorks.QALab") for f in log_signature(events[8])[3])
    assert log_signature(events[30])[0] != log_signature(events[31])[0]  # SB14 under exact
