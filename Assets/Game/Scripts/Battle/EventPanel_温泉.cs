using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：温泉
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28NeaP01zle（2026-10-20 读取）：
//    名称   = 温泉
//    描述   = 研究所里搬来了个奇怪的温泉，大家都泡的很舒服，不过研究员叮嘱不能多泡
//    选项   = 舒服——回复20%血量 / 好舒服——减少3点血量，增加2点最大生命值 / 太舒服了——减少6点血量，增加4点最大生命值
//  调用 EventPanel_温泉.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 回复 20% 最大生命
//    选项 2 → 当前生命 -3，最大生命上限 +2
//    选项 3 → 当前生命 -6，最大生命上限 +4
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_温泉
{
    private const string 事件名称 = "温泉";
    private const string 事件描述 = "研究所里搬来了个奇怪的温泉，大家都泡的很舒服，不过研究员叮嘱不能多泡";

    private const float 回血比例 = 0.2f;

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "舒服——回复20%血量",
                "好舒服——减少3点血量，增加2点最大生命值",
                "太舒服了——减少6点血量，增加4点最大生命值",
            },
            new UnityAction<int>[]
            {
                _ =>
                {
                    EventEffects.HealPercent(回血比例);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.HealthDelta(-3);
                    EventEffects.MaxHealthDelta(2);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.HealthDelta(-6);
                    EventEffects.MaxHealthDelta(4);
                    EventPanel.Close();
                },
            });
    }
}
