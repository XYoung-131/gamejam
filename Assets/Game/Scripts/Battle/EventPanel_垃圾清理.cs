using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：垃圾清理
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28NeaP00i0a（2026-10-20 读取）：
//    名称   = 垃圾清理
//    描述   = 研究人员叫一层的实验体帮忙清理垃圾
//    选项   = 清理垃圾也挺好玩——获得 解压垃圾 / 问普兰要一些炸药——获得 垃圾毁灭者 / 不管，找个地方睡懒觉——回复20%血量
//  调用 EventPanel_垃圾清理.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 获得遗物「解压垃圾」
//    选项 2 → 获得遗物「垃圾毁灭者」
//    选项 3 → 回复 20% 最大生命
//  三个选项互斥（选完即关闭事件），改动都走 PlayerRunState 快照，能带进下一关。
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_垃圾清理
{
    private const string 事件名称 = "垃圾清理";
    private const string 事件描述 = "研究人员叫一层的实验体帮忙清理垃圾";

    private const string 遗物1 = "解压垃圾";
    private const string 遗物2 = "垃圾毁灭者";
    private const float 回血比例 = 0.2f;

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "清理垃圾也挺好玩——获得 解压垃圾",
                "问普兰要一些炸药——获得 垃圾毁灭者",
                "不管，找个地方睡懒觉——回复20%血量",
            },
            new UnityAction<int>[]
            {
                _ => On拿遗物(遗物1),
                _ => On拿遗物(遗物2),
                _ => On睡觉(),
            });
    }

    private static void On拿遗物(string relicName)
    {
        EventEffects.GrantRelic(relicName);
        EventPanel.Close();
    }

    private static void On睡觉()
    {
        EventEffects.HealPercent(回血比例);
        EventPanel.Close();
    }
}
