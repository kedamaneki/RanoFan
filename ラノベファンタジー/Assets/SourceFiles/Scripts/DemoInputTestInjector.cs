using System.Collections.Generic;

/// <summary>
/// DemoTimeLineManager の自動検証用に、1フレーム単位の疑似キー入力を注入します。
/// 通常プレイではキューは空のままなので既存入力経路に影響しません。
/// </summary>
public static class DemoInputTestInjector
{
    private static readonly Queue<int> DigitQueue = new Queue<int>();
    private static int enterPressesRemaining;
    private static int leftClickPressesRemaining;
    private static int stepEvadePressesRemaining;
    private static int parryPressesRemaining;
    private static int scrollTicksRemaining;
    private static float pendingScrollDelta;

    /// <summary>注入キューをクリアします。</summary>
    public static void ClearAll()
    {
        DigitQueue.Clear();
        enterPressesRemaining = 0;
        leftClickPressesRemaining = 0;
        stepEvadePressesRemaining = 0;
        parryPressesRemaining = 0;
        scrollTicksRemaining = 0;
        pendingScrollDelta = 0f;
    }

    /// <summary>次の WasDigitKeyPressed 消費まで数字キー 1〜5 を予約します。</summary>
    public static void QueueDigitKey(int digit1To5, int times = 1)
    {
        int clamped = UnityEngine.Mathf.Clamp(digit1To5, 1, 5);
        int count = UnityEngine.Mathf.Max(1, times);
        for (int i = 0; i < count; i++)
        {
            DigitQueue.Enqueue(clamped);
        }
    }

    /// <summary>Enter キー押下を予約します。</summary>
    public static void QueueEnterKey(int times = 1)
    {
        enterPressesRemaining += UnityEngine.Mathf.Max(1, times);
    }

    /// <summary>左クリック押下を予約します。</summary>
    public static void QueueLeftClick(int times = 1)
    {
        leftClickPressesRemaining += UnityEngine.Mathf.Max(1, times);
    }

    /// <summary>Ctrl ステップ回避押下を予約します。</summary>
    public static void QueueStepEvade(int times = 1)
    {
        stepEvadePressesRemaining += UnityEngine.Mathf.Max(1, times);
    }

    /// <summary>F キー（パリィ）押下を予約します。</summary>
    public static void QueueParry(int times = 1)
    {
        parryPressesRemaining += UnityEngine.Mathf.Max(1, times);
    }

    /// <summary>マウスホイール（属性切替）を予約します。direction: +1=上 / -1=下</summary>
    public static void QueueScroll(int direction, int times = 1)
    {
        int count = UnityEngine.Mathf.Max(1, times);
        scrollTicksRemaining += count;
        pendingScrollDelta = direction >= 0 ? 0.12f : -0.12f;
    }

    /// <summary>スクロール注入を1回消費します。</summary>
    public static bool TryConsumeScroll(out float delta)
    {
        delta = 0f;
        if (scrollTicksRemaining <= 0)
        {
            return false;
        }

        scrollTicksRemaining--;
        delta = pendingScrollDelta;
        return true;
    }

    /// <summary>F キー注入を1回消費します。</summary>
    public static bool TryConsumeParry()
    {
        if (parryPressesRemaining <= 0)
        {
            return false;
        }

        parryPressesRemaining--;
        return true;
    }

    /// <summary>指定スロットの数字キーが注入キューから消費可能か。</summary>
    public static bool TryConsumeDigit(int digit1To5)
    {
        if (DigitQueue.Count == 0 || DigitQueue.Peek() != digit1To5)
        {
            return false;
        }

        DigitQueue.Dequeue();
        return true;
    }

    /// <summary>Enter 注入を1回消費します。</summary>
    public static bool TryConsumeEnter()
    {
        if (enterPressesRemaining <= 0)
        {
            return false;
        }

        enterPressesRemaining--;
        return true;
    }

    /// <summary>左クリック注入を1回消費します。</summary>
    public static bool TryConsumeLeftClick()
    {
        if (leftClickPressesRemaining <= 0)
        {
            return false;
        }

        leftClickPressesRemaining--;
        return true;
    }

    /// <summary>ステップ回避注入を1回消費します。</summary>
    public static bool TryConsumeStepEvade()
    {
        if (stepEvadePressesRemaining <= 0)
        {
            return false;
        }

        stepEvadePressesRemaining--;
        return true;
    }
}
