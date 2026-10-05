"""The optional classic-ML baseline (spec 03): features, training discipline, save and load."""

from pathlib import Path

import joblib
import numpy as np
import pytest

from qalab.vision.ml import FEATURE_VERSION, MlModel, calibrated, features, train


def _frames(n: int, seed: int) -> tuple[list[np.ndarray], list[list[str]]]:
    """Grass-coloured noise frames; every 3rd is black (``black_screen``)."""
    rng = np.random.default_rng(seed)
    images, labels = [], []
    for i in range(n):
        if i % 3 == 0:
            img = rng.integers(0, 8, size=(36, 64, 3), dtype=np.uint8)
            labels.append(["black_screen"])
        else:
            base = np.array([70, 130, 60], dtype=np.int16)
            img = np.clip(base + rng.integers(-40, 40, size=(36, 64, 3)), 0, 255).astype(np.uint8)
            labels.append([])
        images.append(img)
    return images, labels


def test_features_have_a_fixed_length_and_sane_ranges() -> None:
    black = np.zeros((36, 64, 3), dtype=np.uint8)
    white = np.full((720, 1280, 3), 255, dtype=np.uint8)
    for img in (black, white):
        x = features(img)
        assert x.shape == (53,) and x.dtype == np.float32
        assert np.all(x >= 0.0) and np.all(x <= 1.0)
        assert x[:16].sum() == pytest.approx(1.0), "each 16-bin histogram sums to 1"
    assert features(black)[-1] == 1.0, "share of near-black pixels"
    assert features(white)[-2] == 1.0, "share of near-white pixels"


def test_training_learns_an_easy_label_and_skips_labels_without_positives() -> None:
    x_imgs, y = _frames(30, seed=1)
    v_imgs, v_y = _frames(9, seed=2)
    x = np.array([features(i) for i in x_imgs])
    model = train(x, y, np.array([features(i) for i in v_imgs]), v_y)
    assert model.models["missing_texture"] is None, "no positive train frame: no classifier"
    assert model.thresholds["missing_texture"] == 0.5
    assert model.models["black_screen"] is not None

    t_imgs, t_y = _frames(9, seed=3)
    predicted = [[f.label for f in model.predict(img)] for img in t_imgs]
    assert predicted == t_y, "black vs grass is separable from the histograms alone"


def test_scores_put_the_tuned_threshold_at_one_half() -> None:
    # Triage keeps scores ≥ 0.5, eval keeps p ≥ threshold: the rescale makes them agree.
    assert calibrated(0.2, 0.2) == 0.5
    assert calibrated(0.6, 0.2) == 0.75
    assert calibrated(0.1, 0.2) == 0.25
    assert calibrated(1.0, 0.9) == 1.0 and calibrated(0.0, 0.9) == 0.0


def test_predicted_scores_reach_triage(tmp_path: Path) -> None:
    x_imgs, y = _frames(30, seed=1)
    x = np.array([features(i) for i in x_imgs])
    model = train(x, y, x, y)
    model.thresholds["black_screen"] = 0.15  # a low val-tuned threshold
    found = model.predict(np.zeros((36, 64, 3), dtype=np.uint8))
    assert [f.label for f in found] == ["black_screen"] and found[0].score >= 0.5


def test_save_and_load_round_trip(tmp_path: Path) -> None:
    x_imgs, y = _frames(12, seed=4)
    x = np.array([features(i) for i in x_imgs])
    model = train(x, y, x, y)
    path = tmp_path / "models" / "ml.joblib"
    model.save(path)
    loaded = MlModel.load(path)
    assert loaded.thresholds == model.thresholds
    np.testing.assert_allclose(
        loaded.probabilities(x)["black_screen"], model.probabilities(x)["black_screen"]
    )


def test_load_refuses_other_files_and_old_feature_versions(tmp_path: Path) -> None:
    joblib.dump({"not": "a model"}, tmp_path / "other.joblib")
    with pytest.raises(ValueError):
        MlModel.load(tmp_path / "other.joblib")
    old = MlModel(feature_version=FEATURE_VERSION - 1)
    joblib.dump(old, tmp_path / "old.joblib")
    with pytest.raises(ValueError, match="feature version"):
        MlModel.load(tmp_path / "old.joblib")
