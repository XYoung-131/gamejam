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
}
