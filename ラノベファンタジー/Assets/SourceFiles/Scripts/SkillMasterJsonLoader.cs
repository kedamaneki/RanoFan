using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =============================================================================
// SkillMaster JSON — JsonUtility 深度制限（10）回避ローダー
// derivatives を除去した浅い DTO で 1 ノードずつ FromJson し、
// 子配列は文字列抽出＋再帰で木構造を手動復元します。
// =============================================================================

/// <summary>深い ArtsDataDto ツリーを含む SkillMaster JSON の安全な読み込み。</summary>
public static class SkillMasterJsonLoader
{
    /// <summary>
    /// GameMaster / SkillMasters 形式（"skills": [ {...}, ... ]）から SkillMaster 一覧を構築します。
    /// </summary>
    public static List<SkillMaster> ParseSkillsFromWrappedListJson(string json)
    {
        List<SkillMaster> result = new List<SkillMaster>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        List<string> skillObjectJsonList = ExtractTopLevelArrayObjectStrings(json, "skills");
        if (skillObjectJsonList.Count == 0)
        {
            SkillMaster single = TryParseSingleSkillObject(json);
            if (single != null)
            {
                result.Add(single);
            }

            return result;
        }

        for (int i = 0; i < skillObjectJsonList.Count; i++)
        {
            SkillMaster master = TryParseSingleSkillObject(skillObjectJsonList[i]);
            if (master != null && master.IsValid())
            {
                result.Add(master);
            }
        }

        return result;
    }

    /// <summary>単一スキル JSON を深度安全な分割パースで SkillMaster 化します。</summary>
    public static SkillMaster TryParseSingleSkillObject(string skillObjectJson)
    {
        if (string.IsNullOrWhiteSpace(skillObjectJson))
        {
            return null;
        }

        string trimmed = skillObjectJson.Trim();
        try
        {
            if (TryExtractRootObjectPropertyValue(trimmed, "skillMaster", out string envelopeSkillJson))
            {
                SkillMaster fromEnvelope = ParseSkillMasterDeep(envelopeSkillJson);
                if (fromEnvelope != null && fromEnvelope.IsValid())
                {
                    return fromEnvelope;
                }
            }

            return ParseSkillMasterDeep(trimmed);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SkillMasterJsonLoader] スキルオブジェクトのパース失敗: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// baseArts を手動再帰パースし、任意深度の derivatives ツリーを復元します。
    /// </summary>
    public static SkillMaster ParseSkillMasterDeep(string skillJson)
    {
        if (string.IsNullOrWhiteSpace(skillJson))
        {
            return null;
        }

        string trimmed = skillJson.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return null;
        }

        List<string> rootArtJsonList = ExtractTopLevelArrayObjectStrings(trimmed, "baseArts");
        string headerJson = StripRootJsonProperty(trimmed, "baseArts");
        SkillMasterShallowDto header = JsonUtility.FromJson<SkillMasterShallowDto>(headerJson);
        if (header == null || !header.IsValid())
        {
            return null;
        }

        SkillMaster master = header.ToRuntimeShell();
        for (int i = 0; i < rootArtJsonList.Count; i++)
        {
            ArtsData art = ParseArtsDataDeep(rootArtJsonList[i]);
            if (art != null)
            {
                master.baseArts.Add(art);
            }
        }

        return master;
    }

    /// <summary>
    /// derivatives 配列を子 JSON ごとに再帰パースし、ArtsData 1 ノードを復元します。
    /// </summary>
    public static ArtsData ParseArtsDataDeep(string artJson)
    {
        if (string.IsNullOrWhiteSpace(artJson))
        {
            return null;
        }

        string trimmed = artJson.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return null;
        }

        // 子 derivatives を先に文字列として切り出し（深さは JsonUtility に渡さない）
        List<string> childJsonList = ExtractTopLevelArrayObjectStrings(trimmed, "derivatives");
        string shallowJson = StripRootJsonProperty(trimmed, "derivatives");
        ArtsDataShallowDto shallow = JsonUtility.FromJson<ArtsDataShallowDto>(shallowJson);
        if (shallow == null)
        {
            return null;
        }

        ArtsData runtime = shallow.ToRuntime();
        for (int i = 0; i < childJsonList.Count; i++)
        {
            ArtsData child = ParseArtsDataDeep(childJsonList[i]);
            if (child != null)
            {
                runtime.derivatives.Add(child);
            }
        }

        return runtime;
    }

    /// <summary>JSON 内の指定キーに対応する配列から、トップレベルオブジェクト文字列を抽出します。</summary>
    public static List<string> ExtractTopLevelArrayObjectStrings(string json, string arrayKey)
    {
        List<string> objects = new List<string>();
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(arrayKey))
        {
            return objects;
        }

        int arrayStart = FindJsonArrayStartAfterKey(json, arrayKey);
        if (arrayStart < 0)
        {
            return objects;
        }

        int index = arrayStart + 1;
        while (index < json.Length)
        {
            index = SkipJsonWhitespace(json, index);
            if (index >= json.Length)
            {
                break;
            }

            char ch = json[index];
            if (ch == ']')
            {
                break;
            }

            if (ch == ',')
            {
                index++;
                continue;
            }

            if (ch != '{')
            {
                index++;
                continue;
            }

            if (!TryReadJsonObjectRange(json, index, out int objectEndExclusive))
            {
                break;
            }

            int length = objectEndExclusive - index;
            if (length > 0)
            {
                objects.Add(json.Substring(index, length));
            }

            index = objectEndExclusive;
        }

        return objects;
    }

    /// <summary>ルートオブジェクトから指定プロパティ（配列/オブジェクト）を除去した JSON を返します。</summary>
    public static string StripRootJsonProperty(string objectJson, string propertyKey)
    {
        if (string.IsNullOrWhiteSpace(objectJson) || string.IsNullOrWhiteSpace(propertyKey))
        {
            return objectJson;
        }

        if (!TryFindRootPropertyRange(objectJson, propertyKey, out int removeStart, out int removeEndExclusive))
        {
            return objectJson;
        }

        StringBuilder sb = new StringBuilder(objectJson.Length);
        if (removeStart > 0)
        {
            sb.Append(objectJson, 0, removeStart);
        }

        if (removeEndExclusive < objectJson.Length)
        {
            sb.Append(objectJson, removeEndExclusive, objectJson.Length - removeEndExclusive);
        }

        return CleanupJsonCommas(sb.ToString());
    }

    private static bool TryExtractRootObjectPropertyValue(
        string objectJson,
        string propertyKey,
        out string propertyObjectJson)
    {
        propertyObjectJson = null;
        if (!TryFindRootPropertyValueRange(objectJson, propertyKey, out int valueStart, out int valueEndExclusive))
        {
            return false;
        }

        if (valueStart >= valueEndExclusive)
        {
            return false;
        }

        propertyObjectJson = objectJson.Substring(valueStart, valueEndExclusive - valueStart).Trim();
        return propertyObjectJson.StartsWith("{", StringComparison.Ordinal);
    }

    private static bool TryFindRootPropertyRange(
        string objectJson,
        string propertyKey,
        out int removeStart,
        out int removeEndExclusive)
    {
        removeStart = 0;
        removeEndExclusive = 0;
        if (!TryFindRootPropertyValueRange(objectJson, propertyKey, out int valueStart, out int valueEndExclusive))
        {
            return false;
        }

        string quotedKey = "\"" + propertyKey + "\"";
        if (!TryFindRootQuotedKeyIndex(objectJson, quotedKey, out int keyIndex))
        {
            return false;
        }

        removeStart = keyIndex;
        int scanBack = keyIndex - 1;
        while (scanBack >= 0 && char.IsWhiteSpace(objectJson[scanBack]))
        {
            scanBack--;
        }

        if (scanBack >= 0 && objectJson[scanBack] == ',')
        {
            removeStart = scanBack;
            removeEndExclusive = valueEndExclusive;
            while (removeEndExclusive < objectJson.Length && char.IsWhiteSpace(objectJson[removeEndExclusive]))
            {
                removeEndExclusive++;
            }

            return true;
        }

        removeEndExclusive = valueEndExclusive;
        while (removeEndExclusive < objectJson.Length && char.IsWhiteSpace(objectJson[removeEndExclusive]))
        {
            removeEndExclusive++;
        }

        if (removeEndExclusive < objectJson.Length && objectJson[removeEndExclusive] == ',')
        {
            removeEndExclusive++;
        }

        return true;
    }

    private static bool TryFindRootPropertyValueRange(
        string objectJson,
        string propertyKey,
        out int valueStart,
        out int valueEndExclusive)
    {
        valueStart = 0;
        valueEndExclusive = 0;
        string quotedKey = "\"" + propertyKey + "\"";
        if (!TryFindRootQuotedKeyIndex(objectJson, quotedKey, out int keyIndex))
        {
            return false;
        }

        int colonIndex = objectJson.IndexOf(':', keyIndex + quotedKey.Length);
        if (colonIndex < 0)
        {
            return false;
        }

        valueStart = SkipJsonWhitespace(objectJson, colonIndex + 1);
        if (valueStart >= objectJson.Length)
        {
            return false;
        }

        return TryReadJsonValueRange(objectJson, valueStart, out valueEndExclusive);
    }

    private static bool TryFindRootQuotedKeyIndex(string objectJson, string quotedKey, out int keyIndex)
    {
        keyIndex = -1;
        if (string.IsNullOrWhiteSpace(objectJson) || string.IsNullOrWhiteSpace(quotedKey))
        {
            return false;
        }

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = 0; i <= objectJson.Length - quotedKey.Length; i++)
        {
            char ch = objectJson[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                if (depth == 1 && string.Compare(objectJson, i, quotedKey, 0, quotedKey.Length, StringComparison.Ordinal) == 0)
                {
                    keyIndex = i;
                    return true;
                }

                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
            }
        }

        return false;
    }

    private static string CleanupJsonCommas(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        return json
            .Replace("{,", "{")
            .Replace(",}", "}")
            .Replace(",]", "]")
            .Replace("[,", "[");
    }

    private static int FindJsonArrayStartAfterKey(string json, string arrayKey)
    {
        string quotedKey = "\"" + arrayKey + "\"";
        if (!TryFindRootQuotedKeyIndex(json, quotedKey, out int keyIndex))
        {
            return -1;
        }

        int colonIndex = json.IndexOf(':', keyIndex + quotedKey.Length);
        if (colonIndex < 0)
        {
            return -1;
        }

        int bracketIndex = SkipJsonWhitespace(json, colonIndex + 1);
        if (bracketIndex >= json.Length || json[bracketIndex] != '[')
        {
            return -1;
        }

        return bracketIndex;
    }

    private static int SkipJsonWhitespace(string json, int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }

        return index;
    }

    private static bool TryReadJsonValueRange(string json, int startIndex, out int endExclusive)
    {
        endExclusive = startIndex;
        if (startIndex >= json.Length)
        {
            return false;
        }

        startIndex = SkipJsonWhitespace(json, startIndex);
        if (startIndex >= json.Length)
        {
            return false;
        }

        char ch = json[startIndex];
        if (ch == '{')
        {
            return TryReadJsonObjectRange(json, startIndex, out endExclusive);
        }

        if (ch == '[')
        {
            return TryReadJsonArrayRange(json, startIndex, out endExclusive);
        }

        if (ch == '"')
        {
            return TryReadJsonStringRange(json, startIndex, out endExclusive);
        }

        if (string.Compare(json, startIndex, "true", 0, 4, StringComparison.Ordinal) == 0)
        {
            endExclusive = startIndex + 4;
            return true;
        }

        if (string.Compare(json, startIndex, "false", 0, 5, StringComparison.Ordinal) == 0)
        {
            endExclusive = startIndex + 5;
            return true;
        }

        if (string.Compare(json, startIndex, "null", 0, 4, StringComparison.Ordinal) == 0)
        {
            endExclusive = startIndex + 4;
            return true;
        }

        int index = startIndex;
        while (index < json.Length)
        {
            char c = json[index];
            if (c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c))
            {
                break;
            }

            index++;
        }

        if (index > startIndex)
        {
            endExclusive = index;
            return true;
        }

        return false;
    }

    private static bool TryReadJsonStringRange(string json, int startIndex, out int endExclusive)
    {
        endExclusive = startIndex;
        if (startIndex >= json.Length || json[startIndex] != '"')
        {
            return false;
        }

        bool escape = false;
        for (int i = startIndex + 1; i < json.Length; i++)
        {
            char ch = json[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\')
            {
                escape = true;
                continue;
            }

            if (ch == '"')
            {
                endExclusive = i + 1;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadJsonArrayRange(string json, int startIndex, out int endExclusive)
    {
        endExclusive = startIndex;
        if (startIndex >= json.Length || json[startIndex] != '[')
        {
            return false;
        }

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = startIndex; i < json.Length; i++)
        {
            char ch = json[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '[')
            {
                depth++;
                continue;
            }

            if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    endExclusive = i + 1;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>文字列リテラルを考慮しながら { ... } オブジェクト範囲を読み取ります。</summary>
    private static bool TryReadJsonObjectRange(string json, int startIndex, out int endExclusive)
    {
        endExclusive = startIndex;
        if (startIndex >= json.Length || json[startIndex] != '{')
        {
            return false;
        }

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = startIndex; i < json.Length; i++)
        {
            char ch = json[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escape = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    endExclusive = i + 1;
                    return true;
                }
            }
        }

        return false;
    }
}
