#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import json
import os

SKILLS = [
    ("SKILL_COMBAT_MANACIRCLE", "魔力循環", "MANAC",
     "体内の魔力を脈動させ、戦闘効率を高める基礎技術。極めることで、あらゆる上位魔導戦闘スキルへと派生する。",
     "魔力脈動", "魔力の巡りを僅かに活性化させる構え。", 0, 5,
     "魔力奔流", "一時的に魔力の再生力を高める。", 0, 12, [("Psychic_Regen_Mana", 1.5, 5.0)],
     "魔力解放", "全消費魔力を15%カットする。", 0, 20, [("Psychic_ManaControl", 0.15, 8.0)],
     "魔力纏い", "魔力を皮膚に薄く展開する。", 0, 10, [],
     "魔力殻", "筋力と体勢上限を底上げする。", 0, 25, [("Psychic_Boost_STR", 5.0, 6.0), ("Buff_Poise", 0.2, 6.0)]),
    ("SKILL_COMBAT_MINDUNITY", "精神統一", "MIND",
     "精神を一点に集中し魔力操作を安定させる基礎。上位の精神魔導・予知戦闘へ分岐する。",
     "静念", "呼吸を整え魔力を安定させる。", 0, 4,
     "集中", "魔力消費を軽減する。", 0, 10, [("Psychic_ManaControl", 0.1, 6.0)],
     "無想", "魔力再生を高める。", 0, 16, [("Psychic_Regen_Mana", 2.0, 6.0)],
     "精神盾", "防御を高める構え。", 0, 8, [],
     "天人合一", "筋力と防御を同時強化。", 0, 22, [("Psychic_Boost_STR", 4.0, 5.0), ("Psychic_Boost_DEF", 4.0, 5.0)]),
    ("SKILL_COMBAT_MANABARRIER", "魔力障壁", "MBAR",
     "魔力で障壁を張る防御の基礎。上位の絶対障壁・反射流派へ進化する。",
     "薄障", "薄い魔力障壁を展開。", 0, 6,
     "強障", "防御力を高める。", 0, 12, [("Psychic_Boost_DEF", 6.0, 5.0)],
     "再生障", "障壁維持と魔力再生。", 0, 18, [("Psychic_Regen_Mana", 1.8, 5.0)],
     "体勢障", "体勢耐性を高める。", 0, 10, [],
     "絶対障", "体勢上限と防御を大幅強化。", 0, 24, [("Buff_Poise", 0.3, 8.0), ("Psychic_Boost_DEF", 8.0, 8.0)]),
    ("SKILL_COMBAT_SELFREGEN", "自己再生", "REGEN",
     "生命力と魔力の自己修復の基礎。不死身・超再生の上位流派へ派生する。",
     "自愈", "わずかに魔力を回復する構え。", 0, 5,
     "魔力回復", "戦闘中の魔力再生。", 0, 10, [("Psychic_Regen_Mana", 2.5, 6.0)],
     "深癒", "持続的な魔力循環。", 0, 18, [("Psychic_Regen_Mana", 3.5, 8.0)],
     "剛身", "防御を補強する。", 0, 8, [("Psychic_Boost_DEF", 3.0, 5.0)],
     "超再生", "筋力と体勢を底上げする。", 0, 20, [("Psychic_Boost_STR", 5.0, 6.0), ("Buff_Poise", 0.15, 6.0)]),
    ("SKILL_COMBAT_IRONBODY", "剛体術", "IRON",
     "肉体を硬化させる基礎。金剛不壊・破砕拳の流派へ分岐する。",
     "硬気", "身体をわずかに硬化。", 5, 4,
     "鉄皮", "防御力を強化。", 8, 10, [("Psychic_Boost_DEF", 7.0, 6.0)],
     "金身", "体勢耐性を高める。", 12, 14, [("Buff_Poise", 0.25, 6.0)],
     "剛打", "硬化した打撃。", 18, 8, [],
     "不壊", "筋力と防御の極致。", 25, 22, [("Psychic_Boost_STR", 6.0, 5.0), ("Psychic_Boost_DEF", 10.0, 5.0)]),
    ("SKILL_COMBAT_FLASHSTEP", "瞬身術", "FLASH",
     "短距離の高速移動の基礎。残像・空間跳躍の上位技法へ展開する。",
     "足運び", "素早い足さばき。", 0, 3,
     "瞬足", "魔力消費を抑える。", 0, 8, [("Psychic_ManaControl", 0.12, 5.0)],
     "残像", "移動後の体勢強化。", 0, 14, [("Buff_Poise", 0.2, 4.0)],
     "疾風", "筋力を一時的に高める。", 5, 10, [("Psychic_Boost_STR", 4.0, 4.0)],
     "空閃", "魔力再生と機動の融合。", 0, 20, [("Psychic_Regen_Mana", 2.0, 5.0), ("Psychic_ManaControl", 0.1, 5.0)]),
    ("SKILL_COMBAT_MINDEYE", "心眼術", "MEYE",
     "感覚を研ぎ澄ます基礎。予知・弱点看破の上位感知流派へ進む。",
     "見切り", "敵の動きを読む構え。", 0, 4,
     "心眼", "体勢耐性を高める。", 0, 10, [("Buff_Poise", 0.18, 5.0)],
     "識破", "魔力消費を軽減。", 0, 14, [("Psychic_ManaControl", 0.15, 6.0)],
     "集中撃", "筋力を集中させる一撃。", 15, 8, [("Psychic_Boost_STR", 5.0, 4.0)],
     "無明", "魔力循環と防御の極意。", 0, 22, [("Psychic_Regen_Mana", 2.5, 6.0), ("Psychic_Boost_DEF", 5.0, 6.0)]),
    ("SKILL_COMBAT_SPIRIT", "気合", "SPIRIT",
     "気を込めた威圧と強化の基礎。覇気・殺気の上位流派へ派生する。",
     "気合い", "気を込めた一喝。", 10, 5,
     "威圧", "筋力を高める。", 15, 10, [("Psychic_Boost_STR", 6.0, 5.0)],
     "気刃", "気を刃に乗せる。", 20, 14, [("Psychic_Boost_STR", 8.0, 4.0)],
     "気盾", "防御を補強。", 0, 12, [("Psychic_Boost_DEF", 5.0, 5.0)],
     "覇気", "体勢と筋力の頂点。", 30, 24, [("Buff_Poise", 0.3, 6.0), ("Psychic_Boost_STR", 10.0, 6.0)]),
    ("SKILL_COMBAT_HIDESPIRIT", "隠気術", "HIDE",
     "気を殺し姿を隠す基礎。完全隠密・暗殺魔導へ分岐する。",
     "潜気", "気配を抑える。", 0, 4,
     "消気", "魔力消費を抑える。", 0, 9, [("Psychic_ManaControl", 0.12, 6.0)],
     "影歩", "体勢を保ちながら移動。", 0, 12, [("Buff_Poise", 0.15, 5.0)],
     "急所撃", "隠れ身からの一撃。", 25, 10, [("Psychic_Boost_STR", 5.0, 3.0)],
     "無気", "魔力再生と防御の静寂。", 0, 20, [("Psychic_Regen_Mana", 2.0, 6.0), ("Psychic_Boost_DEF", 6.0, 6.0)]),
    ("SKILL_COMBAT_COUNTER", "反撃術", "CNT",
     "受けた力を返す基礎。完全反撃・因果返しの流派へ進化する。",
     "構え", "反撃の準備姿勢。", 0, 3,
     "受勢", "体勢を固める。", 0, 8, [("Buff_Poise", 0.2, 5.0)],
     "返し", "反撃時の筋力強化。", 20, 12, [("Psychic_Boost_STR", 7.0, 4.0)],
     "鉄壁返", "防御を高めて反撃。", 15, 10, [("Psychic_Boost_DEF", 6.0, 5.0)],
     "因果返", "魔力効率と体勢の極意。", 35, 22, [("Psychic_ManaControl", 0.15, 6.0), ("Buff_Poise", 0.25, 6.0)]),
    ("SKILL_COMBAT_PARRY", "受け流し", "PARRY",
     "攻撃をそらす基礎。完全無欠の受け流し流派へ派生する。",
     "受け", "基本の受け流し。", 0, 2,
     "流し", "体勢を保つ受け。", 0, 6, [("Buff_Poise", 0.22, 5.0)],
     "巧流", "魔力消費を軽減。", 0, 12, [("Psychic_ManaControl", 0.1, 6.0)],
     "反打", "流しからの打撃。", 18, 10, [("Psychic_Boost_STR", 5.0, 4.0)],
     "無双受", "防御と魔力再生の極意。", 0, 20, [("Psychic_Boost_DEF", 8.0, 6.0), ("Psychic_Regen_Mana", 2.0, 5.0)]),
    ("SKILL_COMBAT_IRONWALL", "鉄壁", "WALL",
     "鉄壁の如き防御の基礎。要塞・絶対防御の上位流派へ展開する。",
     "防壁", "基本の防御構え。", 0, 5,
     "厚壁", "防御力を大幅強化。", 0, 12, [("Psychic_Boost_DEF", 10.0, 6.0)],
     "体勢壁", "体勢上限を高める。", 0, 14, [("Buff_Poise", 0.28, 7.0)],
     "魔力壁", "魔力で障壁を補強。", 0, 16, [("Psychic_Regen_Mana", 1.5, 5.0)],
     "絶壁", "筋力と防御の鉄壁。", 10, 24, [("Psychic_Boost_DEF", 12.0, 8.0), ("Psychic_Boost_STR", 4.0, 8.0)]),
    ("SKILL_COMBAT_SPRINT", "疾走", "SPRINT",
     "高速移動と突進の基礎。音速・瞬間移動の上位機動へ分岐する。",
     "疾走", "素早く前進する。", 8, 4,
     "突進", "筋力を乗せた突進。", 15, 8, [("Psychic_Boost_STR", 5.0, 4.0)],
     "風走", "魔力消費を抑える。", 0, 12, [("Psychic_ManaControl", 0.12, 5.0)],
     "連走", "体勢を保つ連続移動。", 12, 10, [("Buff_Poise", 0.18, 5.0)],
     "神走", "魔力再生と機動の融合。", 20, 20, [("Psychic_Regen_Mana", 2.5, 6.0), ("Psychic_Boost_STR", 7.0, 5.0)]),
    ("SKILL_COMBAT_VAJRA", "金剛", "VAJRA",
     "金剛の如き不坏の基礎。羅漢・明王の上位体術へ進化する。",
     "金剛印", "身体を金剛の如く固める。", 5, 6,
     "不坏", "防御と体勢を強化。", 10, 12, [("Psychic_Boost_DEF", 8.0, 6.0), ("Buff_Poise", 0.2, 6.0)],
     "明王", "筋力を爆発させる。", 25, 16, [("Psychic_Boost_STR", 9.0, 5.0)],
     "真言", "魔力を節約する。", 0, 10, [("Psychic_ManaControl", 0.15, 6.0)],
     "大威徳", "全能力の極致。", 35, 28, [("Psychic_Boost_STR", 10.0, 6.0), ("Psychic_Boost_DEF", 10.0, 6.0), ("Buff_Poise", 0.25, 6.0)]),
    ("SKILL_COMBAT_FAJIN", "発勁", "FAJIN",
     "内勁を発する基礎。透勁・震盪の上位打撃流派へ派生する。",
     "勁打", "内勁を込めた打撃。", 18, 5,
     "透勁", "防御を無視する勁。", 28, 12, [("Psychic_Boost_STR", 7.0, 4.0)],
     "震勁", "体勢を崩す震盪。", 40, 18, [("Buff_Poise", 0.2, 4.0)],
     "蓄勁", "魔力を蓄え消費を抑える。", 0, 10, [("Psychic_ManaControl", 0.12, 6.0)],
     "爆勁", "魔力再生と筋力の爆発。", 55, 24, [("Psychic_Regen_Mana", 2.0, 5.0), ("Psychic_Boost_STR", 12.0, 5.0)]),
]


def effects(arr):
    return [{"effectType": e[0], "value": e[1], "duration": e[2]} for e in arr]


def art(aid, name, desc, poise, mana, effs, unlocked, derivatives):
    return {
        "artId": aid,
        "artName": name,
        "description": desc + "（技{}個目）".format(_art_num_from_desc(desc) if False else ""),
        "poiseDamage": poise,
        "manaCost": mana,
        "unlocked": unlocked,
        "specialEffects": effects(effs),
        "derivatives": derivatives,
    }


def build_skill(row, art_nums):
    sid, sname, prefix, sdesc, rname, rdesc, rpoise, rmana, \
        b2name, b2desc, b2poise, b2mana, b2eff, \
        l1name, l1desc, l1poise, l1mana, l1eff, \
        b4name, b4desc, b4poise, b4mana, b4eff, \
        l2name, l2desc, l2poise, l2mana, l2eff = row

    def mk_art(aid, name, desc, poise, mana, effs, unlocked, derivs, num):
        return {
            "artId": aid,
            "artName": name,
            "description": f"{desc}（技{num}個目）",
            "poiseDamage": poise,
            "manaCost": mana,
            "unlocked": unlocked,
            "specialEffects": effects(effs),
            "derivatives": derivs,
        }

    leaf1 = mk_art(f"ART_{prefix}_L1", l1name, l1desc, l1poise, l1mana, l1eff, False, [], 3)
    branch2 = mk_art(f"ART_{prefix}_B2", b2name, b2desc, b2poise, b2mana, b2eff, False, [leaf1], 2)
    leaf2 = mk_art(f"ART_{prefix}_L2", l2name, l2desc, l2poise, l2mana, l2eff, False, [], 5)
    branch4 = mk_art(f"ART_{prefix}_B4", b4name, b4desc, b4poise, b4mana, b4eff, False, [leaf2], 4)
    root = mk_art(f"ART_{prefix}_ROOT", rname, rdesc, rpoise, rmana, [], True, [branch2, branch4], 1)

    return {
        "contentType": "SkillMaster",
        "skillMaster": {
            "skillId": sid,
            "skillName": sname,
            "category": "Combat",
            "description": sdesc,
            "baseArts": [root],
        },
    }


def main():
    out = [build_skill(r, None) for r in SKILLS]
    assert len(out) == 15
    base = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(base, "SkillMasters_CombatPsychic15.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    # merge with combat25 into SkillMasters.json
    master_path = os.path.join(base, "SkillMasters.json")
    combat25_path = os.path.join(base, "SkillMasters_Combat25.json")
    all_skills = []
    if os.path.exists(combat25_path):
        c25 = json.load(open(combat25_path, encoding="utf-8"))
        all_skills.extend([x["skillMaster"] for x in c25])
    all_skills.extend([x["skillMaster"] for x in out])
    with open(master_path, "w", encoding="utf-8") as f:
        json.dump({"skills": all_skills}, f, ensure_ascii=False, indent=2)

    catalog_path = os.path.join(base, "SkillMasters_Catalog.json")
    catalog = json.load(open(catalog_path, encoding="utf-8")) if os.path.exists(catalog_path) else []
    catalog.extend([{"skillId": x["skillMaster"]["skillId"], "skillName": x["skillMaster"]["skillName"], "category": "Combat"} for x in out])
    with open(catalog_path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=2)

    print(out_path, len(out))


if __name__ == "__main__":
    main()
