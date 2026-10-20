using System.Collections.Generic;
using UnityEngine;

/// <summary>New run starting cards. Repeated references represent separate copies.</summary>
[CreateAssetMenu(fileName = "StartingDeck", menuName = "Game/Starting Deck")]
public class StartingDeckData : ScriptableObject
{
    public List<CardData> fixedSkills = new List<CardData>();
    public List<CardData> attackPool = new List<CardData>();
    public List<CardData> defensePool = new List<CardData>();

    public void ApplyTo(PlayerCardSystem cards)
    {
        if (cards == null) return;

        // Copy lists so later run rewards cannot mutate the starting asset.
        cards.initialCards = new List<CardData>(fixedSkills);
        cards.attackPool = new List<CardData>(attackPool);
        cards.defensePool = new List<CardData>(defensePool);
    }
}
