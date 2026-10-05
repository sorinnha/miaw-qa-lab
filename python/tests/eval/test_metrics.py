"""Spec 02 (M5) metrics, including the ``pairwise_prf`` learning task (D-030)."""

import time

import pytest

from qalab.eval.metrics import cluster_count_error, pairwise_prf, percentile, rate


def test_hand_checked_toy_example() -> None:
    # True groups: A A A B B. Predicted: x x y y y.
    # Pairs in predicted clusters: x:{0,1}, y:{2,3},{2,4},{3,4} → 4 (TP+FP)
    # Pairs in true groups: A:{0,1},{0,2},{1,2}, B:{3,4} → 4 (TP+FN)
    # Pairs in both: {0,1} (A, x) and {3,4} (B, y) → TP = 2
    precision, recall, f1 = pairwise_prf(["A", "A", "A", "B", "B"], ["x", "x", "y", "y", "y"])
    assert precision == pytest.approx(0.5)
    assert recall == pytest.approx(0.5)
    assert f1 == pytest.approx(0.5)
    assert all(type(v) is float for v in (precision, recall, f1))


def test_perfect_clustering_ignores_label_names() -> None:
    assert pairwise_prf(["SB01", "SB01", "SB04"], [7, 7, 3]) == (1.0, 1.0, 1.0)


def test_everything_in_one_cluster_has_full_recall_and_low_precision() -> None:
    # 4 items, 2 true groups of 2: true pairs 2, predicted pairs 6, TP 2.
    precision, recall, f1 = pairwise_prf(["a", "a", "b", "b"], ["c"] * 4)
    assert (precision, recall) == (pytest.approx(2 / 6), 1.0)
    assert f1 == pytest.approx(2 * (1 / 3) / (1 / 3 + 1))


def test_all_singletons_have_no_predicted_pairs() -> None:
    # No predicted pairs → precision 1.0 by definition; every true pair was missed → recall 0.
    precision, recall, f1 = pairwise_prf(["a", "a", "b"], [1, 2, 3])
    assert (precision, recall, f1) == (1.0, 0.0, 0.0)


def test_edge_cases() -> None:
    assert pairwise_prf([], []) == (1.0, 1.0, 1.0)
    assert pairwise_prf(["a"], ["x"]) == (1.0, 1.0, 1.0)
    assert pairwise_prf(["a", "b"], ["x", "x"]) == (0.0, 1.0, 0.0)  # one wrong merge
    with pytest.raises(ValueError):
        pairwise_prf(["a", "b"], ["x"])


def test_symmetry_swapping_truth_and_prediction_swaps_p_and_r() -> None:
    true = ["a", "a", "a", "b"]
    pred = [1, 1, 2, 2]  # predicted pairs 2, true pairs 3, TP 1
    assert pairwise_prf(true, pred)[:2] == pytest.approx((1 / 2, 1 / 3))
    assert pairwise_prf(pred, true)[:2] == pytest.approx((1 / 3, 1 / 2))


def test_twenty_thousand_items_are_counted_not_enumerated() -> None:
    true = [i % 7 for i in range(20_000)]
    pred = [i % 5 for i in range(20_000)]  # 200 million pairs if enumerated
    started = time.perf_counter()
    precision, recall, _ = pairwise_prf(true, pred)
    assert time.perf_counter() - started < 1.0
    assert 0.0 < precision < 1.0 and 0.0 < recall < 1.0


def test_no_correct_pair_gives_zero_f1_not_a_division_error() -> None:
    # Predicted pair {0, 2} is a wrong merge and true pair {0, 1} is missed: P = R = 0, so F1 = 0.
    assert pairwise_prf(["a", "a", "b"], ["x", "y", "x"]) == (0.0, 0.0, 0.0)


def test_cluster_count_error_sign() -> None:
    assert cluster_count_error(9, 8) == 1  # one bug split
    assert cluster_count_error(7, 8) == -1  # two bugs merged


def test_percentile_and_rate() -> None:
    assert percentile([], 50) is None
    assert percentile([10.0, 20.0, 30.0, 40.0], 50) == pytest.approx(25.0)
    assert percentile([1.0] * 19 + [100.0], 95) == pytest.approx(5.95)
    assert rate(0, 0) is None
    assert rate(3, 4) == 0.75
