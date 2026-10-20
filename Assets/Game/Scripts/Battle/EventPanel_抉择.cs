using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：抉择
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28OlzQDUx4p（2026-10-20 读取）：
//    名称   = 抉择
//    描述   = 战斗结束后，在你面前出现3条路，直觉告诉你，这3条路都危险重重，请做出你的抉择
//    选项   = 血量上限减2点，耐力和思绪上限加5点 / 耐力上限减5点，血量加2点 / 思绪上限减5点，血量加2点
//  调用 EventPanel_抉择.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 最大生命 -2（当前血被压到新上限），最大耐力 +5，最大思绪 +5
//    选项 2 → 最大耐力 -5，当前生命 +2
//    选项 3 → 最大思绪 -5，当前生命 +2
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_抉择
{
    private const string 事件名称 = "抉择";
    private const string 事件描述 =
        "战斗结束后，在你面前出现3条路，直觉告诉你，这3条路都危险重重，请做出你的抉择";

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "血量上限减2点，耐力和思绪上限加5点",
                "耐力上限减5点，血量加2点",
                "思绪上限减5点，血量加2点",
            },
            new UnityAction<int>[]
            {
                _ =>
                {
                    EventEffects.MaxHealthDelta(-2);
                    EventEffects.MaxStaminaDelta(5);
                    EventEffects.MaxThoughtDelta(5);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.MaxStaminaDelta(-5);
                    EventEffects.HealthDelta(2);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.MaxThoughtDelta(-5);
                    EventEffects.HealthDelta(2);
                    EventPanel.Close();
                },
            });
    }
}
