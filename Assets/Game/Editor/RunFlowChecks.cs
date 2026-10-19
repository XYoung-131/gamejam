using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RunFlowChecks
{
    [MenuItem("Tools/Game/Check Full Run Flow")]
    public static void Run()
    {
        RunRouteData route = AssetDatabase.LoadAssetAtPath<RunRouteData>("Assets/Game/Resources/RunRoute.asset");
        Require(route != null && route.IsComplete, "Route must include two complete chapters");
        RewardCardCatalog catalog = AssetDatabase.LoadAssetAtPath<RewardCardCatalog>(
            "Assets/Game/Resources/RewardCardCatalog.asset");
        Require(catalog != null && catalog.cards.Count >= 3, "Reward catalog missing");
        var seen = new HashSet<CardData>();
        foreach (CardData card in catalog.cards)
            Require(card != null && seen.Add(card) && CardPoolRules.IsOfferEligible(card, card.library),
                "Reward catalog contains a duplicate or unavailable skill");

        var flow = new RunProgression();
        Require(flow.StartNew(route), "New run failed");
        Require(flow.Phase == RunPhase.Event && flow.EventId == "实验开始", "Opening event missing");
        Require(flow.CompleteEvent() && !flow.CompleteEvent(), "Opening event can complete twice");
        int fights = 0, events = 1, rests = 0;
        for (int chapter = 0; chapter < 2; chapter++)
        {
            for (int battle = 0; battle < 8; battle++)
            {
                Require(flow.Phase == RunPhase.Battle && flow.ChapterIndex == chapter &&
                    flow.BattleIndex == battle && flow.CurrentLevel != null, "Wrong battle order");
                if (battle == 3) Require(route.chapters[chapter].elites.Contains(flow.CurrentLevel), "Wrong elite");
                if (battle == 7) Require(flow.CurrentLevel == route.chapters[chapter].boss, "Wrong boss");
                fights++;
                Require(flow.CompleteBattle() && !flow.CompleteBattle(), "Victory duplicated rewards");
                Require(flow.ContinueAfterRewards() && !flow.ContinueAfterRewards(), "Rewards advanced twice");
                if (flow.Phase == RunPhase.Event)
                {
                    events++;
                    Require(battle == 0 || battle == 1 || battle == 4 || battle == 5,
                        "Event after wrong battle");
                    Require(flow.CompleteEvent() && !flow.CompleteEvent(), "Event advanced twice");
                }
                else if (flow.Phase == RunPhase.Rest)
                {
                    rests++;
                    Require(battle == 2 || battle == 6, "Rest after wrong battle");
                    Require(flow.ChooseRest(RestOption.HealHalf) &&
                        !flow.ChooseRest(RestOption.HealHalf), "Rest advanced twice");
                }
            }
        }
        Require(fights == 16 && events == 9 && rests == 4 && flow.Phase == RunPhase.Victory,
            "Full run did not end correctly");
        Debug.Log("Full run route checks passed.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
