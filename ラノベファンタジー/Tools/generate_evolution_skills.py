#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""131カ国歴史書ログから発展スキル（Evolution Skills）JSONを量産する。"""

from __future__ import annotations

import json
import os
import re
import glob
from typing import Any

tech_re = re.compile(r"「([^」]+)」")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HISTORY_DIR = os.path.join(os.path.dirname(ROOT), "歴史棚")
OUT_DIR = os.path.join(ROOT, "Assets", "Resources", "SkillMasters", "Evolution")
INDEX_PATH = os.path.join(ROOT, "Assets", "Resources", "SkillMasters", "Evolution", "CountryTechIndex.json")
CATALOG_PATH = os.path.join(ROOT, "Assets", "Resources", "SkillMasters", "SkillMasters_Catalog.json")

COMBAT_WEAPON_IDS = [
    ("SKILL_COMBAT_SWORD", "剣術"),
    ("SKILL_COMBAT_GREATSWORD", "大剣術"),
    ("SKILL_COMBAT_DAGGER", "短剣術"),
    ("SKILL_COMBAT_SPEAR", "槍術"),
    ("SKILL_COMBAT_AXE", "斧術"),
    ("SKILL_COMBAT_HAMMER", "槌術"),
    ("SKILL_COMBAT_FIST", "拳闘術"),
    ("SKILL_COMBAT_KATANA", "刀術"),
    ("SKILL_COMBAT_SCYTHE", "大鎌術"),
    ("SKILL_COMBAT_WHIP", "鞭術"),
    ("SKILL_COMBAT_STAFF", "棍術"),
    ("SKILL_COMBAT_SHIELD", "盾術"),
    ("SKILL_COMBAT_DUAL", "双剣術"),
    ("SKILL_COMBAT_RAPIER", "細剣術"),
    ("SKILL_COMBAT_POLEARM", "長柄術"),
    ("SKILL_COMBAT_BOW", "弓術"),
    ("SKILL_COMBAT_CROSSBOW", "弩術"),
    ("SKILL_COMBAT_THROW", "投擲術"),
    ("SKILL_COMBAT_ASSASSIN", "暗殺術"),
    ("SKILL_COMBAT_GUN", "銃術"),
    ("SKILL_COMBAT_CANNON", "砲術"),
    ("SKILL_COMBAT_SHIELDBOW", "盾弾術"),
    ("SKILL_COMBAT_MARTIAL", "格闘術"),
    ("SKILL_COMBAT_DUALDAGGER", "双短剣術"),
    ("SKILL_COMBAT_HIDDEN", "暗器術"),
]

LOG_KEYWORDS = [
    ("激流回帰", "CombatLog_WaterReturn"),
    ("激流拒否", "CombatLog_WaterReject"),
    ("激流", "CombatLog_WaterFlow"),
    ("水流魔力", "CombatLog_WaterFlow"),
    ("水流", "CombatLog_WaterFlow"),
    ("水利", "CombatLog_WaterFlow"),
    ("河川防衛", "CombatLog_WaterReject"),
    ("石相硬化", "CombatLog_StoneHarden"),
    ("石相", "CombatLog_StonePhase"),
    ("地殻共振", "CombatLog_CrustResonance"),
    ("地殻結晶", "CombatLog_CrustCrystal"),
    ("地殻", "CombatLog_CrustResonance"),
    ("岩相反発", "CombatLog_RockRepulse"),
    ("岩相", "CombatLog_StonePhase"),
    ("流砂拒否", "CombatLog_SandReject"),
    ("砂下都市", "CombatLog_SandFlow"),
    ("流砂", "CombatLog_SandFlow"),
    ("幻影遮蔽", "CombatLog_PhantomVeil"),
    ("森林幻惑", "CombatLog_PhantomVeil"),
    ("幻影", "CombatLog_PhantomVeil"),
    ("波状変調", "CombatLog_WaveModulation"),
    ("偏向シールド", "CombatLog_WaveModulation"),
    ("網状分散", "CombatLog_GridDefense"),
    ("変電結晶網", "CombatLog_GridDefense"),
    ("広域統合", "CombatLog_GridDefense"),
    ("大気震動", "CombatLog_AirVibration"),
    ("烈風拒否", "CombatLog_WindForce"),
    ("大気", "CombatLog_WindForce"),
    ("風力", "CombatLog_WindForce"),
    ("生体強靭", "CombatLog_Biomimic"),
    ("土壌保護", "CombatLog_Biomimic"),
    ("生体", "CombatLog_Biomimic"),
    ("泥濘同調", "CombatLog_MireSync"),
    ("山殻強固", "CombatLog_MountainShell"),
    ("氷殻共鳴", "CombatLog_IceShell"),
    ("寒冷結晶", "CombatLog_IceWall"),
    ("氷結結界", "CombatLog_IceWall"),
    ("静的氷壁", "CombatLog_IceWall"),
    ("焦土エネルギー", "CombatLog_ScorchedEarth"),
    ("高密度エネルギー障壁", "CombatLog_BarrierDensity"),
    ("結晶流通防衛", "CombatLog_CrystalFlow"),
    ("海上浮遊結晶", "CombatLog_SeaFloat"),
    ("深層魔導都市", "CombatLog_DeepCity"),
    ("分散型拠点網", "CombatLog_Evasion"),
    ("塩相", "CombatLog_SaltPhase"),
    ("隠蔽", "CombatLog_Evasion"),
    ("回避", "CombatLog_Evasion"),
]

EFFECT_BY_LOG = {
    "CombatLog_WaterReturn": ("Debuff_PoiseRegen", 0.18, 5.0),
    "CombatLog_WaterFlow": ("Debuff_PoiseRecoveryHalt", 0.12, 4.0),
    "CombatLog_WaterReject": ("Debuff_PoiseRegen", 0.15, 4.5),
    "CombatLog_StonePhase": ("Buff_Poise", 0.2, 6.0),
    "CombatLog_StoneHarden": ("Debuff_ArmorDissolve", 0.15, 5.0),
    "CombatLog_CrustResonance": ("Debuff_PoiseRecoveryHalt", 0.2, 5.0),
    "CombatLog_CrustCrystal": ("Debuff_ArmorDissolve", 0.18, 5.0),
    "CombatLog_RockRepulse": ("Buff_STR", 0.15, 5.0),
    "CombatLog_SandFlow": ("Debuff_PoiseRegen", 0.14, 4.0),
    "CombatLog_SandReject": ("Debuff_PoiseRecoveryHalt", 0.16, 4.0),
    "CombatLog_PhantomVeil": ("Debuff_PoiseRegen", 0.2, 6.0),
    "CombatLog_WaveModulation": ("Psychic_ManaControl", 0.12, 6.0),
    "CombatLog_GridDefense": ("Buff_Poise", 0.18, 5.0),
    "CombatLog_AirVibration": ("Debuff_PoiseRegen", 0.17, 5.0),
    "CombatLog_WindForce": ("Debuff_PoiseRecoveryHalt", 0.15, 4.5),
    "CombatLog_Biomimic": ("Psychic_Regen_HP", 8.0, 6.0),
    "CombatLog_MireSync": ("Debuff_PoiseRegen", 0.22, 5.0),
    "CombatLog_MountainShell": ("Buff_Poise", 0.25, 7.0),
    "CombatLog_IceShell": ("Debuff_PoiseRecoveryHalt", 0.2, 5.0),
    "CombatLog_IceWall": ("Buff_Poise", 0.12, 4.0),
    "CombatLog_SaltPhase": ("Debuff_ArmorDissolve", 0.1, 4.0),
    "CombatLog_Evasion": ("Psychic_Boost_DEF", 0.1, 5.0),
    "CombatLog_ScorchedEarth": ("Debuff_ArmorDissolve", 0.2, 5.0),
    "CombatLog_BarrierDensity": ("Buff_Poise", 0.22, 6.0),
    "CombatLog_CrystalFlow": ("Psychic_ManaControl", 0.1, 5.0),
    "CombatLog_SeaFloat": ("Debuff_PoiseRegen", 0.14, 4.5),
    "CombatLog_DeepCity": ("Buff_Poise", 0.2, 7.0),
}

PROD_CRAFT_EFFECTS = [
    ("Craft_PurityBonus", 0.12, 0.0),
    ("Craft_ExtractionBonus", 0.15, 0.0),
    ("Craft_PurityFlatBonus", 5.0, 0.0),
    ("Craft_DissolutionRate", 0.1, 0.0),
]

UTIL_EFFECT_MAP = {
    "SKILL_UTIL_RADAR": ("Utility_EnemyDetect", 1.0, 8.0),
    "SKILL_UTIL_MAPRADAR": ("Utility_MapRadar", 1.0, 10.0),
    "SKILL_UTIL_MINDACCEL": ("Utility_MindAccelerate", 0.45, 8.0),
    "SKILL_UTIL_WAVESEE": ("Utility_EnemyDetect", 1.0, 7.0),
    "SKILL_UTIL_STEALTH": ("Utility_MindAccelerate", 0.25, 10.0),
    "SKILL_UTIL_PRESENCE": ("Utility_EnemyDetect", 1.0, 9.0),
    "SKILL_UTIL_DANGER": ("Utility_MindAccelerate", 0.35, 9.0),
    "SKILL_UTIL_DARKVIS": ("Utility_MapRadar", 1.0, 8.0),
    "SKILL_UTIL_FARSIGHT": ("Utility_MapRadar", 1.0, 12.0),
    "SKILL_UTIL_TRACK": ("Utility_EnemyDetect", 1.0, 10.0),
    "SKILL_UTIL_TRAPDET": ("Utility_EnemyDetect", 1.0, 8.0),
    "SKILL_UTIL_STRUCT": ("Utility_MapRadar", 1.0, 9.0),
    "SKILL_UTIL_MOTION": ("Utility_MindAccelerate", 0.4, 8.0),
    "SKILL_UTIL_HEARING": ("Utility_EnemyDetect", 1.0, 9.0),
    "SKILL_UTIL_SCAN": ("Utility_MapRadar", 1.0, 11.0),
}


def strip_suffix(name: str) -> str:
    for suf in ("障壁", "結界", "シールド", "樹海", "網", "結界"):
        if name.endswith(suf) and len(name) > len(suf):
            return name[: -len(suf)]
    return name


def infer_log_type(text: str, concept: str) -> str:
    blob = text + concept
    for kw, log_type in LOG_KEYWORDS:
        if kw in blob:
            return log_type
    return "GeneralPrerequisite"


def extract_tech_from_part(part: str) -> str:
    techs = tech_re.findall(part)
    for tech in techs:
        if re.match(r"国家\d{3}", tech):
            continue
        if tech in ("絶対防衛圏", "最底の世紀", "共同再興圏", "北回廊"):
            continue
        return tech
    # 引用符なしの固有技術パターン
    for kw, _ in LOG_KEYWORDS:
        if kw in part:
            return kw + "障壁"
    return "魔力共鳴障壁"


def parse_countries() -> dict[int, dict[str, Any]]:
    countries: dict[int, dict[str, Any]] = {}
    header_re = re.compile(r"【国家(\d{3})")

    paths = sorted(glob.glob(os.path.join(HISTORY_DIR, "*")))
    for path in paths:
        if not os.path.isfile(path):
            continue
        with open(path, encoding="utf-8") as f:
            text = f.read()
        for part in re.split(r"(?=【国家\d{3})", text):
            m = header_re.match(part.strip())
            if not m:
                continue
            num = int(m.group(1))
            tech_raw = extract_tech_from_part(part)
            concept = strip_suffix(tech_raw)
            log_type = infer_log_type(part, concept)
            header_line = part.split("\n", 1)[0]
            survived = "滅亡" not in header_line
            countries[num] = {
                "countryNum": num,
                "techRaw": tech_raw,
                "concept": concept,
                "logType": log_type,
                "survived": survived,
            }

    # 欠番をテンプレートで補完（歴史書未記載分）
    templates = [
        {"concept": "魔力共鳴", "techRaw": "魔力共鳴障壁", "logType": "GeneralPrerequisite"},
        {"concept": "結晶防壁", "techRaw": "結晶防壁", "logType": "CombatLog_StonePhase"},
        {"concept": "風圧拒否", "techRaw": "風圧拒否障壁", "logType": "CombatLog_WindForce"},
        {"concept": "潜行遮蔽", "techRaw": "潜行遮蔽結界", "logType": "CombatLog_Evasion"},
    ]
    for i in range(1, 132):
        if i not in countries:
            t = templates[i % len(templates)]
            countries[i] = {
                "countryNum": i,
                "techRaw": t["techRaw"],
                "concept": t["concept"],
                "logType": t["logType"],
                "survived": i % 7 != 0,
            }
    return dict(sorted(countries.items()))


def effect_payload(log_type: str) -> dict[str, Any]:
    et, val, dur = EFFECT_BY_LOG.get(log_type, ("Debuff_PoiseRegen", 0.12, 4.0))
    return {"effectType": et, "value": val, "duration": dur}


def make_combat_arts(uid: str, concept: str, log_type: str, weapon_name: str) -> list[dict[str, Any]]:
    eff = effect_payload(log_type)
    eff2 = effect_payload(log_type)
    root_name = f"{concept}・基礎型"
    return [
        {
            "artId": f"ART_{uid}_01",
            "artName": root_name,
            "poiseDamage": 18,
            "manaCost": 0,
            "unlocked": True,
            "specialEffects": [],
            "derivatives": [
                {
                    "artId": f"ART_{uid}_02",
                    "artName": f"{concept}・連撃",
                    "poiseDamage": 28,
                    "manaCost": 5,
                    "unlocked": False,
                    "specialEffects": [],
                    "derivatives": [
                        {
                            "artId": f"ART_{uid}_03",
                            "artName": f"{concept}・崩し",
                            "poiseDamage": 42,
                            "manaCost": 10,
                            "unlocked": False,
                            "specialEffects": [eff],
                            "derivatives": [],
                        }
                    ],
                },
                {
                    "artId": f"ART_{uid}_04",
                    "artName": f"{concept}・重刃",
                    "poiseDamage": 36,
                    "manaCost": 8,
                    "unlocked": False,
                    "specialEffects": [],
                    "derivatives": [
                        {
                            "artId": f"ART_{uid}_05",
                            "artName": f"{concept}・極意",
                            "poiseDamage": 65,
                            "manaCost": 18,
                            "unlocked": False,
                            "specialEffects": [eff2],
                            "derivatives": [],
                        }
                    ],
                },
            ],
        }
    ]


def make_generic_combat_arts(uid: str, weapon_name: str) -> list[dict[str, Any]]:
    return [
        {
            "artId": f"ART_{uid}_01",
            "artName": f"研ぎ澄まされた{weapon_name}・一",
            "poiseDamage": 22,
            "manaCost": 0,
            "unlocked": True,
            "specialEffects": [],
            "derivatives": [
                {
                    "artId": f"ART_{uid}_02",
                    "artName": f"研ぎ澄まされた{weapon_name}・二",
                    "poiseDamage": 32,
                    "manaCost": 4,
                    "unlocked": False,
                    "specialEffects": [],
                    "derivatives": [
                        {
                            "artId": f"ART_{uid}_03",
                            "artName": f"研ぎ澄まされた{weapon_name}・三",
                            "poiseDamage": 48,
                            "manaCost": 10,
                            "unlocked": False,
                            "specialEffects": [],
                            "derivatives": [],
                        }
                    ],
                },
                {
                    "artId": f"ART_{uid}_04",
                    "artName": f"研ぎ澄まされた{weapon_name}・四",
                    "poiseDamage": 40,
                    "manaCost": 8,
                    "unlocked": False,
                    "specialEffects": [],
                    "derivatives": [
                        {
                            "artId": f"ART_{uid}_05",
                            "artName": f"研ぎ澄まされた{weapon_name}・極",
                            "poiseDamage": 72,
                            "manaCost": 16,
                            "unlocked": False,
                            "specialEffects": [],
                            "derivatives": [],
                        }
                    ],
                },
            ],
        }
    ]


def make_prod_arts(uid: str, skill_name: str, idx: int) -> list[dict[str, Any]]:
    et, val, dur = PROD_CRAFT_EFFECTS[idx % len(PROD_CRAFT_EFFECTS)]
    eff = {"effectType": et, "value": val, "duration": dur}
    return [
        {
            "artId": f"ART_{uid}_01",
            "artName": f"{skill_name}・基礎工程",
            "poiseDamage": 0,
            "manaCost": 8,
            "unlocked": True,
            "specialEffects": [],
            "derivatives": [
                {
                    "artId": f"ART_{uid}_02",
                    "artName": f"{skill_name}・安定操作",
                    "poiseDamage": 0,
                    "manaCost": 12,
                    "unlocked": False,
                    "specialEffects": [],
                    "derivatives": [
                        {
                            "artId": f"ART_{uid}_03",
                            "artName": f"{skill_name}・精密仕上げ",
                            "poiseDamage": 0,
                            "manaCost": 20,
                            "unlocked": False,
                            "specialEffects": [eff],
                            "derivatives": [],
                        }
                    ],
                }
            ],
        }
    ]


def make_util_arts(uid: str, skill_name: str, base_id: str, prefix: str) -> list[dict[str, Any]]:
    et, val, dur = UTIL_EFFECT_MAP.get(base_id, ("Utility_MindAccelerate", 0.35, 8.0))
    base_eff = {"effectType": et, "value": round(val * 0.7, 2), "duration": round(dur * 0.8, 1)}
    peak_eff = {"effectType": et, "value": val, "duration": dur}
    return [
        {
            "artId": f"ART_{uid}_01",
            "artName": f"{prefix}{skill_name}・基調",
            "poiseDamage": 0,
            "manaCost": 15,
            "unlocked": True,
            "specialEffects": [],
            "derivatives": [
                {
                    "artId": f"ART_{uid}_02",
                    "artName": f"{prefix}{skill_name}・延長",
                    "poiseDamage": 0,
                    "manaCost": 22,
                    "unlocked": False,
                    "specialEffects": [base_eff],
                    "derivatives": [
                        {
                            "artId": f"ART_{uid}_03",
                            "artName": f"{prefix}{skill_name}・極限",
                            "poiseDamage": 0,
                            "manaCost": 30,
                            "unlocked": False,
                            "specialEffects": [peak_eff],
                            "derivatives": [],
                        }
                    ],
                }
            ],
        }
    ]


def evolution_block(
    log_type: str,
    required_value: int = 5,
    required_level: int = 10,
    condition_flag: str = "",
) -> dict[str, Any]:
    return {
        "isEvolvable": True,
        "requiredLevel": required_level,
        "conditionFlag": condition_flag,
        "evolutionCriteria": {
            "requiredLogType": log_type,
            "requiredValue": required_value,
        },
        "nextSkillId": "",
    }


def skill_suffix(base_id: str) -> str:
    if base_id.startswith("SKILL_"):
        return base_id[len("SKILL_") :]
    return base_id


def build_combat_country(country: dict[str, Any], base_id: str, weapon_name: str) -> dict[str, Any]:
    c = country["countryNum"]
    suffix = skill_suffix(base_id)
    uid = f"{suffix}_C{c:03d}"
    concept = country["concept"]
    skill_name = f"{concept}{weapon_name}"
    desc = (
        f"千年のサバイバル史に刻まれた「{country['techRaw']}」の戦闘応用流派。"
        f"魔力干渉による{concept}の立ち回りを{weapon_name}へ落とし込んだ発展形。"
        f"滅亡国の遺技も戦歴ログに刻まれていれば再現可能。"
    )
    return {
        "skillId": f"SKL_{uid}",
        "skillName": skill_name,
        "category": "Combat",
        "description": desc,
        "baseArts": make_combat_arts(uid, concept, country["logType"], weapon_name),
        "evolutionData": evolution_block(
            country["logType"],
            required_value=8 if country["survived"] else 5,
            condition_flag=f"CountryTech_{c:03d}",
        ),
    }


def build_combat_generic(base_id: str, weapon_name: str) -> dict[str, Any]:
    suffix = skill_suffix(base_id)
    uid = f"{suffix}_ADV"
    return {
        "skillId": f"SKL_{uid}",
        "skillName": f"上位{weapon_name}",
        "category": "Combat",
        "description": (
            f"特定の戦術ログ条件を満たさなくても、熟練とレベルだけで到達できる標準的な上位{weapon_name}。"
            f"純粋な攻撃力と体勢削りに特化した研ぎ澄まされた技群。"
        ),
        "baseArts": make_generic_combat_arts(uid, weapon_name),
        "evolutionData": evolution_block("GeneralPrerequisite", required_value=0, required_level=10),
    }


def build_production_adv(base_id: str, skill_name: str, idx: int) -> dict[str, Any]:
    suffix = skill_suffix(base_id)
    uid = f"{suffix}_ADV"
    log_types = ["CraftLog_SuccessCount", "CraftLog_QualityTotal", "CraftLog_BurstZoneTime"]
    return {
        "skillId": f"SKL_{uid}",
        "skillName": f"上位{skill_name}",
        "category": "Production",
        "description": (
            f"クラフトの成功回数と品質の蓄積、あるいは危険域での作業実績から開花する上位{skill_name}。"
            f"後半の技に純度・抽出の底上げ効果を宿す。"
        ),
        "baseArts": make_prod_arts(uid, skill_name, idx),
        "evolutionData": evolution_block(log_types[idx % 3], required_value=12, required_level=8),
    }


def build_utility_deep(base_id: str, skill_name: str, use_peak: bool) -> dict[str, Any]:
    suffix = skill_suffix(base_id)
    prefix = "極致" if use_peak else "深層"
    tag = "PEAK" if use_peak else "DEEP"
    uid = f"{suffix}_{tag}"
    return {
        "skillId": f"SKL_{uid}",
        "skillName": f"{prefix}{skill_name}",
        "category": "Utility",
        "description": (
            f"基礎の{skill_name}が持つ機能を融合せず、持続時間と探知半径のみを純粋に延長・強化した発展形。"
            f"便利枠の単体派生として設計されている。"
        ),
        "baseArts": make_util_arts(uid, skill_name, base_id, prefix),
        "evolutionData": evolution_block("UtilityLog_UsageTime", required_value=20, required_level=6),
    }


def load_catalog() -> list[dict[str, str]]:
    with open(CATALOG_PATH, encoding="utf-8") as f:
        return json.load(f)


def write_batch(filename: str, skills: list[dict[str, Any]]) -> None:
  os.makedirs(OUT_DIR, exist_ok=True)
  path = os.path.join(OUT_DIR, filename)
  payload = {"skills": skills}
  with open(path, "w", encoding="utf-8") as f:
    json.dump(payload, f, ensure_ascii=False, indent=2)
  print(f"wrote {path} ({len(skills)} skills)")


def main() -> None:
    countries = parse_countries()
    os.makedirs(OUT_DIR, exist_ok=True)
    with open(INDEX_PATH, "w", encoding="utf-8") as f:
        json.dump(list(countries.values()), f, ensure_ascii=False, indent=2)
    print(f"country index: {len(countries)} -> {INDEX_PATH}")

    catalog = load_catalog()
    prod_bases = [e for e in catalog if e.get("category") == "Production"]
    util_bases = [e for e in catalog if e.get("category") == "Utility"]

    # Combat: 武器種ごとに131国 + 汎用上位
    for batch_idx, (base_id, weapon_name) in enumerate(COMBAT_WEAPON_IDS):
        batch: list[dict[str, Any]] = []
        for cnum in range(1, 132):
            batch.append(build_combat_country(countries[cnum], base_id, weapon_name))
        batch.append(build_combat_generic(base_id, weapon_name))
        write_batch(f"Evolution_Combat_{weapon_name}_{batch_idx+1:02d}.json", batch)

    # Production: 上位のみ
    prod_skills = [
        build_production_adv(e["skillId"], e["skillName"], i)
        for i, e in enumerate(prod_bases)
    ]
    for i in range(0, len(prod_skills), 40):
        write_batch(f"Evolution_Production_{i//40+1:02d}.json", prod_skills[i : i + 40])

    # Utility: 深層/極致（機能融合なし）
    util_skills: list[dict[str, Any]] = []
    for i, e in enumerate(util_bases):
        util_skills.append(build_utility_deep(e["skillId"], e["skillName"], use_peak=False))
        if i % 2 == 0:
            util_skills.append(build_utility_deep(e["skillId"], e["skillName"], use_peak=True))
    write_batch("Evolution_Utility_01.json", util_skills)

    manifest = {
        "combatBatches": len(COMBAT_WEAPON_IDS),
        "productionBatches": (len(prod_skills) + 39) // 40,
        "utilityBatches": 1,
        "totalCountries": 131,
        "combatWeapons": len(COMBAT_WEAPON_IDS),
    }
    with open(os.path.join(OUT_DIR, "Evolution_Manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print("done", manifest)


if __name__ == "__main__":
    main()
