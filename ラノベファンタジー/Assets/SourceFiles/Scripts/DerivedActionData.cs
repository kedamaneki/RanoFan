using System;
using UnityEngine;

/// <summary>
/// AI（歴史・スキルジェネレーター）から授与される派生技の共通データ構造。
/// JSON シリアライズに対応しており、テキストからランタイムで生成できます。
/// </summary>
[Serializable]
public class DerivedActionData
{
    [Tooltip("技の一意識別子（重複習得防止に使用）")]
    public string actionID;

    [Tooltip("技の表示名（例：神速一閃、無敵ローリング）")]
    public string actionName;

    [Tooltip("攻撃派生か回避派生か")]
    public DerivedActionType actionType;

    [Tooltip("基本 STR に対するダメージ倍率。回避技は 0")]
    public float damageMultiplier = 1f;

    [Tooltip("この技のスタミナ消費量")]
    public float staminaCost = 20f;

    [Tooltip("攻撃：判定 ON 時間（秒）／回避：移動持続時間（秒）")]
    public float activeDetectionTime = 0.3f;

    [Tooltip("無敵時間（秒）。攻撃技は 0、ローリング回避は 0.2 など")]
    public float invincibilityTime;

    [Tooltip("AI がこの技を閃いた理由のフレーバーテキスト")]
    public string unlockTriggerCondition;

    /// <summary>最低限の識別情報があるか</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(actionID);
    }

    /// <summary>深いコピーを作成（リスト内の参照汚染を防ぐ）</summary>
    public DerivedActionData Clone()
    {
        return FromJson(ToJson());
    }

    /// <summary>
    /// AI から送られた JSON 文字列を派生技データに変換します。
    /// フィールド名は C# の public フィールド名と一致させてください。
    /// </summary>
    public static DerivedActionData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("[DerivedActionData] JSON が空です。");
            return null;
        }

        try
        {
            DerivedActionData data = JsonUtility.FromJson<DerivedActionData>(json);
            if (data == null || !data.IsValid())
            {
                Debug.LogWarning("[DerivedActionData] JSON のパース結果が無効です。");
                return null;
            }

            return data;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[DerivedActionData] JSON パースに失敗しました: {exception.Message}");
            return null;
        }
    }

    /// <summary>ゲーム内データを JSON 文字列へ変換（ログ保存・AI 送信用）</summary>
    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    /// <summary>デバッグ・テスト用のサンプル攻撃派生技</summary>
    public static DerivedActionData CreateSampleAttack()
    {
        return new DerivedActionData
        {
            actionID = "attack_divine_flash",
            actionName = "神速一閃",
            actionType = DerivedActionType.Attack,
            damageMultiplier = 1.5f,
            staminaCost = 25f,
            activeDetectionTime = 0.35f,
            invincibilityTime = 0f,
            unlockTriggerCondition = "連続攻撃での高命中率を記録した結果、剣筋が研ぎ澄まされた"
        };
    }

    /// <summary>デバッグ・テスト用のサンプル回避派生技</summary>
    public static DerivedActionData CreateSampleEvade()
    {
        return new DerivedActionData
        {
            actionID = "evade_invincible_roll",
            actionName = "無敵ローリング",
            actionType = DerivedActionType.Evade,
            damageMultiplier = 0f,
            staminaCost = 30f,
            activeDetectionTime = 0.4f,
            invincibilityTime = 0.2f,
            unlockTriggerCondition = "HP10%以下の極限状態での生存"
        };
    }

    public override string ToString()
    {
        return $"[{actionID}] {actionName} ({actionType})";
    }
}
