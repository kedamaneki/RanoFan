using UnityEditor;
using UnityEngine;

/// <summary>SkillMaster 量産プロンプトを Editor から取得するメニュー。</summary>
public static class SkillMasterPromptBuilderMenu
{
    [MenuItem("Tools/Skill Master/量産プロンプトをログ出力")]
    private static void LogMassProductionPrompt()
    {
        string prompt = SkillMasterPromptBuilder.BuildMassProductionPrompt();
        Debug.Log(prompt);
    }

    [MenuItem("Tools/Skill Master/量産プロンプトをクリップボードにコピー")]
    private static void CopyMassProductionPrompt()
    {
        string prompt = SkillMasterPromptBuilder.BuildMassProductionPrompt();
        EditorGUIUtility.systemCopyBuffer = prompt;
        Debug.Log($"SkillMaster 量産プロンプトをクリップボードにコピーしました（{prompt.Length} 文字）。");
    }
}
