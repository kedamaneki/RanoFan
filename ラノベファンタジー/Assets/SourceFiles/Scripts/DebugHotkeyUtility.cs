using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// デバッグ用ホットキー入力の共通処理。
/// Game ビューにフォーカスがあるときのみ Keyboard.current が有効になります。
/// </summary>
public static class DebugHotkeyUtility
{
    private static bool loggedNoKeyboard;
    private static readonly HashSet<int> LoggedInvalidKeys = new HashSet<int>();

    /// <summary>
    /// 現在のキーボードデバイスを取得します。
    /// </summary>
    public static bool TryGetKeyboard(out Keyboard keyboard)
    {
        keyboard = Keyboard.current;
        if (keyboard != null)
        {
            loggedNoKeyboard = false;
            return true;
        }

        if (!loggedNoKeyboard)
        {
            loggedNoKeyboard = true;
            Debug.LogWarning(
                "[DebugHotkey] Keyboard.current が null です。" +
                "Game ビューをクリックしてフォーカスを移してからキーを押してください。");
        }

        return false;
    }

    /// <summary>
    /// 指定キーがこのフレームで押されたかを判定します。
    /// </summary>
    public static bool WasPressed(Key key)
    {
        if (key == Key.None || !TryGetKeyboard(out Keyboard keyboard))
        {
            return false;
        }

        KeyControl control = keyboard[key];
        if (control == null)
        {
            if (LoggedInvalidKeys.Add((int)key))
            {
                Debug.LogWarning(
                    $"[DebugHotkey] キー {key} に対応する KeyControl がありません。Inspector でキーを選び直してください。");
            }

            return false;
        }

        return control.wasPressedThisFrame;
    }

    /// <summary>
    /// プライマリまたはセカンダリのいずれかが押されたかを判定します（OS 奪取対策用）。
    /// </summary>
    public static bool WasPressed(Key primary, Key secondary)
    {
        return WasPressed(primary) || WasPressed(secondary);
    }

    /// <summary>複数キーのいずれかが押されたかを判定します。</summary>
    public static bool WasPressedAny(params Key[] keys)
    {
        if (keys == null || keys.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < keys.Length; i++)
        {
            if (WasPressed(keys[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// US / JIS キーボード双方で「=」入力に相当する押下を判定します。
    /// JIS では Shift + 「-」物理キーが「=」になります。
    /// </summary>
    public static bool WasEqualsLikePressed()
    {
        if (WasPressed(Key.Equals) || WasPressed(Key.NumpadEquals))
        {
            return true;
        }

        if (!TryGetKeyboard(out Keyboard keyboard))
        {
            return false;
        }

        KeyControl minus = keyboard[Key.Minus];
        return keyboard.shiftKey.isPressed &&
               minus != null &&
               minus.wasPressedThisFrame;
    }
}
