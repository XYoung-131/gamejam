using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════
//  具体事件：自助售卖机
//  命名约定：文件/类名固定为 EventPanel_xxx，xxx = 飞书
//  「游戏事件」表（tblrDPCwXMak8Jxn）里的「名称」字段。
//  数据来源：record reczz28OmZGJIdtN（2026-10-20 读取）：
//    名称   = 自助售卖机
//    描述   = 研究所的角落有一个已经生锈了的自助售卖机，大家都不知道它是何时出现在研究所的，用力拍一拍试一试吧
//    选项   = 拍一拍——减少（对应数量）点最大耐力上限，获得（对应遗物名） / 捡个饮料——获得1瓶随机饮料
//    补充描述 = 选择拍一拍后选项会变化，减少3/5/7点耐力上限，依次药水腰带/草席卷/黑鹅项圈，
//              玩家可以一直选择 拍一拍，也可以随时选择 离开。
//
//  三档连拍（本文件实现）：
//    第 1 次拍 → 耐力上限 -3，获得 药水腰带   → 选项文案换成第 2 档
//    第 2 次拍 → 耐力上限 -5，获得 草席卷     → 选项文案换成第 3 档
//    第 3 次拍 → 耐力上限 -7，获得 黑鹅项圈   → 事件结束（推进流程）
//    任何一次拍之前点「捡个饮料」= 拿 1 瓶随机饮料并直接离开事件。
//
//  实现要点：
//    · 换档不关流程：EventPanel.CloseWithoutCompleting() 只销毁界面，
//      立刻用新文案重新 Show，玩家看到的就是"同一个事件里选项变了"；
//    · 属性/物品改动一律走 PlayerRunState 的事件入口（会自动 Capture 快照），
//      否则改动活不过下一关的场景重载。
// ═══════════════════════════════════════════════════════════════
public static class EventPanel_自助售卖机
{
    private const string 事件名称 = "自助售卖机";
    private const string 事件描述 =
        "研究所的角落有一个已经生锈了的自助售卖机，大家都不知道它是何时出现在研究所的，用力拍一拍试一试吧";

    // 三档连拍：第 n 次拍 → 扣 耐力代价[n] 点最大耐力上限，获得 遗物名[n]
    private static readonly int[] 耐力代价 = { 3, 5, 7 };
    private static readonly string[] 遗物名 = { "药水腰带", "草席卷", "黑鹅项圈" };

    // 本局这个事件已经拍了几次（Show 时归零，第三次拍完归零）
    private static int 已拍次数;

    // 资产还没建时的占位遗物缓存：同一件不重复建，避免 dedupe 失效后玩家拿到两件
    private static readonly Dictionary<string, RelicData> 占位遗物 =
        new Dictionary<string, RelicData>();

    // ══════════════════════════════════════════════════════════
    //  入口
    // ══════════════════════════════════════════════════════════

    public static void Show()
    {
        已拍次数 = 0;
        预登记遗物名();
        Rebuild();
    }

    /// <summary>
    /// 三件遗物的资产可能还没建：先把名字登记进"物品名高亮表"，
    /// 这样选项文案里的 药水腰带 / 草席卷 / 黑鹅项圈 才会变黄 + 加 []。
    /// （必须在 EventPanel.Show 之前登记——面板一旦构建，选项文本就按当时的名字表排好了。）
    /// </summary>
    private static void 预登记遗物名()
    {
        foreach (string name in 遗物名)
        {
            if (EventItemLibrary.FindRelic(name) != null) continue;   // 有真资产，高亮表里本来就有
            EventPanel.RegisterItemName(name, 取占位遗物(name).description);
        }
    }

    /// <summary>按当前拍了几次重新出界面（档位选择 + 文案）</summary>
    private static void Rebuild()
    {
        if (已拍次数 >= 耐力代价.Length)
        {
            // 兜底：三档都拍完了还进来，就只剩"捡个饮料"离开
            EventPanel.Show(事件名称, 事件描述,
                new[] { "捡个饮料——获得1瓶随机饮料" },
                new UnityAction<int>[] { _ => On捡个饮料() });
            return;
        }

        string 拍一拍 = "拍一拍——减少" + 耐力代价[已拍次数] + "点最大耐力上限，获得" + 遗物名[已拍次数];

        EventPanel.Show(事件名称, 事件描述,
            new[] { 拍一拍, "捡个饮料——获得1瓶随机饮料" },
            new UnityAction<int>[]
            {
                _ => On拍一拍(),
                _ => On捡个饮料(),
            });
    }

    // ══════════════════════════════════════════════════════════
    //  选项 1：拍一拍
    // ══════════════════════════════════════════════════════════

    private static void On拍一拍()
    {
        if (已拍次数 >= 耐力代价.Length) { EventPanel.Close(); return; }

        int cost = 耐力代价[已拍次数];
        string name = 遗物名[已拍次数];
        PlayerUnit player = Object.FindObjectOfType<PlayerUnit>();

        if (已持有(name))
        {
            // 这件遗物已经拿到过了（战斗结算的思维碎片是从通用池抽的，能抽到这几件）。
            // 以前这里照样扣最大耐力上限，玩家等于白掉 3/5/7 点上限还什么都没拿到。
            // 现在只跳过这一档：不扣上限，继续往下一档走。
            Debug.LogWarning($"[自助售卖机] 已经持有【{name}】，这一档不再扣最大耐力上限（{cost} 点）");
        }
        else
        {
            // ① 扣最大耐力上限（少 3/5/7 点）
            PlayerRunState.ApplyMaxStaminaDelta(player, -cost);

            // ② 给对应遗物
            RelicData relic = 取遗物(name);
            if (relic != null) PlayerRunState.GrantRelic(player, relic);

            // ③ 长条上的遗物栏立刻刷新，让玩家看见新拿到的那件
            EventPanel.RefreshRelics();

            Debug.Log($"[自助售卖机] 第 {已拍次数 + 1} 次拍：最大耐力上限 -{cost}，获得【{name}】");
        }

        已拍次数++;

        if (已拍次数 >= 耐力代价.Length)
        {
            // 第三档拍完 = 事件结束，正常推进流程
            已拍次数 = 0;
            EventPanel.Close();
            return;
        }

        // 还有下一档：只关界面、不推进流程，按新文案重开同一个事件
        EventPanel.CloseWithoutCompleting();
        Rebuild();
    }

    /// <summary>这件遗物是否已经在玩家身上（无场上玩家时查本局快照）</summary>
    private static bool 已持有(string name)
    {
        RelicData relic = EventItemLibrary.FindRelic(name);
        if (relic == null) return false;

        PlayerUnit player = Object.FindObjectOfType<PlayerUnit>();
        if (player != null) return player.HasRelic(relic);
        return PlayerRunState.Relics.Contains(relic);
    }

    /// <summary>取对应遗物资产；资产没建就建一件一次性占位，并登记它的悬停说明</summary>
    private static RelicData 取遗物(string name)
    {
        RelicData relic = EventItemLibrary.FindRelic(name);
        if (relic != null) return relic;

        RelicData placeholder = 取占位遗物(name);
        // 选项文案里的物品名高亮走的是资产表，占位遗物不在表里，手动登记一个说明
        EventPanel.RegisterItemName(name, placeholder.description);
        return placeholder;
    }

    /// <summary>占位遗物缓存：同一件不重复建，避免 dedupe 失效后玩家拿到两件</summary>
    private static RelicData 取占位遗物(string name)
    {
        if (占位遗物.TryGetValue(name, out RelicData relic) && relic != null) return relic;

        relic = EventItemLibrary.CreatePlaceholderRelic(name, null);
        占位遗物[name] = relic;
        Debug.LogWarning($"[自助售卖机] 工程里没有遗物资产【{name}】，本次用占位遗物顶上。" +
                         "把真资产建到 ScriptableObjects/遗物/ 下并保持同名即可自动接管。");
        return relic;
    }

    // ══════════════════════════════════════════════════════════
    //  选项 2：捡个饮料（拿 1 瓶随机饮料后直接离开事件）
    // ══════════════════════════════════════════════════════════

    private static void On捡个饮料()
    {
        PotionData potion = EventItemLibrary.RandomPotion();
        PlayerUnit player = Object.FindObjectOfType<PlayerUnit>();

        if (potion == null)
        {
            Debug.LogWarning("[自助售卖机] 找不到任何药水资产，捡个饮料没有发放");
        }
        else if (!PlayerRunState.GrantPotion(player, potion))
        {
            int limit = player != null ? player.maxPotions : 0;
            Debug.LogWarning($"[自助售卖机] 背包已满（上限 {limit} 瓶），" +
                             $"捡到的【{potion.potionName}】没能带走");
        }
        else
        {
            Debug.Log($"[自助售卖机] 捡到 1 瓶饮料【{potion.potionName}】");
        }

        已拍次数 = 0;
        EventPanel.Close();   // 任何一次拍之前都能靠这个选项离开
    }
}
