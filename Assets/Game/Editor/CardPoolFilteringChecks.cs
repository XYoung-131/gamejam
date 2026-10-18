using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class CardPoolFilteringChecks
{
    public static void Run()
    {
        CheckGeneratedPools();
        CheckRuntimeOffer();
        Debug.Log("Card pool filtering checks passed.");
    }

    private static void CheckGeneratedPools()
    {
        MethodInfo split = typeof(BuildBattleScene).GetMethod(
            "SplitCardPools", BindingFlags.NonPublic | BindingFlags.Static);
        Require(split != null, "Missing scene pool builder");

        object[] args = { null, null };
        split.Invoke(null, args);
        var attack = (List<CardData>)args[0];
        var defense = (List<CardData>)args[1];

        Require(attack.Count > 0 && defense.Count > 0, "Expected both generated pools");
        foreach (CardData card in attack)
            Require(card.rarity != Rarity.敌方特有 && card.library == CardPool.攻击,
                "Invalid card in generated attack pool: " + card.cardName);
        foreach (CardData card in defense)
            Require(card.rarity != Rarity.敌方特有 && card.library == CardPool.防御,
                "Invalid card in generated defense pool: " + card.cardName);
    }

    private static void CheckRuntimeOffer()
    {
        var valid = ScriptableObject.CreateInstance<CardData>();
        var enemyOnly = ScriptableObject.CreateInstance<CardData>();
        var wrongPool = ScriptableObject.CreateInstance<CardData>();
        var go = new GameObject("CardPoolFilteringChecks");

        try
        {
            valid.cardName = "Valid attack";
            valid.library = CardPool.攻击;
            valid.rarity = Rarity.普通;
            enemyOnly.cardName = "Enemy only";
            enemyOnly.library = CardPool.攻击;
            enemyOnly.rarity = Rarity.敌方特有;
            wrongPool.cardName = "Wrong pool";
            wrongPool.library = CardPool.防御;
            wrongPool.rarity = Rarity.普通;

            var cards = go.AddComponent<PlayerCardSystem>();
            MethodInfo build = typeof(PlayerCardSystem).GetMethod(
                "BuildOffer", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(build != null, "Missing runtime offer builder");
            Require(build.GetParameters().Length == 2,
                "Runtime offer builder must receive the requested pool");

            var mixed = new List<CardData> { valid, enemyOnly, wrongPool };
            var offered = (List<CardData>)build.Invoke(cards, new object[] { mixed, CardPool.攻击 });
            Require(offered.Count == cards.offerCount, "Expected a full offer from the valid card");
            foreach (CardData card in offered)
                Require(card == valid, "Invalid card in runtime attack offer: " + card.cardName);

            var blocked = new List<CardData> { enemyOnly, wrongPool };
            offered = (List<CardData>)build.Invoke(cards, new object[] { blocked, CardPool.攻击 });
            Require(offered.Count == 0, "A pool without eligible cards must offer nothing");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(valid);
            UnityEngine.Object.DestroyImmediate(enemyOnly);
            UnityEngine.Object.DestroyImmediate(wrongPool);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
