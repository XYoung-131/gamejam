using System.Collections.Generic;

/// <summary>玩家抽牌与开局技能使用同一套资格规则。</summary>
public static class CardPoolRules
{
    public static bool IsOfferEligible(CardData card, CardPool requestedPool) =>
        card != null && card.library == requestedPool &&
        !string.IsNullOrWhiteSpace(card.cardName) &&
        card.rarity != Rarity.敌方特有 && !card.initialOnly;

    public static bool IsEnemySkillInfusionEligible(CardData card) =>
        card != null && !string.IsNullOrWhiteSpace(card.cardName) &&
        card.rarity != Rarity.敌方特有;

    public static List<CardData> GetInitialCards(IEnumerable<CardData> source)
    {
        var result = new List<CardData>();
        if (source == null) return result;
        var seen = new HashSet<CardData>();
        foreach (CardData card in source)
            if (card != null && card.library == CardPool.防御 && card.initialOnly &&
                card.rarity != Rarity.敌方特有 && seen.Add(card))
                result.Add(card);
        return result;
    }

    public static List<CardData> MergeInitialCards(IEnumerable<CardData> configured,
        IEnumerable<CardData> pool)
    {
        var result = new List<CardData>();
        var seen = new HashSet<CardData>();
        if (configured != null)
            foreach (CardData card in configured)
                if (card != null && seen.Add(card)) result.Add(card);
        foreach (CardData card in GetInitialCards(pool))
            if (seen.Add(card)) result.Add(card);
        return result;
    }
}
