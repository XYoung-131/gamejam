using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RewardCardCatalog", menuName = "Game/Reward Card Catalog")]
public class RewardCardCatalog : ScriptableObject
{
    public List<CardData> cards = new List<CardData>();

    public List<CardData> Roll(int count)
    {
        var available = new List<CardData>();
        foreach (CardData card in cards)
            if (card != null && CardPoolRules.IsOfferEligible(card, card.library) && !available.Contains(card))
                available.Add(card);
        for (int i = available.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            CardData swap = available[i]; available[i] = available[j]; available[j] = swap;
        }
        return available.GetRange(0, Mathf.Min(count, available.Count));
    }
}
