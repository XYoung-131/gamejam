using System;

/// <summary>攻击技能可独立验证的次数、费用与概率规则。</summary>
public static class AttackEffectMath
{
    public static int MissingHealthQuarters(int currentHealth, int maxHealth)
    {
        if (maxHealth <= 0) return 0;
        return Math.Min(4, Math.Max(0, (maxHealth - Math.Max(0, currentHealth)) * 4 / maxHealth));
    }

    public static int HitCount(int baseHits, int extraPerQuarter, int maxHealth, int currentHealth)
    {
        return Math.Max(1, baseHits + extraPerQuarter * MissingHealthQuarters(currentHealth, maxHealth));
    }

    public static int RepeatsForPriorUses(int priorUses, int maxExtra, int baseHits)
    {
        return Math.Max(1, baseHits + Math.Min(Math.Max(0, priorUses), Math.Max(0, maxExtra)));
    }

    public static int DiscountedStamina(int baseCost, int priorUses, int maxDiscount)
    {
        return Math.Max(0, baseCost - Math.Min(Math.Max(0, priorUses), Math.Max(0, maxDiscount)));
    }

    public static int HalfRefund(int spentStamina) => Math.Max(0, (spentStamina + 1) / 2);

    public static bool RollSucceeds(float roll, float probability)
    {
        return probability > 0f && roll < probability;
    }
}
