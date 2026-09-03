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
    ("SKILL_PROD_BLACKSMITH", "鍛冶", "BLSM",
     "金属や結晶を叩き、武具を創り出す総合職人スキル。極めることで高純度な結晶装甲鍛造へと派生する。",
     "叩き打ち", "基本の鍛造スレッジ打ち。", 0,
     "不純物焼鉄", "火力を調整し、結晶のPurityを高める。", 5, [("Craft_PurityBonus", 1.1, 0.0)],
     "名匠の一打", "完成品のQualityに最終固定値+5を叩き込む。", 15, [("Craft_PurityFlatBonus", 5.0, 0.0)],
     "金属目利き", "結晶素材の性質を凝視する。", 4, [],
     "秘伝書記録", "最高純度の結晶変動幅をレシピに焼き付け、UIにガイドラインを表示する。", 20, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_ALCHEMY", "錬金術", "ALCH",
     "素材を変質させる基礎錬金。極めることで賢者の石や魔力触媒の合成へ派生する。",
     "基礎変質", "素材の性質をわずかに変える。", 3,
     "抽出強化", "ExtractionLevel操作を高める。", 8, [("Craft_ExtractionBonus", 1.15, 0.0)],
     "深層抽出", "抽出倍率をさらに引き上げる。", 14, [("Craft_ExtractionBonus", 1.25, 0.0)],
     "溶解調律", "DissolutionRateを安定させる。", 6, [("Craft_DissolutionRate", 1.1, 0.0)],
     "賢者の記録", "最高品質の変動幅をレシピに刻む。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_ARTIFACT", "魔道具作成", "ARTF",
     "魔力を宿す道具を組み立てる基礎。極めることで伝説級の魔導具製作へ派生する。",
     "基礎組立", "魔道具の骨格を組み立てる。", 4,
     "純度注入", "結晶純度を高めて組み込む。", 10, [("Craft_PurityBonus", 1.12, 0.0)],
     "品質仕上げ", "完成品Qualityに固定値を加算。", 16, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "触媒調合", "溶解率を最適化する。", 8, [("Craft_DissolutionRate", 1.12, 0.0)],
     "設計図記録", "最高品質パラメータを設計図に記録。", 22, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_BLEND", "調合", "BLND",
     "液体・粉末を混ぜ合わせる基礎。極めることで複合魔力薬の精密調合へ派生する。",
     "基礎混合", "素材を均等に混ぜる。", 2,
     "抽出調律", "ExtractionLevelを高める。", 7, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "極限抽出", "抽出倍率を最大化する。", 13, [("Craft_ExtractionBonus", 1.22, 0.0)],
     "溶解加速", "DissolutionRateを高める。", 5, [("Craft_DissolutionRate", 1.15, 0.0)],
     "調合記録", "最高品質の調合幅を記録する。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_WOODWORK", "木工", "WOOD",
     "木材を加工する基礎。極めることで魔力導線付き家具・弓材製作へ派生する。",
     "基礎削り", "木材を安全に削る。", 0,
     "乾燥調律", "木材純度を高める。", 4, [("Craft_PurityBonus", 1.08, 0.0)],
     "匠の仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "目利き", "木目と魔力適性を見極める。", 3, [],
     "工法記録", "最高品質の加工幅を記録する。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_FINEWORK", "細工", "FINE",
     "精密部品を作る基礎。極めることで機械式魔導装置の組み立てへ派生する。",
     "基礎細工", "小さな部品を削り出す。", 2,
     "精密抽出", "微細素材の抽出を高める。", 6, [("Craft_ExtractionBonus", 1.1, 0.0)],
     "極細加工", "完成品Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "溶解微調", "溶解率を精密に調整。", 5, [("Craft_DissolutionRate", 1.1, 0.0)],
     "図面記録", "最高精度の変動幅を記録。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_LEATHER", "革細工", "LTHR",
     "革を裁断・加工する基礎。極めることで魔獣皮の防具鍛錬へ派生する。",
     "基礎裁断", "革を均一に裁断する。", 0,
     "鞣し調律", "革の純度を高める。", 5, [("Craft_PurityBonus", 1.1, 0.0)],
     "上質仕上げ", "完成品Qualityに固定値を加算。", 11, [("Craft_PurityFlatBonus", 3.5, 0.0)],
     "皮目鑑定", "革の魔力適性を見極める。", 3, [],
     "裁断記録", "最高品質の裁断幅を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_TAILOR", "裁縫", "TLOR",
     "布と糸を縫い合わせる基礎。極めることで魔力織りの衣装製作へ派生する。",
     "基礎縫製", "布を安全に縫い合わせる。", 1,
     "糸調律", "繊維純度を高める。", 5, [("Craft_PurityBonus", 1.09, 0.0)],
     "名縫い仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "織目確認", "魔力織りの適性を見る。", 3, [],
     "型紙記録", "最高品質の縫製幅を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_PHARMA", "製薬", "PHRM",
     "薬草から薬を作る基礎。極めることで即効魔力回復薬の製造へ派生する。",
     "基礎調合", "薬液を安全に混ぜる。", 3,
     "成分抽出", "有効成分の抽出を高める。", 8, [("Craft_ExtractionBonus", 1.18, 0.0)],
     "高純度精製", "薬液純度を高める。", 14, [("Craft_PurityBonus", 1.15, 0.0)],
     "溶解安定", "DissolutionRateを安定させる。", 6, [("Craft_DissolutionRate", 1.12, 0.0)],
     "処方記録", "最高品質の処方幅を記録。", 19, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_COOKING", "料理", "COOK",
     "食材を調理する基礎。極めることで戦闘直前の魔力補給料理へ派生する。",
     "基礎調理", "食材を安全に火を通す。", 1,
     "旨味抽出", "栄養・魔力成分の抽出を高める。", 5, [("Craft_ExtractionBonus", 1.1, 0.0)],
     "極上仕上げ", "完成品Qualityに固定値を加算。", 10, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "火加減調律", "溶解率を最適化する。", 4, [("Craft_DissolutionRate", 1.08, 0.0)],
     "献立記録", "最高品質の調理幅を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_FARMING", "農業", "FARM",
     "作物を育てる基礎。極めることで魔力結晶作物の大規模栽培へ派生する。",
     "基礎耕作", "土を耕し種をまく。", 0,
     "収穫目利き", "収穫物の発見率を高める。", 3, [("Hobby_GatherLuck", 1.1, 0.0)],
     "豊穣の恵み", "希少素材の発見率を高める。", 8, [("Hobby_GatherLuck", 1.25, 0.0)],
     "土壌調査", "土壌の魔力適性を見極める。", 2, [],
     "作付記録", "最高収穫の変動幅を記録。", 12, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_HYDRO", "水耕栽培", "HYDR",
     "水と養分で作物を育てる基礎。極めることで地下施設の完全自給農場へ派生する。",
     "基礎水耕", "養液で作物を育てる。", 2,
     "養分抽出", "養分抽出効率を高める。", 6, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "高純度収穫", "収穫物の発見率を高める。", 10, [("Hobby_GatherLuck", 1.2, 0.0)],
     "液温調律", "溶解率を安定させる。", 5, [("Craft_DissolutionRate", 1.1, 0.0)],
     "栽培記録", "最高収穫パラメータを記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_BREW", "醸造", "BREW",
     "発酵させて飲料を作る基礎。極めることで魔力活性化の霊酒製造へ派生する。",
     "基礎醸造", "安全な発酵を始める。", 2,
     "発酵抽出", "有効成分の抽出を高める。", 7, [("Craft_ExtractionBonus", 1.14, 0.0)],
     "熟成仕上げ", "完成品Qualityに固定値を加算。", 13, [("Craft_PurityFlatBonus", 3.5, 0.0)],
     "酵母調律", "溶解率を最適化する。", 5, [("Craft_DissolutionRate", 1.12, 0.0)],
     "醸造記録", "最高品質の醸造幅を記録。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_STONE", "石工", "STON",
     "石材を加工する基礎。極めることで魔力結晶の建材・要塞築造へ派生する。",
     "基礎切石", "石材を安全に切り出す。", 0,
     "石目調律", "石材純度を高める。", 5, [("Craft_PurityBonus", 1.1, 0.0)],
     "名石仕上げ", "完成品Qualityに固定値を加算。", 12, [("Craft_PurityFlatBonus", 4.0, 0.0)],
     "地質確認", "石材の魔力適性を見極める。", 3, [],
     "築造記録", "最高品質の加工幅を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_REPAIR", "修繕", "REPR",
     "壊れた道具を直す基礎。極めることで伝説武具の完全再生へ派生する。",
     "基礎修理", "道具を安全に補修する。", 1,
     "純度復元", "素材純度を回復させる。", 6, [("Craft_PurityBonus", 1.1, 0.0)],
     "匠の復元", "修繕後Qualityに固定値を加算。", 14, [("Craft_PurityFlatBonus", 5.0, 0.0)],
     "損傷診断", "損傷箇所の性質を見極める。", 3, [],
     "修繕記録", "最高修繕品質の変動幅を記録。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_MINING", "採掘", "MINE",
     "鉱脈から資源を掘り出す基礎。極めることで深層結晶脈の大規模採掘へ派生する。",
     "基礎掘削", "安全に鉱石を掘る。", 0,
     "鉱脈目利き", "ドロップ率を高める。", 3, [("Hobby_GatherLuck", 1.15, 0.0)],
     "深層発見", "希少鉱石の発見率を高める。", 8, [("Hobby_GatherLuck", 1.3, 0.0)],
     "岩盤確認", "鉱脈の性質を見極める。", 2, [],
     "採掘記録", "最高採掘結果の変動幅を記録。", 12, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_GATHER", "採取", "GATH",
     "野外から素材を集める基礎。極めることで希少薬草の自動発見へ派生する。",
     "基礎採取", "安全に素材を集める。", 0,
     "目利き採取", "ドロップ率を高める。", 2, [("Hobby_GatherLuck", 1.12, 0.0)],
     "希少発見", "希少素材の発見率を高める。", 6, [("Hobby_GatherLuck", 1.28, 0.0)],
     "生態確認", "素材の魔力適性を見る。", 1, [],
     "採取記録", "最高採取結果を記録。", 10, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_LOGGING", "伐採", "LOGG",
     "樹木を切り倒す基礎。極めることで古樹・魔力木の選伐へ派生する。",
     "基礎伐採", "安全に木を切り倒す。", 0,
     "木目目利き", "木材ドロップ率を高める。", 2, [("Hobby_GatherLuck", 1.1, 0.0)],
     "古木発見", "希少木材の発見率を高める。", 7, [("Hobby_GatherLuck", 1.25, 0.0)],
     "樹勢確認", "木の魔力適性を見極める。", 1, [],
     "伐採記録", "最高伐採結果を記録。", 11, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_DISMANTLE", "解体", "DSMT",
     "構造物を分解する基礎。極めることで遺跡からの資材回収へ派生する。",
     "基礎解体", "安全に構造物を分解する。", 1,
     "部材回収", "回収ドロップ率を高める。", 4, [("Hobby_GatherLuck", 1.12, 0.0)],
     "希少部材", "希少部品の発見率を高める。", 9, [("Hobby_GatherLuck", 1.22, 0.0)],
     "構造解析", "構造の性質を見極める。", 3, [],
     "解体記録", "最高回収結果を記録。", 13, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_SKINNING", "剥ぎ取り", "SKIN",
     "魔獣から素材を剥ぎ取る基礎。極めることで高級魔獣皮の完全回収へ派生する。",
     "基礎剥取", "安全に素材を剥ぎ取る。", 1,
     "精密剥取", "ドロップ率を高める。", 4, [("Hobby_GatherLuck", 1.14, 0.0)],
     "完全剥取", "希少素材の発見率を高める。", 8, [("Hobby_GatherLuck", 1.26, 0.0)],
     "部位確認", "剥取部位の適性を見る。", 2, [],
     "剥取記録", "最高剥取結果を記録。", 12, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_DECURSE", "解呪", "DCUR",
     "呪いを解く基礎。極めることで古代呪物の安全な解体・回収へ派生する。",
     "基礎解呪", "軽い呪いを解く。", 5,
     "呪力抽出", "呪力成分の抽出を高める。", 10, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "完全浄化", "溶解率を高め呪いを溶かす。", 16, [("Craft_DissolutionRate", 1.18, 0.0)],
     "呪紋解析", "呪いの性質を見極める。", 6, [],
     "解呪記録", "最高浄化結果を記録。", 20, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_TRAP", "罠解除", "TRAP",
     "罠を無効化する基礎。極めることで古代遺跡の安全開拓へ派生する。",
     "基礎解除", "単純な罠を外す。", 2,
     "機構解析", "罠部品の回収率を高める。", 5, [("Hobby_GatherLuck", 1.1, 0.0)],
     "完全解除", "希少部品の発見率を高める。", 10, [("Hobby_GatherLuck", 1.2, 0.0)],
     "罠目利き", "罠の性質を見極める。", 3, [],
     "解除記録", "最高解除結果を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_CAMP", "拠点設営", "CAMP",
     "野営地を構築する基礎。極めることで永久拠点・工房施設の建設へ派生する。",
     "基礎設営", "簡易野営地を張る。", 3,
     "資材効率", "設営資材の回収率を高める。", 6, [("Hobby_GatherLuck", 1.08, 0.0)],
     "恒久構築", "設営品質に固定値を加算。", 12, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "地形調査", "設営地の適性を見極める。", 4, [],
     "設営記録", "最高設営結果を記録。", 16, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_APPRAISE", "鑑定", "APRS",
     "アイテムの価値を見極める基礎。極めることで未鑑定伝説品の完全解析へ派生する。",
     "基礎鑑定", "アイテムの概略を見る。", 2,
     "精密鑑定", "素材純度を正確に読む。", 7, [("Craft_PurityBonus", 1.1, 0.0)],
     "真価発見", "希少属性の発見率を高める。", 12, [("Hobby_GatherLuck", 1.15, 0.0)],
     "成分解析", "溶解適性を見極める。", 5, [("Craft_DissolutionRate", 1.1, 0.0)],
     "鑑定記録", "最高鑑定結果を記録。", 18, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_LOGISTICS", "資材管理", "LOGI",
     "資材を整理・保管する基礎。極めることで大規模ギルド倉庫の運用へ派生する。",
     "基礎整理", "資材を分類して保管する。", 0,
     "品質維持", "保管中の純度を維持する。", 4, [("Craft_PurityBonus", 1.05, 0.0)],
     "最適保管", "保管品質に固定値を加算。", 10, [("Craft_PurityFlatBonus", 2.0, 0.0)],
     "在庫目利き", "希少資材の発見率を高める。", 3, [("Hobby_GatherLuck", 1.1, 0.0)],
     "管理記録", "最高保管結果を記録。", 14, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_HAUL", "運搬", "HAUL",
     "重い資材を運ぶ基礎。極めることで大陸横断の物流網構築へ派生する。",
     "基礎運搬", "安全に荷物を運ぶ。", 0,
     "荷崩れ防止", "運搬中の品質を維持する。", 3, [("Craft_PurityBonus", 1.05, 0.0)],
     "完全運搬", "到着品質に固定値を加算。", 8, [("Craft_PurityFlatBonus", 2.5, 0.0)],
     "経路目利き", "道中の素材発見率を高める。", 2, [("Hobby_GatherLuck", 1.08, 0.0)],
     "運搬記録", "最高運搬結果を記録。", 12, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_LIGHT", "光源確保", "LITE",
     "暗所で光を確保する基礎。極めることで深層ダンジョンの永久照明へ派生する。",
     "基礎点灯", "簡易光源を確保する。", 2,
     "魔力灯調律", "光源の純度を高める。", 5, [("Craft_PurityBonus", 1.08, 0.0)],
     "恒久照明", "照明品質に固定値を加算。", 11, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "暗所探索", "暗所での発見率を高める。", 4, [("Hobby_GatherLuck", 1.12, 0.0)],
     "照明記録", "最高照明結果を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_CRISIS", "危機回避", "CRIS",
     "危険を察知して回避する基礎。極めることで災害級ダンジョンの安全踏破へ派生する。",
     "基礎警戒", "周囲の危険を警戒する。", 1,
     "危険感知", "危険地の素材発見率を高める。", 4, [("Hobby_GatherLuck", 1.1, 0.0)],
     "完全回避", "希少安全ルートの発見率を高める。", 9, [("Hobby_GatherLuck", 1.22, 0.0)],
     "状況解析", "危険の性質を見極める。", 3, [],
     "踏破記録", "最高踏破結果を記録。", 13, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_ADAPT", "環境適応", "ADPT",
     "過酷な環境に順応する基礎。極めることで極寒・灼熱帯の長期滞在へ派生する。",
     "基礎適応", "環境に体を慣らす。", 2,
     "環境調律", "環境素材の抽出を高める。", 6, [("Craft_ExtractionBonus", 1.1, 0.0)],
     "極限適応", "希少環境素材の発見率を高める。", 11, [("Hobby_GatherLuck", 1.2, 0.0)],
     "気候解析", "環境の魔力適性を見る。", 4, [],
     "適応記録", "最高適応結果を記録。", 15, [("Craft_RecipeRecord", 1.0, 0.0)]),
    ("SKILL_PROD_FIRSTAID", "応急処置", "FAID",
     "負傷者を手当てする基礎。極めることで戦場医療・魔力再生処置へ派生する。",
     "基礎手当", "軽傷を手当てする。", 3,
     "薬草適用", "応急薬の抽出を高める。", 7, [("Craft_ExtractionBonus", 1.12, 0.0)],
     "緊急処置", "処置品質に固定値を加算。", 13, [("Craft_PurityFlatBonus", 3.0, 0.0)],
     "傷勢確認", "傷の性質を見極める。", 4, [],
     "処置記録", "最高処置結果を記録。", 17, [("Craft_RecipeRecord", 1.0, 0.0)]),
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

    # blacksmith example uses HAMMER not ROOT for first skill
    root_suffix = "HAMMER" if prefix == "BLSM" else "ROOT"
    leaf1 = mk_art(f"ART_{prefix}_L1", l1name, l1desc, l1mana, l1eff, False, [], 3)
    branch2 = mk_art(f"ART_{prefix}_B2", b2name, b2desc, b2mana, b2eff, False, [leaf1], 2)
    leaf2 = mk_art(f"ART_{prefix}_L2", l2name, l2desc, l2mana, l2eff, False, [], 5)
    branch4 = mk_art(f"ART_{prefix}_B4", b4name, b4desc, b4mana, b4eff, False, [leaf2], 4)
    root = mk_art(f"ART_{prefix}_{root_suffix}", rname, rdesc, rmana, [], True, [branch2, branch4], 1)

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
    assert len(out) == 30
    for x in out:
        c = count_arts(x["skillMaster"]["baseArts"])
        assert c == 5, f"{x['skillMaster']['skillName']}: {c}"

    base = os.path.dirname(os.path.abspath(__file__))
    out_path = os.path.join(base, "SkillMasters_Production30.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    master_path = os.path.join(base, "SkillMasters.json")
    existing = []
    if os.path.exists(master_path):
        data = json.load(open(master_path, encoding="utf-8"))
        existing = data.get("skills", [])

    prod_ids = {x["skillMaster"]["skillId"] for x in out}
    merged = [s for s in existing if s.get("skillId") not in prod_ids]
    merged.extend([x["skillMaster"] for x in out])
    with open(master_path, "w", encoding="utf-8") as f:
        json.dump({"skills": merged}, f, ensure_ascii=False, indent=2)

    catalog_path = os.path.join(base, "SkillMasters_Catalog.json")
    catalog = json.load(open(catalog_path, encoding="utf-8")) if os.path.exists(catalog_path) else []
    cat_ids = {c["skillId"] for c in catalog}
    for x in out:
        sm = x["skillMaster"]
        if sm["skillId"] not in cat_ids:
            catalog.append({"skillId": sm["skillId"], "skillName": sm["skillName"], "category": "Production"})
    with open(catalog_path, "w", encoding="utf-8") as f:
        json.dump(catalog, f, ensure_ascii=False, indent=2)

    print(out_path, len(out))


if __name__ == "__main__":
    main()
