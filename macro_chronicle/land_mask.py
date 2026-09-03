"""陸地判定（メルカトル地図の白=陸 + 地理ポリゴン）。"""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from typing import List, Tuple

import numpy as np
from PIL import Image
from scipy import ndimage

from .map_projection import IMG_H, IMG_W, MAP_IMAGE, latlng_to_pixel, pixel_to_latlng
from .world_land_rings import LAND_RINGS

_MAP_PATH = Path(__file__).resolve().parent / MAP_IMAGE
_OCEAN_RGB = np.array([87, 193, 255], dtype=np.int16)
_LAND_COLOR_DIST = 50


def _point_in_ring(lat: float, lng: float, ring: List[Tuple[float, float]]) -> bool:
    inside = False
    n = len(ring)
    for i in range(n):
        lat_i, lng_i = ring[i]
        lat_j, lng_j = ring[(i + 1) % n]
        if (lng_i > lng) != (lng_j > lng):
            t = (lng_j - lng_i) or 1e-12
            if lat < (lat_j - lat_i) * (lng - lng_i) / t + lat_i:
                inside = not inside
    return inside


def point_in_any_land(lat: float, lng: float) -> bool:
    for ring in LAND_RINGS:
        if _point_in_ring(lat, lng, ring):
            return True
    return False


@lru_cache(maxsize=1)
def _land_raster() -> np.ndarray:
    im = Image.open(_MAP_PATH).convert("RGB")
    arr = np.array(im, dtype=np.int16)
    dist = np.sqrt(((arr - _OCEAN_RGB) ** 2).sum(axis=2))
    return dist > _LAND_COLOR_DIST


@lru_cache(maxsize=1)
def _nearest_land_indices() -> Tuple[np.ndarray, np.ndarray]:
    land = _land_raster()
    _, indices = ndimage.distance_transform_edt(~land, return_indices=True)
    return indices[0], indices[1]


def is_land_pixel(px: float, py: float) -> bool:
    land = _land_raster()
    x, y = int(round(px)), int(round(py))
    if not (0 <= x < IMG_W and 0 <= y < IMG_H):
        return False
    return bool(land[y, x])


def is_on_land(lat: float, lng: float) -> bool:
    if not point_in_any_land(lat, lng):
        return False
    px, py = latlng_to_pixel(lat, lng)
    return is_land_pixel(px, py)


def snap_to_land(lat: float, lng: float, max_radius: int = 160) -> Tuple[float, float]:
    if is_on_land(lat, lng):
        return lat, lng

    land = _land_raster()
    px, py = latlng_to_pixel(lat, lng)
    ix, iy = int(round(px)), int(round(py))

    if 0 <= ix < IMG_W and 0 <= iy < IMG_H:
        iy_n, ix_n = _nearest_land_indices()
        nx, ny = int(ix_n[iy, ix]), int(iy_n[iy, ix])
        if land[ny, nx]:
            la, lo = pixel_to_latlng(nx, ny)
            if point_in_any_land(la, lo):
                return la, lo

    best_xy = None
    best_score = float("inf")
    for r in range(1, min(max_radius, 40) + 1):
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if abs(dx) != r and abs(dy) != r:
                    continue
                x, y = ix + dx, iy + dy
                if not (0 <= x < IMG_W and 0 <= y < IMG_H and land[y, x]):
                    continue
                la, lo = pixel_to_latlng(x, y)
                if not point_in_any_land(la, lo):
                    continue
                score = dx * dx + dy * dy + ((la - lat) ** 2 + (lo - lng) ** 2) * 4.0
                if score < best_score:
                    best_score = score
                    best_xy = (x, y)
        if best_xy is not None:
            break

    if best_xy is None:
        for ring in LAND_RINGS:
            for la, lo in ring:
                if not point_in_any_land(la, lo):
                    continue
                x, y = latlng_to_pixel(la, lo)
                xi, yi = int(round(x)), int(round(y))
                if 0 <= xi < IMG_W and 0 <= yi < IMG_H and land[yi, xi]:
                    d = (la - lat) ** 2 + (lo - lng) ** 2
                    if d < best_score:
                        best_score = d
                        best_xy = (xi, yi)
        if best_xy is None:
            la, lo = LAND_RINGS[0][0]
            return la, lo
        return pixel_to_latlng(best_xy[0], best_xy[1])

    return pixel_to_latlng(best_xy[0], best_xy[1])


def distance_to_coast_px(px: float, py: float, radius: int = 40) -> float:
    return 0.0 if is_land_pixel(px, py) else 9999.0
