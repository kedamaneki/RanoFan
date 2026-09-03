#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import json

SKILLS = [
    ("SKILL_COMBAT_SWORD", "剣術", "SWORD",
     "刃物の扱いに関する最も基礎的な戦闘スキル。熟練により長剣・細剣・大剣など多様な流派へ分岐する。",
     "スラッシュ", "基本の横薙ぎ。", 15, 0,
     "突き", "鋭い踏み込み突き。", 25, 4, [],
     "連続刺突", "体勢回復を遅延させる高速突き。", 45, 12, [("Debuff_PoiseRegen", 0.15, 4.0)],
     "重撃", "大きく振り下ろす一撃。", 35, 6, [],
     "兜割り", "装甲溶解の予兆を付与する渾身の一撃。", 70, 20, [("Debuff_ArmorDissolve", 0.2, 6.0)]),
    ("SKILL_COMBAT_GREATSWORD", "大剣術", "GSWORD",
     "巨刃の慣性を活かす重戦闘の基礎。破壊特化や広域薙ぎの上位流派へ展開する。",
     "大振り", "重量を伴う基本斬撃。", 22, 0,
     "薙ぎ払い", "広い軌道の一撃。", 38, 8, [],
     "地鳴り", "体勢回復を完全停止する叩き込み。", 65, 22, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)],
     "踏み込み", "前進を伴う打撃。", 40, 10, [],
     "断層", "高poiseDamageの破壊斬。", 85, 28, [("Debuff_PoiseRegen", 0.25, 6.0)]),
    ("SKILL_COMBAT_DAGGER", "短剣術", "DAGGER",
     "短刃の迅速な攻防の基礎。暗殺・連撃・毒刃などの特化へ枝分かれする。",
     "刺突", "最小動作の一刺し。", 12, 0,
     "急所突き", "要害を狙う突き。", 28, 6, [],
     "影刺し", "体勢回復を大幅に低下させる。", 42, 14, [("Debuff_PoiseRegen", 0.22, 5.0)],
     "裏斬り", "体勢を崩す切り返し。", 32, 8, [],
     "無音刃", "体勢回復を一時停止する。", 55, 18, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_SPEAR", "槍術", "SPEAR",
     "長柄の拒止距離を学ぶ基礎。突き特化・投槍・騎兵槍などへ進化する。",
     "突き", "中距離の基本突き。", 18, 0,
     "払い", "槍身を使った払い除け。", 30, 5, [],
     "拒止突き", "体勢回復速度を低下させる。", 48, 12, [("Debuff_PoiseRegen", 0.18, 5.0)],
     "叩き伏せ", "柄打ちによる体勢崩し。", 36, 8, [],
     "穿ち", "体勢を貫く一突き。", 72, 20, [("Debuff_PoiseRecoveryHalt", 1.0, 2.0)]),
    ("SKILL_COMBAT_AXE", "斧術", "AXE",
     "斧の重さと切断力の基礎。戦斧・投斧・双斧の流派へ派生する。",
     "斬り", "基本の斧斬り。", 20, 0,
     "割り", "盾持ちを想定した打撃。", 34, 7, [],
     "砕き", "装甲溶解予兆を付与。", 58, 16, [("Debuff_ArmorDissolve", 0.12, 5.0)],
     "旋回", "回旋を伴う斬撃。", 38, 9, [],
     "崩斧", "体勢回復を停止する。", 75, 24, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_HAMMER", "槌術", "HAMMER",
     "鈍器の衝撃伝達を学ぶ基礎。戦槌・メイス・地響き流派へ分岐する。",
     "打撃", "基本の槌打ち。", 24, 0,
     "叩き", "頭部を狙う打撃。", 40, 8, [],
     "震脚", "体勢回復を遅延させる地打ち。", 62, 18, [("Debuff_PoiseRegen", 0.2, 5.0)],
     "振り", "大振りの一撃。", 44, 10, [],
     "鉄槌", "体勢を大きく削る。", 88, 26, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)]),
    ("SKILL_COMBAT_FIST", "拳闘術", "FIST",
     "素手の打撃理論の基礎。体術・蹴り・投げの特化へ広がる。",
     "直拳", "基本のストレート。", 14, 0,
     "勾拳", "体勢を揺らす打撃。", 26, 4, [],
     "崩拳", "体勢回復を低下させる。", 44, 10, [("Debuff_PoiseRegen", 0.2, 4.0)],
     "蹴り", "距離を取る一撃。", 30, 6, [],
     "鉄山靠", "体勢回復を停止する近身打。", 60, 16, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_KATANA", "刀術", "KATANA",
     "片手刀の抜刀と斬りの基礎。居合・二刀・脇差流派へ進む。",
     "斬り", "基本の袈裟斬り。", 16, 0,
     "居合", "踏み込みの一斬。", 32, 6, [],
     "流刃", "体勢回復を遅延させる連斬。", 50, 14, [("Debuff_PoiseRegen", 0.18, 5.0)],
     "上段", "大きく振り上げる斬撃。", 38, 8, [],
     "断水", "体勢を止める一閃。", 68, 22, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)]),
    ("SKILL_COMBAT_SCYTHE", "大鎌術", "SCYTHE",
     "長柄鎌の軌道制御の基礎。収穫斬・回転鎌・死神流派へ派生する。",
     "薙ぎ", "基本の横薙ぎ。", 20, 0,
     "引き", "引き寄せる斬撃。", 34, 6, [],
     "断頭", "体勢回復を大幅低下。", 56, 16, [("Debuff_PoiseRegen", 0.24, 5.0)],
     "旋刃", "回転を伴う一撃。", 42, 10, [],
     "死鎌", "装甲溶解予兆を付与。", 78, 24, [("Debuff_ArmorDissolve", 0.15, 6.0)]),
    ("SKILL_COMBAT_WHIP", "鞭術", "WHIP",
     "軟武器の軌道と距離の基礎。捕縛・多段・雷鞭流派へ展開する。",
     "鞭打", "基本の一撃。", 10, 0,
     "絡み", "相手を引き寄せる。", 24, 5, [],
     "連打", "体勢回復を遅延させる多段。", 40, 12, [("Debuff_PoiseRegen", 0.16, 4.0)],
     "振り", "広範囲の払い。", 28, 7, [],
     "雷鞭", "体勢回復を停止する。", 52, 18, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_STAFF", "棍術", "STAFF",
     "棒術の攻防距離の基礎。長棍・三節棍・破魔棍へ分岐する。",
     "打", "基本の棒打ち。", 16, 0,
     "突き", "棒先の突き。", 28, 4, [],
     "乱打", "体勢回復を低下させる連打。", 46, 12, [("Debuff_PoiseRegen", 0.17, 5.0)],
     "払い", "武器を弾く一撃。", 32, 6, [],
     "破魔打", "体勢を大きく崩す。", 64, 20, [("Debuff_PoiseRecoveryHalt", 1.0, 2.0)]),
    ("SKILL_COMBAT_SHIELD", "盾術", "SHIELD",
     "盾の防御と体当たりの基礎。要塞・反撃・聖盾流派へ進化する。",
     "盾撃", "基本の盾殴り。", 20, 0,
     "体当たり", "前進する衝突。", 32, 5, [],
     "要塞", "自身の体勢耐性を高める。", 38, 10, [("Buff_Poise", 0.25, 5.0)],
     "反撃", "受け流し後の打撃。", 42, 8, [],
     "砕盾", "敵体勢回復を停止する。", 70, 18, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_DUAL", "双剣術", "DUAL",
     "二刀の連携の基礎。クロス・旋風・暗双流派へ派生する。",
     "二連斬", "左右交互の斬撃。", 14, 0,
     "交差", "刃を交差させる斬り。", 28, 6, [],
     "旋風", "体勢回復を遅延させる。", 46, 14, [("Debuff_PoiseRegen", 0.19, 4.0)],
     "突進", "双剣での突進斬。", 34, 8, [],
     "無双", "体勢回復を停止する乱舞。", 62, 22, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)]),
    ("SKILL_COMBAT_RAPIER", "細剣術", "RAPIER",
     "細身の刃による刺突の基礎。决斗・刺突・無音流派へ分岐する。",
     "刺突", "基本の一刺し。", 14, 0,
     "踏み込み", "距離を詰める突き。", 30, 5, [],
     "連突", "体勢回復を遅延させる。", 48, 12, [("Debuff_PoiseRegen", 0.2, 4.0)],
     "掠め", "体勢を削る切り返し。", 32, 7, [],
     "決闘刺", "体勢回復を停止する。", 58, 20, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_POLEARM", "長柄術", "POLE",
     "長柄複合武器の基礎。ハルバード・グレイブ・拒馬流派へ展開する。",
     "斬突", "斬りと突きの中間。", 18, 0,
     "引寄", "引き寄せる一撃。", 30, 6, [],
     "穿突", "体勢回復を低下させる突き。", 50, 14, [("Debuff_PoiseRegen", 0.18, 5.0)],
     "叩伏", "柄での打撃。", 36, 8, [],
     "大車", "体勢を大きく崩す。", 74, 22, [("Debuff_PoiseRecoveryHalt", 1.0, 2.0)]),
    ("SKILL_COMBAT_BOW", "弓術", "BOW",
     "弓の射撃理論の基礎。長弓・速射・魔力矢流派へ進む。",
     "射撃", "基本の一射。", 12, 0,
     "狙撃", "精密な一矢。", 26, 6, [],
     "連射", "体勢回復を遅延させる。", 42, 12, [("Debuff_PoiseRegen", 0.14, 4.0)],
     "貫通", "高poiseDamageの矢。", 38, 10, [],
     "魔力矢", "装甲溶解予兆を付与。", 55, 18, [("Debuff_ArmorDissolve", 0.1, 5.0)]),
    ("SKILL_COMBAT_CROSSBOW", "弩術", "XBOW",
     "弩の装填と射撃の基礎。重弩・機関・貫通流派へ派生する。",
     "射撃", "基本の一射。", 14, 0,
     "装填射", "威力を高めた一射。", 30, 8, [],
     "貫矢", "体勢回復を遅延させる。", 48, 14, [("Debuff_PoiseRegen", 0.16, 5.0)],
     "急射", "素早い射撃。", 34, 9, [],
     "重貫", "体勢回復を停止する。", 65, 22, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)]),
    ("SKILL_COMBAT_THROW", "投擲術", "THROW",
     "投擲武器の軌道の基礎。手裏剣・爆弾・連投流派へ分岐する。",
     "投擲", "基本の一投。", 10, 0,
     "回転投", "回転を付けた投擲。", 22, 4, [],
     "急所投", "体勢回復を低下させる。", 38, 10, [("Debuff_PoiseRegen", 0.15, 4.0)],
     "強投", "高威力の投擲。", 32, 8, [],
     "炸裂投", "装甲溶解予兆を付与。", 52, 16, [("Debuff_ArmorDissolve", 0.08, 5.0)]),
    ("SKILL_COMBAT_ASSASSIN", "暗殺術", "ASSN",
     "奇襲と急所の基礎。影歩・毒・無音流派へ進化する。",
     "急所打", "要害を狙う一撃。", 16, 0,
     "背刺", "背後からの攻撃。", 32, 6, [],
     "無音", "体勢回復を停止する。", 50, 16, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)],
     "煙幕", "体勢を削る奇襲。", 28, 8, [],
     "断喉", "体勢回復を大幅低下。", 60, 20, [("Debuff_PoiseRegen", 0.28, 6.0)]),
    ("SKILL_COMBAT_GUN", "銃術", "GUN",
     "銃器の射撃の基礎。速射・狙撃・魔弾流派へ展開する。",
     "射撃", "基本の一発。", 16, 0,
     "精密", "精密射撃。", 30, 8, [],
     "連射", "体勢回復を遅延させる。", 44, 14, [("Debuff_PoiseRegen", 0.15, 4.0)],
     "強装", "高威力射撃。", 40, 12, [],
     "貫弾", "体勢回復を停止する。", 68, 24, [("Debuff_PoiseRecoveryHalt", 1.0, 2.0)]),
    ("SKILL_COMBAT_CANNON", "砲術", "CANNON",
     "大型火器の基礎。曲射・集中砲撃・魔力砲流派へ派生する。",
     "砲撃", "基本の一砲。", 28, 0,
     "曲射", "弾道を曲げる射撃。", 42, 12, [],
     "集中", "体勢回復を低下させる。", 58, 20, [("Debuff_PoiseRegen", 0.2, 5.0)],
     "榴弾", "範囲を想定した一撃。", 48, 14, [],
     "轟砲", "装甲溶解予兆を付与。", 90, 30, [("Debuff_ArmorDissolve", 0.18, 6.0)]),
    ("SKILL_COMBAT_SHIELDBOW", "盾弾術", "SHBOW",
     "盾と射撃の複合基礎。要塞射・移動砲台流派へ分岐する。",
     "盾射", "盾を支えた射撃。", 14, 0,
     "伏射", "低姿勢からの一射。", 28, 6, [],
     "拒射", "体勢回復を遅延させる。", 44, 12, [("Debuff_PoiseRegen", 0.16, 4.0)],
     "突進射", "前進しながらの射撃。", 36, 9, [],
     "要塞弾", "体勢回復を停止する。", 62, 20, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)]),
    ("SKILL_COMBAT_MARTIAL", "格闘術", "MART",
     "総合格闘の基礎。打撃・投技・関節技の流派へ広がる。",
     "打", "基本の打撃。", 14, 0,
     "蹴", "基本の蹴り。", 26, 4, [],
     "投げ", "体勢を大きく崩す。", 48, 12, [("Debuff_PoiseRegen", 0.22, 5.0)],
     "関節", "関節を狙う打撃。", 34, 8, [],
     "鉄掌", "体勢回復を停止する。", 58, 18, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
    ("SKILL_COMBAT_DUALDAGGER", "双短剣術", "DDAG",
     "双短剣の連撃基礎。乱舞・暗双・疾風流派へ派生する。",
     "二連刺", "左右の連続刺突。", 12, 0,
     "交差刺", "刃を交差させる刺突。", 26, 5, [],
     "乱刺", "体勢回復を遅延させる。", 44, 12, [("Debuff_PoiseRegen", 0.2, 4.0)],
     "旋回", "回転を伴う斬刺。", 30, 7, [],
     "影双", "体勢回復を停止する。", 56, 18, [("Debuff_PoiseRecoveryHalt", 1.0, 2.5)]),
    ("SKILL_COMBAT_HIDDEN", "暗器術", "HIDDEN",
     "隠し武器の基礎。手裏剣・毒針・無形流派へ進む。",
     "投針", "基本の一投。", 10, 0,
     "連針", "連続投擲。", 22, 4, [],
     "毒針", "体勢回復を低下させる。", 36, 10, [("Debuff_PoiseRegen", 0.18, 5.0)],
     "急所針", "急所を狙う一撃。", 28, 7, [],
     "無形", "体勢回復を停止する。", 50, 16, [("Debuff_PoiseRecoveryHalt", 1.0, 3.0)]),
]


def effects(arr):
    return [{"effectType": e[0], "value": e[1], "duration": e[2]} for e in arr]


def art(aid, name, desc, poise, mana, effs, unlocked, derivatives):
    return {
        "artId": aid,
        "artName": name,
        "description": desc,
        "poiseDamage": poise,
        "manaCost": mana,
        "unlocked": unlocked,
        "specialEffects": effects(effs),
        "derivatives": derivatives,
    }


def build_skill(row):
    sid, sname, prefix, sdesc, rname, rdesc, rpoise, rmana, \
        b2name, b2desc, b2poise, b2mana, b2eff, \
        l1name, l1desc, l1poise, l1mana, l1eff, \
        b4name, b4desc, b4poise, b4mana, b4eff, \
        l2name, l2desc, l2poise, l2mana, l2eff = row

    leaf1 = art(f"ART_{prefix}_L1", l1name, l1desc + "（技3個目）", l1poise, l1mana, l1eff, False, [])
    branch2 = art(f"ART_{prefix}_B2", b2name, b2desc + "（技2個目）", b2poise, b2mana, b2eff, False, [leaf1])
    leaf2 = art(f"ART_{prefix}_L2", l2name, l2desc + "（技5個目）", l2poise, l2mana, l2eff, False, [])
    branch4 = art(f"ART_{prefix}_B4", b4name, b4desc + "（技4個目）", b4poise, b4mana, b4eff, False, [leaf2])
    root = art(f"ART_{prefix}_ROOT", rname, rdesc + "（技1個目）", rpoise, rmana, [], True, [branch2, branch4])

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
    out = [build_skill(r) for r in SKILLS]
    assert len(out) == 25
    path = __file__.replace("_gen_combat25.py", "SkillMasters_Combat25.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
    # also wrapper for repository
    wrap_path = __file__.replace("_gen_combat25.py", "SkillMasters.json")
    with open(wrap_path, "w", encoding="utf-8") as f:
        json.dump({"skills": [x["skillMaster"] for x in out]}, f, ensure_ascii=False, indent=2)
    print(path)


if __name__ == "__main__":
    main()
