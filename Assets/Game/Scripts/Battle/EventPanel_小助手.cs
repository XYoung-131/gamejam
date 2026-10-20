using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：小助手
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record recvycWTrtG0PZ（2026-10-20 读取）：
//    名称   = 小助手
//    描述   = 研究所里引进了一台外型可爱的机器，能帮助实验体解决需求
//    选项   = 我头疼——增加10点最大思绪上限 / 我腿酸——增加10点最大耐力上限 / 我累了——回复20%血量
//  调用 EventPanel_小助手.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 最大思绪上限 +10
//    选项 2 → 最大耐力上限 +10
//    选项 3 → 回复 20% 最大生命
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_小助手
{
    private const string 事件名称 = "小助手";
    private const string 事件描述 = "研究所里引进了一台外型可爱的机器，能帮助实验体解决需求";

    private const int 上限加成 = 10;
    private const float 回血比例 = 0.2f;

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "我头疼——增加10点最大思绪上限",
                "我腿酸——增加10点最大耐力上限",
                "我累了——回复20%血量",
            },
            new UnityAction<int>[]
            {
                _ =>
                {
                    EventEffects.MaxThoughtDelta(上限加成);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.MaxStaminaDelta(上限加成);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.HealPercent(回血比例);
                    EventPanel.Close();
                },
            });
    }
}
