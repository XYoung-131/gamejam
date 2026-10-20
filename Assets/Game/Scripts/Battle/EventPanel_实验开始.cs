using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：实验开始
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record recvycXnhEb4Ip（2026-10-20 读取）：
//    名称   = 实验开始
//    描述   = "勒忒，实验要开始了"研究员熟悉的声音响起，
//             你走出自己那熟悉却陌生的房间，准备战斗
//    选项 1 = 迎接第一场战斗（选项 2/3 为空 → 只显示一个槽）
//  调用 EventPanel_ExperimentStart.Show() 打开本事件界面。
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_实验开始
{
    public static void Show()
    {
        EventPanel.Show(
            "实验开始",
            "”勒忒，实验要开始了“研究员熟悉的声音响起，你走出自己那熟悉却陌生的房间，准备战斗",
            new[] { "迎接第一场战斗" },
            new UnityAction<int>[]
            {
                index =>
                {
                    // 关闭后由本局流程进入第一关。
                    EventPanel.Close();
                },
            });
    }
}
