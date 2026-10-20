using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：奇怪的药剂
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28NeaP01KHq（2026-10-20 读取）：
//    名称   = 奇怪的药剂
//    描述   = 研究人员照例带来了饮料给你，但他们这次还带了一瓶陌生但又令你感到熟悉的药剂，让你从药剂和饮料中选一种
//    选项   = 尝尝药剂——获得 X药 / 饮料！我爱喝饮料——获得3瓶随机饮料
//  调用 EventPanel_奇怪的药剂.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 → 获得遗物「X药」
//    选项 2 → 获得 3 瓶随机饮料（背包满了就只拿到能装下的部分）
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_奇怪的药剂
{
    private const string 事件名称 = "奇怪的药剂";
    private const string 事件描述 =
        "研究人员照例带来了饮料给你，但他们这次还带了一瓶陌生但又令你感到熟悉的药剂，让你从药剂和饮料中选一种";

    private const string 遗物名 = "X药";
    private const int 随机饮料瓶数 = 3;

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "尝尝药剂——获得 X药",
                "饮料！我爱喝饮料——获得3瓶随机饮料",
            },
            new UnityAction<int>[]
            {
                _ =>
                {
                    EventEffects.GrantRelic(遗物名);
                    EventPanel.Close();
                },
                _ =>
                {
                    EventEffects.GrantRandomPotions(随机饮料瓶数);
                    EventPanel.Close();
                },
            });
    }
}
