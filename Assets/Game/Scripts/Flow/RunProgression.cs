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
        for (int i = 0; i < eliteChoices.Length; i++) eliteChoices[i] = Random.Range(0, 2);
        ChapterIndex = 0;
        BattleIndex = 0;
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
            EventId = "第" + (ChapterIndex + 1) + "章第" + (finished < 3 ? finished + 1 : finished) + "关后事件";
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
