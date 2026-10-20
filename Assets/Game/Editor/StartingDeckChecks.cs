using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Checks the playable starting deck asset and its draw eligibility.</summary>
public static class StartingDeckChecks
{
    [MenuItem("Tools/Game/Check Starting Deck")]
    public static void Run()
    {
        var asset = AssetDatabase.LoadMainAssetAtPath("Assets/Game/Resources/StartingDeck.asset")
            as ScriptableObject;
        Require(asset != null, "Starting deck asset is missing");

        var data = new SerializedObject(asset);
        CheckCards(data.FindProperty("attackPool"), new Dictionary<string, int>
        {
            { "拳打", 4 }, { "抛射", 2 }, { "痛殴", 1 }
        });
        CheckCards(data.FindProperty("defensePool"), new Dictionary<string, int>
        {
            { "远跳", 2 }, { "绊倒", 2 }, { "格挡", 3 }
        });
        CheckCards(data.FindProperty("fixedSkills"), new Dictionary<string, int>
        {
            { "推", 1 }, { "跳", 1 }
        });

        foreach (string name in new[] { "远跳", "格挡" })
        {
            var card = AssetDatabase.LoadAssetAtPath<CardData>(
                "Assets/Game/ScriptableObjects/技能/防御技能/" + name + ".asset");
            Require(CardPoolRules.IsOfferEligible(card, CardPool.防御), name + " cannot be drawn");
        }

        Debug.Log("Starting deck checks passed.");
    }

    private static void CheckCards(SerializedProperty property, Dictionary<string, int> expected)
    {
        Require(property != null && property.isArray, "Starting deck list is missing");
        var actual = new Dictionary<string, int>();
        int expectedCount = 0;
        foreach (int count in expected.Values) expectedCount += count;
        Require(property.arraySize == expectedCount, "Starting deck list has wrong size");
        for (int i = 0; i < property.arraySize; i++)
        {
            var card = property.GetArrayElementAtIndex(i).objectReferenceValue as CardData;
            Require(card != null, "Starting deck has a missing card reference");
            actual.TryGetValue(card.cardName, out int count);
            actual[card.cardName] = count + 1;
        }
        foreach (var entry in expected)
            Require(actual.TryGetValue(entry.Key, out int count) && count == entry.Value,
                entry.Key + " has wrong copy count");
        Require(actual.Count == expected.Count, "Starting deck has an unexpected card");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
