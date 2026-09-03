using System;
using UnityEngine;

// =============================================================================
// HistorySimulation JSON（JsonUtility 互換 DTO）
// 連携: HistorySimulationLoader / HistoryDecodingPresenter
// =============================================================================

/// <summary>Resources/HistorySimulation 配下ファイルのルート。</summary>
[Serializable]
public class HistorySimulationFileDto
{
    public HistorySimulationEventDto[] simulationEvents;
}

/// <summary>1 件の歴史シミュレーションイベント。</summary>
[Serializable]
public class HistorySimulationEventDto
{
    public string eventId;
    public int macroTurn;
    public HistoryTimestampDto timestamp;
    public string targetNationId;
    public string relatedNationId;
    public string eventCategory;
    public string eventType;
    public string logMessageTemplate;
    public bool isHistoricalCollapseRoute;
    public string evolutionSystemType;
    public string microLore;
    public string conditionFlag;
    public HistoryHistoricalModeDto historicalMode;
    public HistoryAlternativeModeDto alternativeMode;
    public HistoryMmoModeDto mmoMode;
    public HistoryProsperityTimelineDto prosperityTimeline;
    public HistoryDecayTimelineDto decayTimeline;
}

[Serializable]
public class HistoryTimestampDto
{
    public int year;
    public int month;
    public int day;
    public int hour;
}

[Serializable]
public class HistoryHistoricalModeDto
{
    public int timeLockedHourOffset;
    public string[] forcedFlagsOnTrigger;
}

[Serializable]
public class HistoryAlternativeModeDto
{
    public HistoryCausalPrerequisitesDto causalPrerequisites;
    public HistoryUnlockRewardsDto unlockRewards;
}

[Serializable]
public class HistoryCausalPrerequisitesDto
{
    public string requiredLogType;
    public int requiredValue;
}

[Serializable]
public class HistoryUnlockRewardsDto
{
    public string alternativeFlag;
    public string alternativeLogMessage;
    public string alternativeLore;
}

[Serializable]
public class HistoryMmoModeDto
{
    public string zoneId;
    public int recommendedLevel;
    public string progressTriggerFlag;
}

[Serializable]
public class HistoryTimelinePhaseDto
{
    public string timeRange;
    public string npcDialogue;
    public string statusBuff;
    public string visualChange;
    public string gameplayReward;
}

[Serializable]
public class HistoryProsperityTimelineDto
{
    public HistoryTimelinePhaseDto phase1_accumulation;
    public HistoryTimelinePhaseDto phase2_pioneering;
    public HistoryTimelinePhaseDto phase3_perfection;
}

[Serializable]
public class HistoryDecayTimelineDto
{
    public HistoryTimelinePhaseDto phase1_premonition;
    public HistoryTimelinePhaseDto phase2_poverty;
    public HistoryTimelinePhaseDto phase3_critical;
}
