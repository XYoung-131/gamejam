using UnityEngine;
using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：洗礼
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record recvycXwrqOteb（2026-10-20 读取）：
//    名称   = 洗礼
//    描述   = 路西法有一个烧毁的十字架，她说用这玩意施加祝福不如注射器好用，现在也已经作废了，送给你玩了
//    选项   = 拿着——减少2点血量，获得 焦黑十字架 / 不要——回复10%血量
//  调用 EventPanel_洗礼.Show() 打开本事件界面。
//
//  效果（2026-10-20 接通）：
//    选项 1 拿着 → 当前生命 -2（最少留 1 点），获得遗物「焦黑十字架」（关卡开始给自己上神圣）
//    选项 2 不要 → 回复最大生命的 10%
//  两个选项都通过 PlayerRunState 的事件入口落地，会立刻刷新存档快照，
//  改动才能带进下一关（下一关是重新生成玩家）。
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_洗礼
{
    private const string 事件名称 = "洗礼";
    private const string 事件描述 =
        "路西法有一个烧毁的十字架，她说用这玩意施加祝福不如注射器好用，现在也已经作废了，送给你玩了";

    private const string 遗物名 = "焦黑十字架";
    private const int 扣血 = 2;
    private const float 回血比例 = 0.1f;

    public static void Show()
    {
        EventPanel.Show(事件名称, 事件描述,
            new[]
            {
                "拿着——减少2点血量，获得 焦黑十字架",
                "不要——回复10%血量",
            },
            new UnityAction<int>[]
            {
                _ => On拿着(),
                _ => On不要(),
            });
    }

    private static void On拿着()
    {
        PlayerUnit player = Object.FindObjectOfType<PlayerUnit>();

        PlayerRunState.ApplyHealthDelta(player, -扣血);

        RelicData relic = EventItemLibrary.FindRelic(遗物名);
        if (relic != null)
        {
            PlayerRunState.GrantRelic(player, relic);
            EventPanel.RefreshRelics();
            Debug.Log($"[洗礼] 获得遗物【{遗物名}】，当前生命 -{扣血}");
        }
        else
        {
            Debug.LogWarning($"[洗礼] 找不到遗物资产【{遗物名}】（应在 ScriptableObjects/遗物/ 下），" +
                             "这次只扣了血，没有获得遗物");
        }

        EventPanel.Close();
    }

    private static void On不要()
    {
        PlayerUnit player = Object.FindObjectOfType<PlayerUnit>();
        PlayerRunState.HealPercent(player, 回血比例);
        Debug.Log($"[洗礼] 回复 {Mathf.RoundToInt(回血比例 * 100f)}% 最大生命");
        EventPanel.Close();
    }
}
