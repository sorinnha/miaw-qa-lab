"""Loading and resizing screenshots. Pixels are numpy arrays (height, width, 3) of uint8 RGB."""

from __future__ import annotations

import io
from pathlib import Path

import numpy as np
from PIL import Image


def load_rgb(path: Path) -> np.ndarray:
    """A screenshot as an RGB array (alpha dropped)."""
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.uint8)


def downscale(rgb: np.ndarray, long_side: int) -> np.ndarray:
    """Shrink (never enlarge) so the long side is at most ``long_side`` px, keeping the aspect."""
    height, width = rgb.shape[:2]
    longest = max(height, width)
    if longest <= long_side:
        return rgb
    scale = long_side / longest
    size = (max(1, round(width * scale)), max(1, round(height * scale)))
    return np.asarray(Image.fromarray(rgb).resize(size, Image.Resampling.BILINEAR), dtype=np.uint8)


def to_png(rgb: np.ndarray) -> bytes:
    """PNG bytes, e.g. to send to a vision model."""
    buffer = io.BytesIO()
    Image.fromarray(rgb).save(buffer, format="PNG")
    return buffer.getvalue()


def luminance(rgb: np.ndarray) -> np.ndarray:
    """Per-pixel luma 0.299R + 0.587G + 0.114B (0–255), as float32."""
    channels = rgb.astype(np.float32)
    return 0.299 * channels[..., 0] + 0.587 * channels[..., 1] + 0.114 * channels[..., 2]
