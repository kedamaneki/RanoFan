"""魔法IF地球 国家シミュレーション — コアエンジン."""

from .engine import SimulationEngine
from .models import HistoryEntry, NationalPower, Region, State
from .world_setup import create_initial_world, load_world_from_json

__all__ = [
    "SimulationEngine",
    "State",
    "NationalPower",
    "Region",
    "HistoryEntry",
    "create_initial_world",
    "load_world_from_json",
]

__version__ = "0.2.0"
