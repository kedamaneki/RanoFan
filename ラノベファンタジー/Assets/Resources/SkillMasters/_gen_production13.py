#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import json
import os

# (skillId, skillName, prefix, description,
#  rname, rdesc, rmana, b2name, b2desc, b2mana, b2eff,
#  l1name, l1desc, l1mana, l1eff,
#  b4name, b4desc, b4mana, b4eff,
#  l2name, l2desc, l2mana, l2eff)
SKILLS = [
    ("SKILL_PROD_HUNT", "狩猟", "HUNT",
     "魔獣を狩り素材を得る基礎。極めることで伝説獣の完全解体と高級素材の安定入手へ派生する。",
     "基礎狩猟", "安全に魔獣を仕留める。", 0,
     "剥取目利き", "ドロップ率を高める。", 4, [("Hobby_GatherLuck", 1.12, 0.0)],
     "伝説狩", "希少素材の発見率を高める。", 10, [("Hobby_GatherLuck", 1.28, 0.0)],
     "部位確認", "剥取部位の適性を見極める。", 2, [],
     "狩猟記録", "最高狩猟結果をレシピに記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_FISHERY", "漁業", "FSRY",
     "水域から大量の魚介を獲る基礎。極めることで深海漁場の大規模操業へ派生する。",
     "基礎漁", "安全に魚を獲る。", 0,
     "網目利き", "漁獲ドロップ率を高める。", 3, [("Hobby_GatherLuck", 1.14, 0.0)],
     "豊漁", "希少魚介の発見率を高める。", 9, [("Hobby_GatherLuck", 1.25, 0.0)],
     "潮流読み", "水域の魔力適性を見極める。", 2, [],
     "漁場記録", "最高漁獲結果を記録。", 13, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_GLASS", "硝子工", "GLAS",
     "砂と魔力で硝子を作る基礎。極めることで魔力透過レンズ・結晶窓の製作へ派生する。",
     "基礎吹き", "硝子を安全に成形する。", 3,
     "純度熔解", "硝子純度を高める。", 7, [("Craft_PurityBonus", 1.1, 0.0)],
     "透晶仕上げ", "完成品Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "溶解調律", "溶解率を最適化する。", 5, [("Craft_DissolutionRate", 1.1, 0.0)],
     "硝子記録", "最高品質の加工幅を記録。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_BRICK", "煉瓦工", "BRCK",
     "土と火力で煉瓦を焼く基礎。極めることで魔力耐熱建材の大規模築造へ派生する。",
     "基礎成型", "煉瓦を安全に成型する。", 0,
     "焼成調律", "煉瓦純度を高める。", 5, [("Craft_PurityBonus", 1.09, 0.0)],
     "堅牢仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 3.5, 0.0)],
     "土質確認", "土壌の魔力適性を見る。", 3, [],
     "築造記録", "最高品質の焼成幅を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_POTTERY", "陶芸", "POTT",
     "粘土を焼き陶器を作る基礎。極めることで魔力容器・触媒壺の精密製作へ派生する。",
     "基礎陶塑", "粘土を安全に成形する。", 2,
     "釉薬調律", "陶器純度を高める。", 6, [("Craft_PurityBonus", 1.11, 0.0)],
     "名陶仕上げ", "完成品Qualityに固定値を加算。", 13, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "窯温調整", "溶解率を安定させる。", 5, [("Craft_DissolutionRate", 1.12, 0.0)],
     "陶芸記録", "最高品質の焼成幅を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_BREW", "醸造", "BREW",
     "発酵させて飲料を作る基礎。極めることで魔力活性化の霊酒製造へ派生する。",
     "基礎醸造", "安全な発酵を始める。", 2,
     "発酵抽出", "有効成分の抽出を高める。", 7, [("Craft_ExtractionBonus", 1.14, 0.0)],
     "熟成仕上げ", "完成品Qualityに固定値を加算。", 13, [("Craft_PurityFlatBonus", 3.5, 0.0)],
     "酵母調律", "溶解率を最適化する。", 5, [("Craft_DissolutionRate", 1.12, 0.0)],
     "醸造記録", "最高品質の醸造幅を記録。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_PAPER", "製紙", "PAPR",
     "繊維から紙を作る基礎。極めることで魔力導線付き魔導紙の製造へ派生する。",
     "基礎抄紙", "紙を安全に抄き出す。", 1,
     "繊維抽出", "有効繊維の抽出を高める。", 6, [("Craft_ExtractionBonus", 1.1, 0.0)],
     "上質仕上げ", "完成品Qualityに固定値を加算。", 11, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "紙目確認", "繊維の魔力適性を見る。", 3, [],
     "製紙記録", "最高品質の抄紙幅を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_BOOKBIND", "製本", "BKBD",
     "紙と革で書物を綴じる基礎。極めることで魔力封印の魔導書製作へ派生する。",
     "基礎綴じ", "書物を安全に綴じる。", 2,
     "純度綴じ", "綴じ純度を高める。", 6, [("Craft_PurityBonus", 1.08, 0.0)],
     "名綴じ仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 3.5, 0.0)],
     "版面確認", "版面の魔力適性を見る。", 3, [],
     "製本記録", "最高品質の綴じ幅を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_ENGRAVE", "彫金", "ENGR",
     "金属に紋様を刻む基礎。極めることで魔力回路刻印の精密加工へ派生する。",
     "基礎刻印", "金属に安全に刻む。", 3,
     "精密彫刻", "刻印純度を高める。", 8, [("Craft_PurityBonus", 1.12, 0.0)],
     "名彫仕上げ", "完成品Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 4.5, 0.0)],
     "紋様設計", "刻印の魔力適性を見る。", 4, [],
     "彫金記録", "最高精度の刻印幅を記録。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_EMBROID", "刺繍", "EMBR",
     "布に刺繍で紋様を施す基礎。極めることで魔力織り紋章の衣装製作へ派生する。",
     "基礎刺繍", "安全に刺繍する。", 1,
     "糸調律", "刺繍純度を高める。", 5, [("Craft_PurityBonus", 1.1, 0.0)],
     "名刺仕上げ", "完成品Qualityに固定値を加算。", 11, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "織目設計", "刺繍の魔力適性を見る。", 3, [],
     "刺繍記録", "最高品質の刺繍幅を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_FURNITURE", "家具工", "FURN",
     "木材で家具を作る基礎。極めることで魔力導線付き工房設備の製作へ派生する。",
     "基礎組立", "家具を安全に組み立てる。", 0,
     "木目調律", "木材純度を高める。", 5, [("Craft_PurityBonus", 1.09, 0.0)],
     "匠の仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "構造確認", "家具の魔力適性を見る。", 3, [],
     "家具記録", "最高品質の組立幅を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_INSTRUMENT", "楽器工", "INST",
     "木材と金属で楽器を作る基礎。極めることで魔力共鳴楽器の製作へ派生する。",
     "基礎製作", "楽器を安全に組み立てる。", 3,
     "共鳴調律", "共鳴純度を高める。", 8, [("Craft_PurityBonus", 1.11, 0.0)],
     "名器仕上げ", "完成品Qualityに固定値を加算。", 15, [("Craft_PurityFlatBonus", 5.0, 0.0)],
     "音孔調整", "溶解率を最適化する。", 6, [("Craft_DissolutionRate", 1.1, 0.0)],
     "楽器記録", "最高品質の調律幅を記録。", 19, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_GEM", "宝石工", "GEM",
     "原石を研磨し宝石にする基礎。極めることで魔力結晶宝石の精密研磨へ派生する。",
     "基礎研磨", "宝石を安全に研磨する。", 4,
     "純度研磨", "宝石純度を高める。", 9, [("Craft_PurityBonus", 1.15, 0.0)],
     "極光仕上げ", "完成品Qualityに固定値を加算。", 16, [("Craft_PurityFlatBonus", 5.0, 0.0)],
     "原石目利き", "原石の魔力適性を見極める。", 4, [],
     "宝石記録", "最高純度の研磨幅を記録。", 20, [("Craft_RecipeRecord", 1.0, 0.0)]),
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
            "category": "Production",
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
    assert len(out) == 13
    for x in out:
        c = count_arts(x["skillMaster"]["baseArts"])
        assert c == 5, f"{x['skillMaster']['skillName']}: {c}"

    base = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(base, "SkillMasters_Production13.json")
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
    cat_map = {c["skillId"]: c for c in catalog}
    for x in out:
        sm = x["skillMaster"]
        if sm["skillId"] in cat_map:
            cat_map[sm["skillId"]]["skillName"] = sm["skillName"]
            cat_map[sm["skillId"]]["category"] = "Production"
        else:
            catalog.append({"skillId": sm["skillId"], "skillName": sm["skillName"], "category": "Production"})
    with open(catalog_path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=2)

    print(out_path, len(out))


if __name__ == "__main__":
    main()
