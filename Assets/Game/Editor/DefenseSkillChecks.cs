using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>在 Unity 编辑器菜单运行，检查防御牌的实际资产引用。</summary>
public static class DefenseSkillChecks
{
    [MenuItem("Tools/Game/Check Defense Skills")]
    public static void Run()
    {
        string[] guids = AssetDatabase.FindAssets("t:CardData",
            new[] { "Assets/Game/ScriptableObjects/技能/防御技能" });
        Require(guids.Length == 29, "Expected 29 defense assets");
        var names = new HashSet<string>();
        int enemyOnly = 0;
        foreach (string guid in guids)
        {
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(
                AssetDatabase.GUIDToAssetPath(guid));
            Require(card != null && names.Add(card.cardName), "Missing or duplicate defense card");
            Require(card.library == CardPool.防御, card.cardName + " is in the wrong pool");
            if (card.rarity == Rarity.敌方特有) enemyOnly++;
            if (card.defenseEffectRule == DefenseEffectRule.抱抱喷射 ||
                card.defenseEffectRule == DefenseEffectRule.克勒妮的呼唤)
                Require(card.summonUnit != null && card.summonUnit.prefab != null,
                    card.cardName + " has no summon prefab");
            if (card.defenseEffectRule == DefenseEffectRule.克勒妮的呼唤)
                Require(card.alternateSummonUnit != null &&
                    card.alternateSummonUnit.prefab != null,
                    card.cardName + " has no alternate summon prefab");
            if (card.defenseEffectRule == DefenseEffectRule.投掷织毯)
                Require(card.generatedTerrain != null, card.cardName + " has no terrain");
            if (card.defenseEffectRule == DefenseEffectRule.炎障 ||
                card.defenseEffectRule == DefenseEffectRule.神圣祝福 ||
                card.defenseEffectRule == DefenseEffectRule.毒遁)
                Require(card.specialBuff != null, card.cardName + " has no Buff");
        }
        Require(enemyOnly == 13, "Expected 13 enemy-only defense cards");
        foreach (string starter in new[] { "跳" })
        {
            Require(names.Contains(starter), "Missing starting card: " + starter);
            CardData card = LoadByName(starter);
            Require(card != null && card.initialOnly, starter + " must start slotted");
            Require(!CardPoolRules.IsOfferEligible(card, CardPool.防御),
                starter + " entered ordinary offers");
        }
        foreach (string deckCard in new[] { "格挡", "远跳" })
        {
            CardData card = LoadByName(deckCard);
            Require(card != null && !card.initialOnly, deckCard + " must be in the starting deck");
            Require(CardPoolRules.IsOfferEligible(card, CardPool.防御),
                deckCard + " cannot be offered from the defense deck");
        }
        Debug.Log("Defense skill assets passed: 29 cards, 13 enemy-only, 1 starter.");
    }

    private static CardData LoadByName(string name)
    {
        return AssetDatabase.LoadAssetAtPath<CardData>(
            "Assets/Game/ScriptableObjects/技能/防御技能/" + name + ".asset");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
