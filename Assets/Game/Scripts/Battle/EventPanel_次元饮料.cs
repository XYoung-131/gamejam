using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：次元饮料
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28NeaP02PxG（2026-10-20 读取）：
//    名称   = 次元饮料
//    描述   = 研究员在进行空间实验，他们希望可以隔着墙壁将饮料传送给那些危险的实验体，但是饮料经过传送发生了一些未知变化，被封存起来了
//    选项   = 但是你偷拿了一瓶，不是很好喝——减少2点血量，获得 次元抹除 / 但是你偷拿了一瓶，很好喝——增加10点最大思绪上限
//  调用 EventPanel_次元饮料.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 当前生命 -2，获得遗物「次元抹杀」（飞书文案里写作"次元抹除"，同一件）
//    选项 2 → 最大思绪上限 +10
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_次元饮料
{
    private const string 事件名称 = "次元饮料";
    private const string 事件描述 =
        "研究员在进行空间实验，他们希望可以隔着墙壁将饮料传送给那些危险的实验体，但是饮料经过传送发生了一些未知变化，被封存起来了";

    // 文案写"次元抹除"，资产名是"次元抹杀"（EventPanel 里登记了别名，悬停说明两边都能弹）
    private const string 遗物名 = "次元抹杀";

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "但是你偷拿了一瓶，不是很好喝——减少2点血量，获得 次元抹除",
                "但是你偷拿了一瓶，很好喝——增加10点最大思绪上限",
            },
            new UnityAction<int>[]
            {
                _ =>
                {
                    // 先结算遗物，再扣血：这件遗物在"通用池"里，战斗结算的思维碎片
                    // 也可能先抽到它。已经持有的话 GrantRelic 会返回 false，
                    // 这时候再扣 2 点血就成了"纯亏"的选项，所以扣血跟着发放结果走。
                    bool granted = EventEffects.GrantRelic(遗物名);
                    if (granted)
                    {
                        EventEffects.HealthDelta(-2);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning(
                            "[次元饮料] 已经持有遗物【次元抹杀】，本次不再扣血");
                    }
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.MaxThoughtDelta(10);
                    EventPanel.Close();
                },
            });
    }
}
