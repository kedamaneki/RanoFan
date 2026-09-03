using UnityEngine;

/// <summary>
/// Play 開始時にデバッグホットキー一覧を Console へ出力します。
/// </summary>
public static class DebugHotkeyBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void LogHotkeyMap()
    {
        Debug.Log(
            "<color=#FFD54F><b>═══ デバッグホットキー一覧（現行仕様）═══</b></color>\n" +
            "※ Game ビューをクリックしてから押してください（Editor / OS が F キーを奪う場合あり）\n" +
            "<color=#FF8A65><b>【本編デモ DemoTimeLineManager】</b></color>\n" +
            "  Play 開始で自動 <b>BattlePhase → 撃破で CraftingPhase → Enter で ResultPhase</b>\n" +
            "  入力はステート別に統治（戦闘と生産の誤爆なし）\n" +
            "<color=#FF8A65>  BattlePhase</color>\n" +
            "    マウスホイール … 攻撃属性切替（Slash / Thrust / Strike）\n" +
            "    マウス左クリック … 選択中属性で攻撃\n" +
            "    F … パリィ / LeftCtrl … ステップ回避 / T … デバッグ敵攻撃誘発\n" +
            "<color=#4DD0E1>  CraftingPhase</color>\n" +
            "    1〜5 … 職種共通こだわり工程（Forge / Alch 自動分岐）\n" +
            "    鍛冶: 1=精製 2=魔物合金 3=大槌 4=研磨 5=魔力行使\n" +
            "    調合: 1=茎除去 2=丸投入 3=すり潰し 4=沸騰 5=冷水投入\n" +
            "    Enter … 品質ジャッジ + 例外JSON射出 → ResultPhase\n" +
            "  \\ … 町の中判定 ON/OFF（屋外 ⇆ 街の工房安全圏）\n" +
            "<color=#87CEEB><b>【工房実験 CraftingExperimentHub】</b></color>（デモ本編と独立セッション）\n" +
            "  C … 鍛冶 Forge（街）開始/終了 / P … 調合 Alch（街）開始/終了\n" +
            "  / … 屋外（携帯キット）開始/終了\n" +
            "  [ … 介入① 大槌/右攪拌 / L … 介入② 小槌/左攪拌 / , … 介入③ 高度干渉\n" +
            "  . … 設備フラグ反転（hasHeavyTool / hasAdvancedAttachment）\n" +
            "  Y … 例外ルート強制（Param 99/99/1 + 触媒） / ; … 被弾シミュレート\n" +
            "<color=#7DF9FF><b>【重量インベントリ InventoryManager】</b></color>\n" +
            "  R … インベントリ表示（JIS: Shift+- / テンキー= でも可）\n" +
            "  J … 携帯用魔力炉（35kg） / K … 魅了の粉末\n" +
            "  U … グリフォンの骨 / I … 鉄鉱石（即席クラフト素材）\n" +
            "<color=#CE93D8><b>【AI・フェーズ連携】</b></color>\n" +
            "  B … AI 基礎スキル目覚め（Training → Gemini 実通信 → スキル枠付与）\n" +
            "  P … フェーズ評価 → AI パイプライン手動実行（GamePhaseBridgeTester）\n" +
            "  <color=#FFAB91>※ P は工房実験の調合トグルと競合します。用途に応じてどちらか一方を優先してください。</color>\n" +
            "<color=#7DF9FF><b>【歴史座標 ChronosCoordinateHub】</b></color>\n" +
            "  H … ワイプ＆時代進行 / N … 過去ダイブAI（正史⇔改変）\n" +
            "<color=#B39DDB><b>【派生技 PlayerRobot】</b></color>\n" +
            "  F1〜F3 … 派生技習得テスト（攻撃 / 回避 / JSON 経由）\n" +
            "<color=#A5D6A7><b>【UI】</b></color>\n" +
            "  Esc … ゲーム操作 ⇆ UI操作（カメラ / マウスクリック）切替\n" +
            "  Q … 戦歴ギャラリー（CombatMetricsGallery）開閉\n" +
            "  <color=#80DEEA>Shift+N … デイリーシミュ 1日経過 / Shift+G … 村納品テスト（結界回復）</color>\n" +
            "<color=#90A4AE><b>【エディタ専用 Tools → Demo】</b></color>（Play モード中・自動検証は既定 OFF）\n" +
            "  Run Phase Risk Verifications … 3大リスク自動検証\n" +
            "  AI Debug Fuzzer … Hyper-Burst / Accelerated（<b>Shift+F10</b> で開始/停止トグル）\n" +
            "  Auto Fuzzer Tester … 正史・AI・装備還流・ドロップ（<b>Shift+F12</b> で開始/停止トグル）\n" +
            "  Run Inspiration / Mastery / Shop / Status / Chronostasis / Magic Tech / Weapon / Enemy Part … 各種テスター\n" +
            "  Inspiration Debug GUI … Show / Hide\n" +
            "<color=#90A4AE>敵プロファイル切替（スライム/グリフォン）は CombatActionTester の ContextMenu のみ（U/I ホットキーは廃止）</color>");
    }
}
