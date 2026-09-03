using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// NPC 個別ステータス — 社会生活ジョブとは別の個人属性
// =============================================================================

/// <summary>性格。行動スコアの偏りに使います。</summary>
public enum NpcTrait
{
    Diligent = 0,
    Cautious = 1,
    Inquisitive = 2
}

/// <summary>
/// 1 人の NPC の個人情報・バイタル・習得技術です。
/// </summary>
[System.Serializable]
public class NpcIndividualStatus
{
    public string NpcId = string.Empty;
    public string Name = string.Empty;
    [Range(12, 90)] public int Age = 24;
    public string JobId = nameof(NpcCivicJob.Farmer);
    [Range(1f, 3f)] public float JobProficiency = 1f;

    public float MaxHP = 100f;
    public float CurrentHP = 100f;
    public float MaxMP = 80f;
    public float CurrentMP = 80f;
    [Range(0f, 100f)] public float Hunger = 18f;

    public List<string> LearnedMagicIds = new List<string>();
    public NpcTrait Trait = NpcTrait.Diligent;

    /// <summary>シミュレーション上のワールド座標（作業スポット滞在）。</summary>
    public Vector3 WorldPosition;
    /// <summary>現在割り当て中の作業スポット ID。</summary>
    public string AssignedSpotId = string.Empty;

    public NpcCivicJob CivicJob => ParseJobId(JobId);

    public bool IsStarving => Hunger >= 85f;
    public bool IsHungry => Hunger >= 55f;
    public float HpRatio => MaxHP <= 0.01f ? 1f : Mathf.Clamp01(CurrentHP / MaxHP);
    public float MpRatio => MaxMP <= 0.01f ? 1f : Mathf.Clamp01(CurrentMP / MaxMP);

    public NpcIndividualStatus()
    {
    }

    public NpcIndividualStatus(
        string npcId,
        string name,
        int age,
        NpcCivicJob job,
        float proficiency,
        NpcTrait trait,
        params string[] magics)
    {
        NpcId = npcId ?? string.Empty;
        Name = name ?? job.ToString();
        Age = Mathf.Clamp(age, 12, 90);
        JobId = job.ToString();
        JobProficiency = Mathf.Clamp(proficiency, 1f, 3f);
        Trait = trait;
        CurrentHP = MaxHP = 90f + age * 0.2f;
        CurrentMP = MaxMP = 60f + proficiency * 12f;
        Hunger = 12f + (trait == NpcTrait.Diligent ? 6f : 0f);
        LearnedMagicIds = magics != null
            ? new List<string>(magics)
            : new List<string>();
    }

    public static NpcCivicJob ParseJobId(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return NpcCivicJob.Farmer;
        }

        string trimmed = jobId.Trim();
        if (System.Enum.TryParse(trimmed, true, out NpcCivicJob job))
        {
            return job;
        }

        return DynamicJobBuilder.MapJobIdToCivicJob(trimmed);
    }

    public bool KnowsMagic(string magicId)
    {
        if (string.IsNullOrWhiteSpace(magicId) || LearnedMagicIds == null)
        {
            return false;
        }

        for (int i = 0; i < LearnedMagicIds.Count; i++)
        {
            if (string.Equals(LearnedMagicIds[i], magicId, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool TryLearnMagic(string magicId)
    {
        if (string.IsNullOrWhiteSpace(magicId) || KnowsMagic(magicId))
        {
            return false;
        }

        if (LearnedMagicIds == null)
        {
            LearnedMagicIds = new List<string>();
        }

        LearnedMagicIds.Add(magicId.Trim());
        return true;
    }

    public void ClampVitals()
    {
        MaxHP = Mathf.Max(1f, MaxHP);
        MaxMP = Mathf.Max(1f, MaxMP);
        CurrentHP = Mathf.Clamp(CurrentHP, 0f, MaxHP);
        CurrentMP = Mathf.Clamp(CurrentMP, 0f, MaxMP);
        Hunger = Mathf.Clamp(Hunger, 0f, 100f);
        JobProficiency = Mathf.Clamp(JobProficiency, 1f, 3f);
    }

    public string FormatVitals()
    {
        return $"HP {CurrentHP:F0}/{MaxHP:F0} MP {CurrentMP:F0}/{MaxMP:F0} Hunger {Hunger:F0}";
    }
}
