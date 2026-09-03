using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// URP 専用デモプロジェクト向けの Cinemachine エディタ衛生処理。
/// Package Cache（immutable）は変更せず、CM2 互換コードのみ Scripting Define で除外します。
/// </summary>
[InitializeOnLoad]
public static class CinemachineEditorHygiene
{
    private const string Cm2DisableDefine = "CINEMACHINE_NO_CM2_SUPPORT";
    private const string CinemachinePackageId = "com.unity.cinemachine";
    private const string HdrpAsmrefRelativePath =
        "Editor/Samples/ExposeHDRPInternals/HDRP-Editor-ref.asmref";

    private const string SessionAppliedKey = "CinemachineEditorHygiene.Applied";

    static CinemachineEditorHygiene()
    {
        EditorApplication.delayCall += ApplyOncePerSession;
    }

    private static void ApplyOncePerSession()
    {
        if (SessionState.GetBool(SessionAppliedKey, false))
        {
            return;
        }

        bool defineChanged = EnsureCm2SupportDisabledDefine();
        bool asmrefRestored = RestoreAlteredPackageAsmrefIfNeeded();

        if (defineChanged || asmrefRestored)
        {
            if (asmrefRestored)
            {
                Debug.LogWarning(
                    "<color=#FFCC80><b>[CinemachineEditorHygiene]</b></color> " +
                    "以前の処理で変更された Package Cache 内 .asmref を復元しました。" +
                    "immutable パッケージは今後変更しません。");
            }

            if (defineChanged)
            {
                Debug.Log(
                    "<color=#A5D6A7><b>[CinemachineEditorHygiene]</b></color> " +
                    $"{Cm2DisableDefine} を Scripting Define Symbols に追加しました。");
            }

            if (asmrefRestored)
            {
                AssetDatabase.Refresh();
            }
        }

        SessionState.SetBool(SessionAppliedKey, true);
    }

    /// <summary>全ビルドターゲットに CM2 互換コード除外シンボルを付与します。</summary>
    private static bool EnsureCm2SupportDisabledDefine()
    {
        bool changed = false;

        foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
        {
            if (group == BuildTargetGroup.Unknown)
            {
                continue;
            }

            NamedBuildTarget namedTarget;
            try
            {
                namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
            }
            catch (ArgumentException)
            {
                continue;
            }

            PlayerSettings.GetScriptingDefineSymbols(namedTarget, out string[] defines);
            if (Array.IndexOf(defines, Cm2DisableDefine) >= 0)
            {
                continue;
            }

            List<string> merged = new List<string>(defines) { Cm2DisableDefine };
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, merged.ToArray());
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// 旧バージョンの衛生処理で .asmref.disabled にリネームされたファイルを元に戻します。
    /// Unity 6 は immutable パッケージの改変で AssetImportWorker がクラッシュします。
    /// </summary>
    private static bool RestoreAlteredPackageAsmrefIfNeeded()
    {
        string packageRoot = ResolvePackageRoot(CinemachinePackageId);
        if (string.IsNullOrEmpty(packageRoot))
        {
            return false;
        }

        string asmrefPath = Path.Combine(packageRoot, HdrpAsmrefRelativePath);
        string disabledPath = asmrefPath + ".disabled";

        if (!File.Exists(disabledPath))
        {
            return false;
        }

        bool restored = false;
        restored |= TryMoveFile(disabledPath, asmrefPath);
        restored |= TryMoveFile(disabledPath + ".meta", asmrefPath + ".meta");
        return restored;
    }

    private static string ResolvePackageRoot(string packageId)
    {
        UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo
            .FindForAssetPath($"Packages/{packageId}/package.json");
        return info != null ? info.resolvedPath : null;
    }

    private static bool TryMoveFile(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return false;
        }

        File.Move(sourcePath, destinationPath);
        return true;
    }

    [MenuItem("Tools/Demo/Apply Cinemachine Editor Hygiene")]
    private static void ApplyFromMenu()
    {
        SessionState.SetBool(SessionAppliedKey, false);
        ApplyOncePerSession();
    }

    [MenuItem("Tools/Demo/Repair Cinemachine Package Cache (reimport)")]
    private static void RepairPackageCacheFromMenu()
    {
        bool restored = RestoreAlteredPackageAsmrefIfNeeded();
        if (restored)
        {
            AssetDatabase.Refresh();
            Debug.Log(
                "<color=#A5D6A7>[CinemachineEditorHygiene]</color> .asmref を復元しました。" +
                "まだ不安定な場合は Package Manager で Cinemachine を Remove → Add してください。");
            return;
        }

        Debug.Log(
            "<color=#90A4AE>[CinemachineEditorHygiene]</color> 復元対象の .asmref.disabled は見つかりませんでした。" +
            "Package Manager で com.unity.cinemachine を再インストールしてください。");
    }
}
