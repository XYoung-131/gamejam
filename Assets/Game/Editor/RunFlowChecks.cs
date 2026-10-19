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

        foreach (RunRouteData.Chapter chapter in route.chapters)
        {
            foreach (LevelData level in chapter.normals)
                Require(level.variants.Count >= 2, "Normal level needs a variant pool");
            foreach (LevelData level in chapter.elites)
                Require(level.variants.Count > 0, "Elite level has no layout");
            Require(chapter.boss.variants.Count > 0, "Boss level has no layout");
        }

        int variantRolls = 0, eventRolls = 0;
        var flow = new RunProgression(
            count => { variantRolls++; return count - 1; },
            count => { eventRolls++; return count - 1; });
        var eventNames = new HashSet<string>
        {
            "垃圾清理", "奇怪的药剂", "小助手", "抉择", "次元饮料",
            "洗礼", "温泉", "自助售卖机", "重启"
        };
        var seenEvents = new HashSet<string>();
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
                int expectedVariant = flow.CurrentLevel.variants.Count - 1;
                Require(flow.CurrentVariantIndex == expectedVariant &&
                    flow.CurrentVariantIndex == expectedVariant && variantRolls == fights + 1,
                    "Variant must be rolled once per battle from the current level pool");
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
                    Require(eventNames.Contains(flow.EventId) && seenEvents.Add(flow.EventId),
                        "A post-battle event must use an available event once per run");
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
        Require(fights == 16 && variantRolls == 16 && events == 9 && eventRolls == 8 &&
            rests == 4 && flow.Phase == RunPhase.Victory,
            "Full run did not end correctly");
        Debug.Log("Full run route checks passed.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
