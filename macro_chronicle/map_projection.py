"""1925447.jpg（太平洋中心メルカトル・陸白／海水色）の投影キャリブレーション。

ImageOverlay + EPSG4326 の線形貼り付けではメルカトル歪みと一致しないため、
ピクセル座標（CRS.Simple）へ変換して使用する。
"""

from __future__ import annotations

import math
from typing import Tuple

MAP_IMAGE = "1925447.jpg"

# 画像サイズ
IMG_W = 3000
IMG_H = 1500

# 地図本体領域（周囲の水色余白を除く）
MAP_X = 11
MAP_Y = 34
MAP_W = 2850
MAP_H = 1439

# 太平洋中心メルカトル（主要都市座標で実測キャリブレーション）
CENTRAL_MERIDIAN = 137.76
LAT_MIN = -70.65
LAT_MAX = 76.25


def mercator_y(lat: float) -> float:
    lat = max(min(lat, 85.0), -85.0)
    return math.log(math.tan(math.pi / 4 + math.radians(lat) / 2))


def latlng_to_pixel(lat: float, lng: float, central_meridian: float = CENTRAL_MERIDIAN) -> Tuple[float, float]:
    """(lat, lng) -> (px, py) 画像ピクセル座標。"""
    y0 = mercator_y(LAT_MIN)
    y1 = mercator_y(LAT_MAX)
    dlon = lng - central_meridian
    while dlon > 180:
        dlon -= 360
    while dlon < -180:
        dlon += 360
    x_frac = (dlon + 180) / 360
    y_frac = (y1 - mercator_y(lat)) / (y1 - y0)
    px = MAP_X + x_frac * MAP_W
    py = MAP_Y + y_frac * MAP_H
    return px, py


def pixel_to_leaflet(py: float, px: float) -> Tuple[float, float]:
    """Leaflet CRS.Simple 用 [y, x]。"""
    return py, px


def pixel_to_latlng(px: float, py: float, central_meridian: float = CENTRAL_MERIDIAN) -> Tuple[float, float]:
    """画像ピクセル -> (lat, lng)。"""
    x_frac = (px - MAP_X) / MAP_W
    y_frac = (py - MAP_Y) / MAP_H
    y0 = mercator_y(LAT_MIN)
    y1 = mercator_y(LAT_MAX)
    y_merc = y1 - y_frac * (y1 - y0)
    lat = math.degrees(2 * math.atan(math.exp(y_merc)) - math.pi / 2)
    dlon = x_frac * 360 - 180
    lng = central_meridian + dlon
    while lng > 180:
        lng -= 360
    while lng < -180:
        lng += 360
    return lat, lng


def latlng_to_leaflet(lat: float, lng: float, central_meridian: float = CENTRAL_MERIDIAN) -> Tuple[float, float]:
    px, py = latlng_to_pixel(lat, lng, central_meridian)
    return pixel_to_leaflet(py, px)


def map_meta() -> dict:
    return {
        "projection": "Mercator_Pacific_centered",
        "reference_image": MAP_IMAGE,
        "image_size": [IMG_W, IMG_H],
        "map_area_pixels": {"x": MAP_X, "y": MAP_Y, "w": MAP_W, "h": MAP_H},
        "central_meridian": CENTRAL_MERIDIAN,
        "lat_range": [LAT_MIN, LAT_MAX],
        "leaflet_crs": "Simple",
    }
