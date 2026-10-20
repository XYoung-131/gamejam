using UnityEngine;

// ═══════════════════════════════════════════════════════════════
//  事件选项效果（EventEffects）
//
//  飞书「游戏事件」表里的选项文案形如：
//    "清理垃圾也挺好玩——获得 解压垃圾"
//    "好舒服——减少3点血量，增加2点最大生命值"
//  每个具体事件（EventPanel_xxx）都会把文案拆成"数值 + 物品名"，
//  再调这里的入口真正改到玩家身上。集中放一处的原因：
//    ① 所有改动都必须走 PlayerRunState 的事件入口（内部会 Capture 快照），
//       否则改动活不过下一关的场景重载；
//    ② 找不到资产 / 背包已满等失败情况要统一打日志，排查时只有一个地方要翻；
//    ③ 拿到遗物后要立刻刷新长条上的遗物栏（EventPanel.RefreshRelics）。
//
//  约定：所有方法在没有场上玩家（例如开局的"实验开始"）时也不报错，
//  改动会写进 PlayerRunState 的快照 / 待回放增量，下一关生成玩家时生效。
// ═══════════════════════════════════════════════════════════════
public static class EventEffects
{
    /// <summary>场上当前玩家（事件多半发生在上一场战斗的场景里，通常不为 null）</summary>
    public static PlayerUnit Player => Object.FindObjectOfType<PlayerUnit>();

    // ============================================================
    //                        遗物
    // ============================================================

    /// <summary>
    /// 按中文名发一件遗物（名字取飞书文案里写的那个）。
    /// 拿不到（工程里没有同名资产、或已经持有）返回 false，只打日志不抛错。
    /// </summary>
    public static bool GrantRelic(string relicName)
    {
        RelicData relic = EventItemLibrary.FindRelic(relicName);
        if (relic == null)
        {
            Debug.LogWarning($"[事件] 找不到遗物资产【{relicName}】（应在 ScriptableObjects/遗物/ 下），" +
                             "这次没有发放任何遗物");
            return false;
        }

        PlayerUnit player = Player;
        if (player != null && player.HasRelic(relic))
        {
            Debug.Log($"[事件] 已经持有遗物【{relicName}】，本次不再重复发放");
            return false;
        }

        PlayerRunState.GrantRelic(player, relic);
        EventPanel.RefreshRelics();   // 长条上的遗物栏立刻显示新拿到的那件
        Debug.Log($"[事件] 获得遗物【{relicName}】");
        return true;
    }

    // ============================================================
    //                        药水
    // ============================================================

    /// <summary>发一瓶指定名字的药水（背包满返回 false）</summary>
    public static bool GrantPotion(string potionName)
    {
        PotionData potion = EventItemLibrary.FindPotion(potionName);
        if (potion == null)
        {
            Debug.LogWarning($"[事件] 找不到药水资产【{potionName}】（应在 ScriptableObjects/药水/ 下）");
            return false;
        }
        return GrantPotion(potion, potionName);
    }

    /// <summary>
    /// 发 count 瓶随机饮料，返回实际拿到手的瓶数
    /// （背包上限由 PlayerUnit.maxPotions 决定，满了就不再发）。
    /// </summary>
    public static int GrantRandomPotions(int count)
    {
        int granted = 0;
        for (int i = 0; i < count; i++)
        {
            PotionData potion = EventItemLibrary.RandomPotion();
            if (potion == null)
            {
                Debug.LogWarning("[事件] 药水池为空，随机饮料没有发放");
                break;
            }
            if (GrantPotion(potion, potion.potionName)) granted++;
            else break;   // 背包已满：继续抽也没意义
        }
        return granted;
    }

    private static bool GrantPotion(PotionData potion, string displayName)
    {
        PlayerUnit player = Player;
        if (!PlayerRunState.GrantPotion(player, potion))
        {
            int limit = player != null ? player.maxPotions : 0;
            Debug.LogWarning($"[事件] 背包已满（上限 {limit} 瓶），【{displayName}】没能带走");
            return false;
        }
        Debug.Log($"[事件] 获得饮料【{displayName}】");
        return true;
    }

    // ============================================================
    //                    数值改动（生命 / 上限）
    // ============================================================

    /// <summary>当前生命加减（负数 = 扣血，最少留 1 点）</summary>
    public static void HealthDelta(int delta)
    {
        if (delta == 0) return;
        PlayerRunState.ApplyHealthDelta(Player, delta);
        Debug.Log($"[事件] 当前生命 {(delta > 0 ? "+" : "")}{delta}");
    }

    /// <summary>按最大生命比例回血（0.2 = 回复 20%）</summary>
    public static void HealPercent(float ratio)
    {
        if (ratio <= 0f) return;
        PlayerRunState.HealPercent(Player, ratio);
        Debug.Log($"[事件] 回复 {Mathf.RoundToInt(ratio * 100f)}% 最大生命");
    }

    /// <summary>最大生命上限加减（增加时当前生命不变；减少时当前生命会被压到新上限）</summary>
    public static void MaxHealthDelta(int delta)
    {
        if (delta == 0) return;
        PlayerRunState.ApplyMaxHealthDelta(Player, delta);
        Debug.Log($"[事件] 最大生命上限 {(delta > 0 ? "+" : "")}{delta}");
    }

    /// <summary>最大耐力上限加减</summary>
    public static void MaxStaminaDelta(int delta)
    {
        if (delta == 0) return;
        PlayerRunState.ApplyMaxStaminaDelta(Player, delta);
        Debug.Log($"[事件] 最大耐力上限 {(delta > 0 ? "+" : "")}{delta}");
    }

    /// <summary>最大思绪上限加减</summary>
    public static void MaxThoughtDelta(int delta)
    {
        if (delta == 0) return;
        PlayerRunState.ApplyMaxThoughtDelta(Player, delta);
        Debug.Log($"[事件] 最大思绪上限 {(delta > 0 ? "+" : "")}{delta}");
    }

    // ============================================================
    //                     技能选择
    // ============================================================

    /// <summary>
    /// 记下"额外进行 N 次新技能选择"（「重启」选项 1）。
    /// 事件里没有卡牌界面，所以先存进本局状态，
    /// 等下一次战斗结算界面（BattleEndPanel）打开时补出对应次数的选择按钮。
    /// </summary>
    public static void QueueExtraCardChoices(int count)
    {
        if (count <= 0) return;
        PlayerRunState.QueueExtraCardChoices(count);
        Debug.Log($"[事件] 额外获得 {count} 次技能选择（下一次战斗结算界面里可进行）");
    }
}
