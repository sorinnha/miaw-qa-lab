"""Spec 03 optional classic-ML baseline: hand-made image features + a logistic regression per label.

The point is the discipline (train on train, pick thresholds on val, report on test, check for
overfitting by comparing train and test scores), and to see when a simple model is enough.
"""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass, field
from pathlib import Path

import joblib
import numpy as np
from PIL import Image
from scipy import ndimage
from sklearn.linear_model import LogisticRegression
from sklearn.pipeline import Pipeline, make_pipeline
from sklearn.preprocessing import StandardScaler

from qalab.models.visual import FoundLabel
from qalab.vision.images import downscale, luminance

LABELS = ("missing_texture", "black_screen", "ui_overflow", "placeholder_ui")
FEATURE_LONG_SIDE = 256
FEATURE_VERSION = 1
THRESHOLD_GRID = tuple(round(x, 2) for x in np.arange(0.1, 0.95, 0.05))


def features(rgb: np.ndarray) -> np.ndarray:
    """53 numbers: a 16-bin histogram per HSV channel (48), edge density, mean and std luminance,
    and the shares of near-white and near-black pixels."""
    small = downscale(rgb, FEATURE_LONG_SIDE)
    hsv = np.asarray(Image.fromarray(small).convert("HSV"), dtype=np.uint8)
    pixels = max(1, small.shape[0] * small.shape[1])
    hists = [np.bincount((hsv[..., c] // 16).ravel(), minlength=16) / pixels for c in range(3)]
    luma = luminance(small)
    edges = np.hypot(ndimage.sobel(luma, axis=0), ndimage.sobel(luma, axis=1))
    extra = [
        float(np.mean(edges > 64.0)),
        float(np.mean(luma) / 255.0),
        float(np.std(luma) / 255.0),
        float(np.mean(np.all(small >= 245, axis=-1))),
        float(np.mean(luma < 16.0)),
    ]
    return np.concatenate([*hists, np.asarray(extra)]).astype(np.float32)


@dataclass
class MlModel:
    """One classifier per label (None when train had no positives) and its val-tuned threshold."""

    models: dict[str, Pipeline | None] = field(default_factory=dict)
    thresholds: dict[str, float] = field(default_factory=dict)
    feature_version: int = FEATURE_VERSION

    def probabilities(self, x: np.ndarray) -> dict[str, np.ndarray]:
        out: dict[str, np.ndarray] = {}
        for label in LABELS:
            model = self.models.get(label)
            out[label] = model.predict_proba(x)[:, 1] if model is not None else np.zeros(len(x))
        return out

    def predict(self, rgb: np.ndarray) -> list[FoundLabel]:
        probs = self.probabilities(features(rgb).reshape(1, -1))
        return [
            FoundLabel(label=label, score=round(float(p[0]), 4))  # type: ignore[arg-type]
            for label, p in probs.items()
            if p[0] >= self.thresholds.get(label, 0.5)
        ]

    def save(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        joblib.dump(self, path)

    @staticmethod
    def load(path: Path) -> MlModel:
        model = joblib.load(path)
        if not isinstance(model, MlModel) or model.feature_version != FEATURE_VERSION:
            raise ValueError(f"{path} is not a qalab ML model of feature version {FEATURE_VERSION}")
        return model


def _f1(truth: np.ndarray, predicted: np.ndarray) -> float:
    tp = float(np.sum(truth & predicted))
    fp = float(np.sum(~truth & predicted))
    fn = float(np.sum(truth & ~predicted))
    return 2 * tp / (2 * tp + fp + fn) if tp else 0.0


def train(
    x_train: np.ndarray,
    y_train: Sequence[Sequence[str]],
    x_val: np.ndarray,
    y_val: Sequence[Sequence[str]],
    seed: int = 7,
) -> MlModel:
    """Fit on train; for each label pick the probability threshold with the best F1 on val (0.5
    when val has no positives)."""
    model = MlModel()
    for label in LABELS:
        truth_train = np.array([label in labels for labels in y_train])
        if truth_train.sum() == 0 or truth_train.all():
            model.models[label] = None
            model.thresholds[label] = 0.5
            continue
        pipeline = make_pipeline(
            StandardScaler(),
            LogisticRegression(class_weight="balanced", max_iter=2000, random_state=seed),
        )
        pipeline.fit(x_train, truth_train)
        model.models[label] = pipeline
        truth_val = np.array([label in labels for labels in y_val], dtype=bool)
        if truth_val.sum() == 0:
            model.thresholds[label] = 0.5
            continue
        probs = pipeline.predict_proba(x_val)[:, 1]
        model.thresholds[label] = max(
            THRESHOLD_GRID, key=lambda t: (_f1(truth_val, probs >= t), -abs(t - 0.5))
        )
    return model
