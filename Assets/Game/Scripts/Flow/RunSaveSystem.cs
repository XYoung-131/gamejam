using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════
//  单存档：把"当前这一局"（关卡进度 + 玩家快照）序列化到本地文件
//
//  存档位置：Application.persistentDataPath/run_save.json
//  （Windows 下即 %userprofile%\AppData\LocalLow\<公司名>\<产品名>\）
//
//  存什么：
//   · RunProgression —— 章节 / 关卡序号 / 当前事件 / 剩余事件 / 精英选择 / 随机变体
//   · PlayerRunState —— 血量、各项上限与回复、遗物、药水、卡池、待补的技能选择次数
//
//  存什么相位：只有"可以接着打"的相位才算存档点（事件 / 战斗 / 休息处）。
//  主界面(Menu)、结算奖励(Reward)、通关(Victory)、失败(Defeat) 都不写档——
//  奖励界面自身没有独立 UI，读到 Reward 会变成黑屏。
//
//  ScriptableObject 引用（卡牌 / 遗物 / 药水）不能直接 JSON 化，
//  所以按名字存，读档时再按名字找回资产：
//   · 遗物 / 药水 → EventItemLibrary（工程扫描 + 掉落池）
//   · 卡牌 → Resources 的 RewardCardCatalog / StartingDeck，编辑器下再兜底扫全工程
// ═══════════════════════════════════════════════════════════════
public static class RunSaveSystem
{
    private const string FileName = "run_save.json";

    /// <summary>存档文件完整路径</summary>
    public static string SavePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

    /// <summary>是否存在存档（主界面"唤醒 / 回忆"按钮据此分支）</summary>
    public static bool HasSave { get { return File.Exists(SavePath); } }

    /// <summary>存档内容（JsonUtility 能序列化的字段：只放基础类型与数组）</summary>
    [System.Serializable]
    public class Data
    {
        // ---- RunProgression ----
        public int phase;                 // RunPhase 枚举值
        public int chapterIndex;
        public int battleIndex;
        public string eventId;
        public int[] eliteChoices;
        public string[] remainingEvents;
        public int variantIndex;          // 当前战斗已掷出的关卡变体（-1 = 还没掷）

        // ---- PlayerRunState ----
        public bool hasSnapshot;
        public int health;
        public int maxHealth;
        public int maxStamina;
        public int maxThought;
        public int staminaRegen;
        public int thoughtRegen;
        public int potionMisses;
        public bool hasPoolSnapshot;
        public string[] attackPool;
        public string[] defensePool;
        public string[] extraCards;
        public string[] relics;
        public string[] potions;
        public int bonusStamina;
        public int bonusThought;
        public int pendingExtraCardChoices;
    }

    // ============================================================
    //                          写档
    // ============================================================

    /// <summary>
    /// 把当前进度写进存档文件。
    /// 只在该写的时候写（相位是存档点且流程控制器存在），返回是否真的写了。
    /// </summary>
    public static bool Save()
    {
        RunFlowController flow = RunFlowController.Instance;
        if (flow == null || flow.Progression == null) return false;

        RunProgression p = flow.Progression;
        if (!IsCheckpoint(p.Phase)) return false;

        Data d = new Data
        {
            phase = (int)p.Phase,
            chapterIndex = p.ChapterIndex,
            battleIndex = p.BattleIndex,
            eventId = p.EventId,
            eliteChoices = p.GetEliteChoices(),
            remainingEvents = p.GetRemainingEvents(),
            variantIndex = p.SavedVariantIndex,

            hasSnapshot = PlayerRunState.HasSnapshot,
            health = PlayerRunState.Health,
            maxHealth = PlayerRunState.MaxHealth,
            maxStamina = PlayerRunState.MaxStamina,
            maxThought = PlayerRunState.MaxThought,
            staminaRegen = PlayerRunState.StaminaRegen,
            thoughtRegen = PlayerRunState.ThoughtRegen,
            potionMisses = PlayerRunState.PotionMisses,
            hasPoolSnapshot = PlayerRunState.HasPoolSnapshot,
            attackPool = CardNames(PlayerRunState.SavedAttackPool),
            defensePool = CardNames(PlayerRunState.SavedDefensePool),
            extraCards = CardNames(PlayerRunState.ExtraCards),
            relics = RelicNames(PlayerRunState.Relics),
            potions = PotionNames(PlayerRunState.Potions),
            bonusStamina = PlayerRunState.BonusStamina,
            bonusThought = PlayerRunState.BonusThought,
            pendingExtraCardChoices = PlayerRunState.PendingExtraCardChoices
        };

        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(d, true));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[RunSave] 写入存档失败：" + e.Message);
            return false;
        }
    }

    // ============================================================
    //                          读档
    // ============================================================

    /// <summary>
    /// 读取存档并把进度 / 玩家快照恢复进 RunFlowController 与 PlayerRunState。
    /// 读档成功后由调用方（RunFlowController.ContinueRun）接着把界面推出来。
    /// </summary>
    public static bool Load()
    {
        RunFlowController flow = RunFlowController.Instance;
        if (flow == null || !HasSave) return false;

        Data d;
        try
        {
            d = JsonUtility.FromJson<Data>(File.ReadAllText(SavePath));
        }
        catch (System.Exception e)
        {
            Debug.LogError("[RunSave] 读取存档失败：" + e.Message);
            return false;
        }
        if (d == null) return false;

        RunRouteData route = Resources.Load<RunRouteData>("RunRoute");
        if (route == null)
        {
            Debug.LogError("[RunSave] 找不到 Resources/RunRoute，无法读档");
            return false;
        }

        if (!flow.RestoreProgression(route, (RunPhase)d.phase, d.chapterIndex, d.battleIndex,
                d.eventId, d.eliteChoices, d.remainingEvents, d.variantIndex))
        {
            Debug.LogError("[RunSave] 存档与当前路线不匹配（路线改过？），读档失败");
            return false;
        }

        PlayerRunState.LoadSnapshot(
            d.hasSnapshot, d.health, d.maxHealth, d.maxStamina, d.maxThought,
            d.staminaRegen, d.thoughtRegen, d.potionMisses, d.hasPoolSnapshot,
            ResolveCards(d.attackPool), ResolveCards(d.defensePool), ResolveCards(d.extraCards),
            ResolveRelics(d.relics), ResolvePotions(d.potions),
            d.bonusStamina, d.bonusThought, d.pendingExtraCardChoices);

        return true;
    }

    /// <summary>删除存档文件（"唤醒"确认重开、通关 / 失败回到主界面时调用）</summary>
    public static void Delete()
    {
        if (!HasSave) return;
        try
        {
            File.Delete(SavePath);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[RunSave] 删除存档失败：" + e.Message);
        }
    }

    // ============================================================
    //                          内部工具
    // ============================================================

    /// <summary>只有"还能接着打"的相位才写存档</summary>
    private static bool IsCheckpoint(RunPhase phase)
    {
        return phase == RunPhase.Event || phase == RunPhase.Battle || phase == RunPhase.Rest;
    }

    private static string[] CardNames(IReadOnlyList<CardData> cards)
    {
        if (cards == null) return new string[0];
        List<string> names = new List<string>();
        foreach (CardData card in cards)
            if (card != null) names.Add(card.cardName);
        return names.ToArray();
    }

    private static string[] RelicNames(IReadOnlyList<RelicData> relics)
    {
        if (relics == null) return new string[0];
        List<string> names = new List<string>();
        foreach (RelicData relic in relics)
            if (relic != null) names.Add(relic.relicName);
        return names.ToArray();
    }

    private static string[] PotionNames(IReadOnlyList<PotionData> potions)
    {
        if (potions == null) return new string[0];
        List<string> names = new List<string>();
        foreach (PotionData potion in potions)
            if (potion != null) names.Add(potion.potionName);
        return names.ToArray();
    }

    private static List<CardData> ResolveCards(string[] names)
    {
        List<CardData> result = new List<CardData>();
        if (names == null) return result;
        foreach (string name in names)
        {
            CardData card = FindCard(name);
            if (card != null) result.Add(card);
            else Debug.LogWarning("[RunSave] 找不到卡牌资产：" + name + "（跳过）");
        }
        return result;
    }

    private static List<RelicData> ResolveRelics(string[] names)
    {
        List<RelicData> result = new List<RelicData>();
        if (names == null) return result;
        foreach (string name in names)
        {
            RelicData relic = EventItemLibrary.FindRelic(name);
            if (relic != null) result.Add(relic);
            else Debug.LogWarning("[RunSave] 找不到遗物资产：" + name + "（跳过）");
        }
        return result;
    }

    private static List<PotionData> ResolvePotions(string[] names)
    {
        List<PotionData> result = new List<PotionData>();
        if (names == null) return result;
        foreach (string name in names)
        {
            PotionData potion = EventItemLibrary.FindPotion(name);
            if (potion != null) result.Add(potion);
            else Debug.LogWarning("[RunSave] 找不到药水资产：" + name + "（跳过）");
        }
        return result;
    }

    /// <summary>
    /// 按卡名找回卡牌资产：
    /// ① 正式包里靠 Resources 的两个目录资产（开局牌库 + 奖励卡池，覆盖全部在用的牌）；
    /// ② 编辑器里再兜底扫一遍工程里所有 CardData（新加的临时牌也能读到）。
    /// </summary>
    private static CardData FindCard(string cardName)
    {
        if (string.IsNullOrEmpty(cardName)) return null;

        RewardCardCatalog catalog = Resources.Load<RewardCardCatalog>("RewardCardCatalog");
        if (catalog != null)
            foreach (CardData card in catalog.cards)
                if (card != null && card.cardName == cardName) return card;

        StartingDeckData deck = Resources.Load<StartingDeckData>("StartingDeck");
        if (deck != null)
        {
            foreach (CardData card in deck.fixedSkills)
                if (card != null && card.cardName == cardName) return card;
            foreach (CardData card in deck.attackPool)
                if (card != null && card.cardName == cardName) return card;
            foreach (CardData card in deck.defensePool)
                if (card != null && card.cardName == cardName) return card;
        }

#if UNITY_EDITOR
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:CardData"))
        {
            CardData card = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (card != null && card.cardName == cardName) return card;
        }
#endif
        return null;
    }
}
