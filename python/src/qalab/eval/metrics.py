"""Metrics for ``qalab eval`` (spec 02, M5): clustering quality, retrieval, latency.

Plain functions over plain values, so each one can be checked by hand on a toy example.
"""

from __future__ import annotations

from collections import Counter
from collections.abc import Hashable, Sequence

import numpy as np


def pairwise_prf(
    true_labels: Sequence[Hashable], pred_labels: Sequence[Hashable]
) -> tuple[float, float, float]:
    """Pairwise precision, recall and F1 of a clustering against the true grouping.

    (Learning task, written by Claude at Sora's request, D-030.) Item ``i`` belongs to true group
    ``true_labels[i]`` and to predicted cluster ``pred_labels[i]``. Every unordered pair of items
    ``{i, j}`` is one of:

    - **TP**: same predicted cluster and same true group (a correct merge);
    - **FP**: same predicted cluster, different true groups (a wrong merge);
    - **FN**: different predicted clusters, same true group (a missed merge, a split bug).

    ``precision = TP / (TP + FP)`` and ``recall = TP / (TP + FN)``. When a denominator is 0 the
    value is 1.0: no predicted pairs means no wrong merges, no true pairs means nothing to miss.
    ``f1 = 2PR / (P + R)``, or 0.0 when ``P + R`` is 0. Returns plain ``float``s.

    Raises ``ValueError`` if the two sequences have different lengths. Empty input gives
    (1.0, 1.0, 1.0).

    Pairs are counted, never looped over: a benchmark has thousands of events, so millions of
    pairs. A group of ``n`` items holds ``n * (n - 1) // 2`` pairs, so ``TP + FP`` = pairs inside
    predicted clusters, ``TP + FN`` = pairs inside true groups, and ``TP`` = pairs inside each
    (true group, predicted cluster) combination. ``collections.Counter`` over ``pred_labels``,
    ``true_labels`` and ``zip(true_labels, pred_labels)`` gives all three. A test checks that 20 000
    items finish in well under a second.

    C# comparison: ``GroupBy(x => x).Sum(g => g.Count() * (g.Count() - 1) / 2)`` three times.
    """
    if len(true_labels) != len(pred_labels):
        raise ValueError(
            f"true_labels has {len(true_labels)} items but pred_labels has {len(pred_labels)}"
        )

    def pairs(groups: Counter[Hashable]) -> int:
        """Unordered pairs inside groups: a group of n items holds n·(n−1)/2 of them."""
        return sum(n * (n - 1) // 2 for n in groups.values())

    # strict=True can't fail after the length check above; ruff's B905 rule asks for it anyway, so a
    # later edit that drops the check still can't silently pair up the wrong items.
    true_positives = pairs(Counter(zip(true_labels, pred_labels, strict=True)))  # same in both
    predicted_pairs = pairs(Counter(pred_labels))  # TP + FP
    true_pairs = pairs(Counter(true_labels))  # TP + FN
    precision = true_positives / predicted_pairs if predicted_pairs else 1.0
    recall = true_positives / true_pairs if true_pairs else 1.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return precision, recall, f1  # already floats: "/" always gives a float in Python 3


def cluster_count_error(predicted: int, true: int) -> int:
    """Predicted clusters minus true bugs: > 0 means bugs were split, < 0 means bugs were merged."""
    return predicted - true


def percentile(values: Sequence[float], q: float) -> float | None:
    """The ``q``-th percentile (0–100, linear interpolation), or None for no values."""
    if not values:
        return None
    return float(np.percentile(np.asarray(values, dtype=float), q))


def rate(hits: int, total: int) -> float | None:
    """``hits / total``, or None when nothing was measured (never a fake 0 or 1)."""
    return hits / total if total else None
