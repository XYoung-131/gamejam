using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：重启
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28NeaP01JJz（2026-10-20 读取）：
//    名称   = 重启
//    描述   = 战斗结束后，你像往常一样感到头晕目眩，再将那些繁杂的思绪一扫而空后，头脑顿时清醒了许多
//    选项   = 新的记忆涌现了出来——额外进行2次新技能选择 / 以前的记忆巩固了——获得 回归本质
//  调用 EventPanel_重启.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 在当前事件界面立即连续进行 2 次技能三选一；每次选中的技能加入卡池，
//             两次都完成后才关闭事件并进入下一场战斗。
//    选项 2 → 获得遗物「回归本质」（普通稀有度技能伤害 +1）。
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_重启
{
    private const string 事件名称 = "重启";
    private const string 事件描述 =
        "战斗结束后，你像往常一样感到头晕目眩，再将那些繁杂的思绪一扫而空后，头脑顿时清醒了许多";

    private const string 遗物名 = "回归本质";
    private const int 额外技能选择次数 = 2;

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "新的记忆涌现了出来——额外进行2次新技能选择",
                "以前的记忆巩固了——获得 回归本质",
            },
            new UnityAction<int>[]
            {
                _ => On新的记忆(),
                _ => On以前的记忆(),
            });
    }

    private static void On新的记忆()
    {
        EventPanel.BeginImmediateCardChoices(额外技能选择次数, EventPanel.Close);
    }

    private static void On以前的记忆()
    {
        EventEffects.GrantRelic(遗物名);
        EventPanel.Close();
    }
}
