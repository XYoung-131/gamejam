using System;
using System.Collections.Generic;
using UnityEngine;

public enum RunPhase { Menu, Event, Battle, Reward, Rest, Victory, Defeat }
public enum RestOption { HealHalf, HealthAndHealQuarter, HealthAndResources }

/// <summary>Pure route state. No scene loading or reward side effects.</summary>
public sealed class RunProgression
{
    public RunPhase Phase { get; private set; } = RunPhase.Menu;
    public int ChapterIndex { get; private set; }
    public int BattleIndex { get; private set; }
    public string EventId { get; private set; }
    public RunRouteData Route { get; private set; }
    private int[] eliteChoices;
    private readonly Func<int, int> variantPicker;
    private readonly Func<int, int> eventPicker;
    private static readonly string[] EventNames =
    {
        "垃圾清理", "奇怪的药剂", "小助手", "抉择", "次元饮料",
        "洗礼", "温泉", "自助售卖机", "重启"
    };
    private readonly List<string> remainingEvents = new List<string>();
    private int currentVariantIndex = -1;

    public RunProgression(Func<int, int> variantPicker = null, Func<int, int> eventPicker = null)
    {
        this.variantPicker = variantPicker ?? RollVariant;
        this.eventPicker = eventPicker ?? RollVariant;
    }

    private static int RollVariant(int count) => UnityEngine.Random.Range(0, count);

    /// <summary>Roll once for this battle; reading it again keeps the same layout.</summary>
    public int CurrentVariantIndex
    {
        get
        {
            if (Phase != RunPhase.Battle) return -1;
            if (currentVariantIndex >= 0) return currentVariantIndex;
            LevelData level = CurrentLevel;
            if (level == null || level.variants == null || level.variants.Count == 0) return -1;
            int selected = variantPicker(level.variants.Count);
            if (selected < 0 || selected >= level.variants.Count)
                throw new InvalidOperationException("Variant picker returned an invalid index");
            currentVariantIndex = selected;
            return currentVariantIndex;
        }
    }

    public LevelData CurrentLevel
    {
        get
        {
            if (Route == null || !Route.IsComplete || Phase != RunPhase.Battle) return null;
            RunRouteData.Chapter chapter = Route.chapters[ChapterIndex];
            if (BattleIndex < 3) return chapter.normals[BattleIndex];
            if (BattleIndex == 3) return chapter.elites[eliteChoices[ChapterIndex]];
            if (BattleIndex < 7) return chapter.normals[BattleIndex - 1];
            return chapter.boss;
        }
    }

    public bool StartNew(RunRouteData route)
    {
        if (route == null || !route.IsComplete) return false;
        Route = route;
        eliteChoices = new int[route.chapters.Count];
        for (int i = 0; i < eliteChoices.Length; i++) eliteChoices[i] = UnityEngine.Random.Range(0, 2);
        ChapterIndex = 0;
        BattleIndex = 0;
        currentVariantIndex = -1;
        remainingEvents.Clear();
        remainingEvents.AddRange(EventNames);
        EventId = "实验开始";
        Phase = RunPhase.Event;
        return true;
    }

    public bool CompleteEvent()
    {
        if (Phase != RunPhase.Event) return false;
        EventId = null;
        Phase = RunPhase.Battle;
        return true;
    }

    public bool CompleteBattle()
    {
        if (Phase != RunPhase.Battle) return false;
        currentVariantIndex = -1;
        Phase = RunPhase.Reward;
        return true;
    }

    public bool ContinueAfterRewards()
    {
        if (Phase != RunPhase.Reward) return false;
        if (BattleIndex == 7)
        {
            if (++ChapterIndex >= Route.chapters.Count) { Phase = RunPhase.Victory; return true; }
            BattleIndex = 0;
            Phase = RunPhase.Battle;
            return true;
        }
        int finished = BattleIndex++;
        if (finished == 0 || finished == 1 || finished == 4 || finished == 5)
        {
            if (remainingEvents.Count == 0)
                throw new InvalidOperationException("No post-battle events remain");
            int selected = eventPicker(remainingEvents.Count);
            if (selected < 0 || selected >= remainingEvents.Count)
                throw new InvalidOperationException("Event picker returned an invalid index");
            EventId = remainingEvents[selected];
            remainingEvents.RemoveAt(selected);
            Phase = RunPhase.Event;
        }
        else if (finished == 2 || finished == 6) Phase = RunPhase.Rest;
        else Phase = RunPhase.Battle;
        return true;
    }

    public bool ChooseRest(RestOption option)
    {
        if (Phase != RunPhase.Rest || !System.Enum.IsDefined(typeof(RestOption), option)) return false;
        Phase = RunPhase.Battle;
        return true;
    }

    public bool Defeat()
    {
        if (Phase != RunPhase.Battle) return false;
        Phase = RunPhase.Defeat;
        return true;
    }

    // ============================================================
    //                      存档：读 / 恢复
    // ============================================================

    /// <summary>当前精英关的二选一结果（存档用，返回副本）</summary>
    public int[] GetEliteChoices()
    {
        return eliteChoices == null ? new int[0] : (int[])eliteChoices.Clone();
    }

    /// <summary>还没抽到的事件名列表（存档用）</summary>
    public string[] GetRemainingEvents()
    {
        return remainingEvents.ToArray();
    }

    /// <summary>当前战斗已掷出的变体下标（-1 = 还没掷；存档用，不走 CurrentVariantIndex 的相位限制）</summary>
    public int SavedVariantIndex { get { return currentVariantIndex; } }

    /// <summary>
    /// 从存档恢复进度。数据对不上（路线被改过 / 下标越界 / 相位非法）就返回 false，
    /// 调用方读档失败，主界面保持原样。
    /// </summary>
    public bool Restore(RunRouteData route, RunPhase phase, int chapterIndex, int battleIndex,
        string eventId, int[] savedEliteChoices, string[] savedRemainingEvents, int variantIndex)
    {
        if (route == null || !route.IsComplete) return false;
        if (phase != RunPhase.Event && phase != RunPhase.Battle && phase != RunPhase.Rest) return false;
        if (chapterIndex < 0 || chapterIndex >= route.chapters.Count) return false;
        if (battleIndex < 0 || battleIndex > 7) return false;
        if (savedEliteChoices == null || savedEliteChoices.Length != route.chapters.Count) return false;
        foreach (int choice in savedEliteChoices)
            if (choice != 0 && choice != 1) return false;

        Route = route;
        eliteChoices = (int[])savedEliteChoices.Clone();
        ChapterIndex = chapterIndex;
        BattleIndex = battleIndex;
        EventId = eventId;
        remainingEvents.Clear();
        if (savedRemainingEvents != null) remainingEvents.AddRange(savedRemainingEvents);
        currentVariantIndex = variantIndex;
        Phase = phase;
        return true;
    }
}
