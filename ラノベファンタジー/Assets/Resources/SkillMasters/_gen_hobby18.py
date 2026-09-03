#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import json
import os

SKILLS = [
    ("SKILL_HOBBY_RESEARCH", "研究", "RSH",
     "現象を調べ原理を探る基礎。極めることで未知魔導現象の体系的研究へ派生する。",
     "基礎調査", "安全に現象を観測する。", 2,
     "深層抽出", "研究データの抽出を高める。", 7, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "理論構築", "抽出倍率をさらに引き上げる。", 13, [("Craft_ExtractionBonus", 1.22, 0.0)],
     "仮説検証", "溶解率を安定させる。", 5, [("Craft_DissolutionRate", 1.1, 0.0)],
     "研究記録", "最高品質の研究幅を記録。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_EXPERIMENT", "実験", "EXP",
     "試行錯誤で真理を確かめる基礎。極めることで大規模魔導実験装置の運用へ派生する。",
     "基礎実験", "安全に実験を行う。", 3,
     "変数制御", "実験の抽出効率を高める。", 8, [("Craft_ExtractionBonus", 1.15, 0.0)],
     "再現成功", "完成品Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 3.5, 0.0)],
     "条件調律", "溶解率を最適化する。", 6, [("Craft_DissolutionRate", 1.12, 0.0)],
     "実験記録", "最高再現結果を記録。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_DRAFTING", "製図", "DRFT",
     "設計図を描き構造を可視化する基礎。極めることで魔力工房の完全設計図製作へ派生する。",
     "基礎製図", "安全に図面を描く。", 1,
     "精度製図", "図面純度を高める。", 6, [("Craft_PurityBonus", 1.1, 0.0)],
     "名図仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "尺度確認", "図面の魔力適性を見る。", 3, [],
     "図面記録", "最高精度の製図幅を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_SURVEY", "測量", "SRVY",
     "土地と地形を測る基礎。極めることで大陸規模の魔力地形測量へ派生する。",
     "基礎測量", "安全に距離を測る。", 0,
     "精密測量", "測量データの発見率を高める。", 5, [("Hobby_GatherLuck", 1.1, 0.0)],
     "広域測量", "希少地形データの発見率を高める。", 10, [("Hobby_GatherLuck", 1.22, 0.0)],
     "地盤確認", "地盤の魔力適性を見る。", 3, [],
     "測量記録", "最高測量結果を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_MEASURE", "測定", "MSR",
     "数値と魔力パラメータを計測する基礎。極めることで工房全パラメータの精密測定へ派生する。",
     "基礎測定", "安全に数値を読む。", 2,
     "精密測定", "測定純度を高める。", 6, [("Craft_PurityBonus", 1.08, 0.0)],
     "極限測定", "完成品Qualityに固定値を加算。", 11, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "誤差補正", "溶解率を安定させる。", 4, [("Craft_DissolutionRate", 1.08, 0.0)],
     "測定記録", "最高測定結果を記録。", 13, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_RECORD", "記録", "RECD",
     "観察と成果を書き留める基礎。極めることで全工房レシピの完全アーカイブへ派生する。",
     "基礎記録", "安全にデータを記す。", 1,
     "精密記録", "記録の抽出効率を高める。", 5, [("Craft_ExtractionBonus", 1.1, 0.0)],
     "完全記録", "レシピ記録を有効化する。", 9, [("Craft_RecipeRecord", 1.0, 0.0)],
     "分類整理", "データ純度を高める。", 4, [("Craft_PurityBonus", 1.06, 0.0)],
     "秘録封印", "最高品質の記録幅を固定。", 16, [("Craft_PurityFlatBonus", 3.5, 0.0)]),
    ("SKILL_HOBBY_OBSERVE", "観察", "OBSV",
     "対象をじっくり見極める基礎。極めることで希少素材の自動発見へ派生する。",
     "基礎観察", "安全に対象を観察する。", 1,
     "目利き観察", "発見率を高める。", 4, [("Hobby_GatherLuck", 1.12, 0.0)],
     "深層観察", "希少対象の発見率を高める。", 9, [("Hobby_GatherLuck", 1.26, 0.0)],
     "性質読取", "対象の魔力適性を見る。", 3, [],
     "観察記録", "最高観察結果を記録。", 12, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_PHYSICS", "物理学", "PHYS",
     "物質と力の法則を学ぶ基礎。極めることで魔力力学の応用工学へ派生する。",
     "基礎力学", "力と運動を安全に分析する。", 3,
     "魔力力学", "抽出効率を高める。", 8, [("Craft_ExtractionBonus", 1.14, 0.0)],
     "法則応用", "完成品Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "平衡調律", "溶解率を安定させる。", 6, [("Craft_DissolutionRate", 1.1, 0.0)],
     "力学記録", "最高解析結果を記録。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_MATH", "数学", "MATH",
     "数と構造を扱う基礎。極めることで魔導計算式の完全最適化へ派生する。",
     "基礎演算", "安全に数式を組む。", 2,
     "精密計算", "計算純度を高める。", 6, [("Craft_PurityBonus", 1.1, 0.0)],
     "極限演算", "抽出倍率を最大化する。", 12, [("Craft_ExtractionBonus", 1.2, 0.0)],
     "誤差解析", "溶解率を最適化する。", 5, [("Craft_DissolutionRate", 1.1, 0.0)],
     "数式記録", "最高計算結果を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_SCIENCE", "科学", "SCIE",
     "総合的な科学的手法の基礎。極めることで万能魔導研究の頂点へ派生する。",
     "基礎科学", "安全に仮説を立てる。", 2,
     "分析科学", "抽出効率を高める。", 7, [("Craft_ExtractionBonus", 1.13, 0.0)],
     "応用科学", "完成品Qualityに固定値を加算。", 13, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "検証科学", "純度を高める。", 6, [("Craft_PurityBonus", 1.1, 0.0)],
     "科学記録", "最高研究成果を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_GEOLOGY", "地学", "GEOL",
     "大地と鉱脈を学ぶ基礎。極めることで深層結晶脈の完全把握へ派生する。",
     "基礎地学", "安全に地層を調べる。", 1,
     "鉱脈読み", "鉱石発見率を高める。", 5, [("Hobby_GatherLuck", 1.14, 0.0)],
     "深層地学", "希少鉱石の発見率を高める。", 10, [("Hobby_GatherLuck", 1.28, 0.0)],
     "地質解析", "地層の魔力適性を見る。", 3, [],
     "地学記録", "最高調査結果を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_ARCHAEO", "考古学", "ARCH",
     "遺跡と古代遺物を掘り起こす基礎。極めることで失われた文明の完全復元へ派生する。",
     "基礎発掘", "安全に遺物を掘り出す。", 2,
     "遺物目利き", "遺物発見率を高める。", 6, [("Hobby_GatherLuck", 1.15, 0.0)],
     "文明解読", "希少遺物の発見率を高める。", 11, [("Hobby_GatherLuck", 1.25, 0.0)],
     "層位確認", "遺跡の魔力適性を見る。", 4, [],
     "考古記録", "最高発掘結果を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_MECH", "機械学", "MECH",
     "機構と装置を学ぶ基礎。極めることで自動魔導機械の設計製作へ派生する。",
     "基礎機構", "安全に機構を組む。", 3,
     "精密機構", "機構純度を高める。", 8, [("Craft_PurityBonus", 1.11, 0.0)],
     "名機仕上げ", "完成品Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 4.5, 0.0)],
     "動力調律", "溶解率を最適化する。", 6, [("Craft_DissolutionRate", 1.12, 0.0)],
     "機械記録", "最高設計結果を記録。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_BIOLOGY", "生物学", "BIOL",
     "生命の構造を学ぶ基礎。極めることで魔獣生態の完全解析へ派生する。",
     "基礎解剖", "安全に生物を調べる。", 2,
     "成分抽出", "生物成分の抽出を高める。", 7, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "生態解析", "希少生物素材の発見率を高める。", 10, [("Hobby_GatherLuck", 1.2, 0.0)],
     "組織確認", "生物の魔力適性を見る。", 4, [],
     "生物記録", "最高解析結果を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_BOTANY", "植物学", "BOTN",
     "植物と薬草を学ぶ基礎。極めることで希少魔力植物の大規模栽培研究へ派生する。",
     "基礎植物観察", "安全に植物を調べる。", 1,
     "薬草目利き", "植物素材の発見率を高める。", 4, [("Hobby_GatherLuck", 1.13, 0.0)],
     "希少植物", "希少薬草の発見率を高める。", 9, [("Hobby_GatherLuck", 1.27, 0.0)],
     "生態調査", "植物の魔力適性を見る。", 3, [],
     "植物記録", "最高調査結果を記録。", 12, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_ZOOLOGY", "動物学", "ZOOL",
     "動物の生態を学ぶ基礎。極めることで伝説獣の生息域完全特定へ派生する。",
     "基礎動物観察", "安全に動物を観察する。", 1,
     "生態目利き", "動物素材の発見率を高める。", 5, [("Hobby_GatherLuck", 1.12, 0.0)],
     "伝説生態", "希少動物の発見率を高める。", 10, [("Hobby_GatherLuck", 1.26, 0.0)],
     "行動解析", "動物の魔力適性を見る。", 3, [],
     "動物記録", "最高観察結果を記録。", 13, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_MONSTER", "魔物学", "MONS",
     "魔獣と魔物の性質を学ぶ基礎。極めることで災害級魔獣の完全攻略研究へ派生する。",
     "基礎魔物観察", "安全に魔物を調べる。", 3,
     "弱点解析", "魔物素材の発見率を高める。", 7, [("Hobby_GatherLuck", 1.16, 0.0)],
     "完全解析", "希少魔物素材の発見率を高める。", 12, [("Hobby_GatherLuck", 1.3, 0.0)],
     "魔力読取", "魔物の魔力適性を見る。", 5, [("Craft_ExtractionBonus", 1.1, 0.0)],
     "魔物記録", "最高解析結果を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_HOBBY_ASTRON", "天文学", "ASTR",
     "星と天象を学ぶ基礎。極めることで魔力星座の完全予測へ派生する。",
     "基礎観星", "安全に星を観測する。", 2,
     "星図読取", "天象データの発見率を高める。", 6, [("Hobby_GatherLuck", 1.1, 0.0)],
     "深空観測", "希少天象の発見率を高める。", 11, [("Hobby_GatherLuck", 1.24, 0.0)],
     "軌道計算", "抽出効率を高める。", 5, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "天文記録", "最高観測結果を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
]


def effects(arr):
    return [{"effectType": e[0], "value": e[1], "duration": e[2]} for e in arr]


def build_skill(row):
    sid, sname, prefix, sdesc, rname, rdesc, rmana, \
        b2name, b2desc, b2mana, b2eff, \
        l1name, l1desc, l1mana, l1eff, \
        b4name, b4desc, b4mana, b4eff, \
        l2name, l2desc, l2mana, l2eff = row

    def mk_art(aid, name, desc, mana, effs, unlocked, derivs, num):
        return {
            "artId": aid,
            "artName": name,
            "description": f"{desc}（技{num}個目）",
            "poiseDamage": 0,
            "manaCost": mana,
            "unlocked": unlocked,
            "specialEffects": effects(effs),
            "derivatives": derivs,
        }

    leaf1 = mk_art(f"ART_{prefix}_L1", l1name, l1desc, l1mana, l1eff, False, [], 3)
    branch2 = mk_art(f"ART_{prefix}_B2", b2name, b2desc, b2mana, b2eff, False, [leaf1], 2)
    leaf2 = mk_art(f"ART_{prefix}_L2", l2name, l2desc, l2mana, l2eff, False, [], 5)
    branch4 = mk_art(f"ART_{prefix}_B4", b4name, b4desc, b4mana, b4eff, False, [leaf2], 4)
    root = mk_art(f"ART_{prefix}_ROOT", rname, rdesc, rmana, [], True, [branch2, branch4], 1)

    return {
        "contentType": "SkillMaster",
        "skillMaster": {
            "skillId": sid,
            "skillName": sname,
            "category": "Hobby",
            "description": sdesc,
            "baseArts": [root],
        },
    }


def count_arts(arts):
    n = len(arts)
    for a in arts:
        n += count_arts(a.get("derivatives", []))
    return n


def main():
    out = [build_skill(r) for r in SKILLS]
    assert len(out) == 18
    for x in out:
        c = count_arts(x["skillMaster"]["baseArts"])
        assert c == 5, f"{x['skillMaster']['skillName']}: {c}"

    base = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(base, "SkillMasters_Hobby18.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    master_path = os.path.join(base, "SkillMasters.json")
    existing = json.load(open(master_path, encoding="utf-8")).get("skills", [])
    new_ids = {x["skillMaster"]["skillId"] for x in out}
    merged = [s for s in existing if s.get("skillId") not in new_ids]
    merged.extend([x["skillMaster"] for x in out])
    with open(master_path, "w", encoding="utf-8") as f:
        json.dump({"skills": merged}, f, ensure_ascii=False, indent=2)

    catalog_path = os.path.join(base, "SkillMasters_Catalog.json")
    catalog = json.load(open(catalog_path, encoding="utf-8"))
    cat_ids = {c["skillId"] for c in catalog}
    for x in out:
        sm = x["skillMaster"]
        if sm["skillId"] not in cat_ids:
            catalog.append({"skillId": sm["skillId"], "skillName": sm["skillName"], "category": "Hobby"})
    with open(catalog_path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=2)

    print(out_path, len(out))


if __name__ == "__main__":
    main()
