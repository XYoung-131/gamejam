using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  这个文件是"饮料（药水）的配置表"
//
//  PotionData 是一个 ScriptableObject（可在 Unity 编辑器里创建的资产文件），
//  项目里 ScriptableObjects/饮料/ 文件夹下的每一个 .asset 文件，
//  比如"耐力饮料.asset""打嗝饮料.asset"，都是一瓶饮料。
//
//  它只存"静态配置"（名字、稀有度、效果），不存战斗中的临时状态。
//  真正喝下这瓶饮料时调用 Resolve() 结算效果。
//
//  ============================================================
//                       一瓶饮料的结算流程
//  ============================================================
//
//  Resolve(user, usedCountThisBattle) 依次做四件事：
//
//    第 1 步  effects 里每一条效果依次结算
//             （回复耐力/思绪、治疗、护盾、击退相邻、挂 buff、清除负面、免费技能）
//
//    第 2 步  如果本场战斗累计使用次数 ≥ penaltyAfterUses，结算副作用
//             （扣光耐力思绪 + 挂眩晕 + 登记战斗结束奖励遗物）
//             ⚠ 顺序很重要：策划写的是"先给 8 耐力 8 思绪，再扣光"，
//               所以副作用必须排在第 1 步之后。
//
//    第 3 步  OnPotionResolved 钩子（子类覆写，基类什么都不做）
//
//    第 4 步  广播 "PotionUsed" 事件（UI / 音效从这里接）
//
//  usedCountThisBattle 由调用方（PlayerUnit.UsePotion）传进来，
//  不能存在 PotionData 里 —— 它是 ScriptableObject 资产，全场景共享一份，
//  存运行时状态会串味。
//
//  数据来源：飞书多维表格《饮料（药水）及效果一览》
//  逐瓶实现：Assets/Game/Scripts/Progression/Potions/ 下每瓶饮料一个文件
// ============================================================

/// <summary>
/// 药水稀有度——对应策划表里的"稀有度"列（表里一共三档：普通 / 稀有 / 特殊）
/// 普通：常规补给，效果直白（耐力 / 提神 / 混合 / 劣质混合 / 快走 / 打嗝 / 硬化）
/// 稀有：数值更好的补给（青柠 / 蓝莓 / 橘子 / 桃子）
/// 特殊：带隐藏机制的药水（劣质混合饮料）
///
/// ⚠ 新成员一律追加在末尾：Unity 按成员序号序列化，
///   插队会让已有资产串号（劣质混合饮料.asset 里存的就是 rarity: 1 = 特殊，
///   如果把"稀有"插在"特殊"前面，它会变成"稀有"）。
/// </summary>
public enum PotionRarity
{
    普通,
    特殊,
    稀有
}

/// <summary>
/// 药水的效果类型——一瓶饮料可以同时带多个效果
/// （例："混合饮料" = 回复耐力 + 回复思绪 两条）
/// </summary>
public enum PotionEffectType
{
    回复耐力,       // 加行动资源（PlayerUnit 的耐力，移动和出牌共用）
    回复思绪,       // 加思绪（PlayerUnit 的思绪）
    治疗,           // 回血（调用 Unit.Heal）
    获得护盾,       // 加护盾（调用 Unit.AddShield）
    免费使用技能,   // 不消耗资源地直接放出一次指定技能
    击退相邻目标,   // 把相邻格的目标往外推开
    施加Buff,       // 给自身或敌方挂 buff（层数取 value，见 GetBuffStacks）
    清除负面状态,   // 移除自身所有负面 buff
    恢复技能使用次数, // 把槽位里所有技能的使用次数补满（青柠饮料）——追加在末尾，别插队
}

/// <summary>
/// 药水效果的作用对象
/// </summary>
public enum PotionTargetType
{
    自身,        // 喝饮料的人自己
    相邻目标,    // 喝饮料的人周围 1 格内的所有单位
    全场敌人     // 地图上所有敌方单位
}

/// <summary>
/// 一条药水效果——参考 BuffEffect 的写法，
/// 一条效果 = "对谁 + 做什么 + 数值多少"
/// </summary>
[System.Serializable]
public class PotionEffect
{
    [Header("效果类型")]
    public PotionEffectType effectType;

    [Header("作用对象")]
    public PotionTargetType targetType = PotionTargetType.自身;

    [Header("数值（回复量 / 护盾量 / 击退格数 / 施加Buff 时的层数）")]
    public int value;

    [Header("施加 Buff 时填（effectType = 施加Buff）")]
    public BuffData buff;

    [Header("免费使用技能时填（effectType = 免费使用技能）")]
    public CardData skill;
}

[CreateAssetMenu(fileName = "NewPotion", menuName = "Game/Potion Data")]
public class PotionData : ScriptableObject
{
    [Header("基础信息")]
    public string potionName;                       // 饮料名称，如"耐力饮料"
    [TextArea] public string description;           // 饮料描述（UI 上显示的说明文字）
    public Sprite icon;                             // 图标

    [Header("分类")]
    public PotionRarity rarity = PotionRarity.普通;  // 稀有度

    [Header("使用限制")]
    [Tooltip("每场战斗能喝几瓶。0 或负数 = 不限次数")]
    public int maxUsesPerBattle = 0;

    [Header("效果（可多条）")]
    public List<PotionEffect> effects = new List<PotionEffect>();

    // ========================================================
    //     战斗内使用次数惩罚（"劣质混合饮料"专用机制）
    // ========================================================

    [Header("战斗内使用次数惩罚")]
    [Tooltip("同一场战斗内累计使用达到这个次数后触发惩罚。0 或负数 = 没有惩罚机制")]
    public int penaltyAfterUses = 0;

    [Tooltip("触发惩罚时，是否把耐力和思绪全部扣光")]
    public bool penaltyDrainResources = true;

    [Tooltip("触发惩罚时附加的负面 buff（如眩晕 1 回合）")]
    public BuffData penaltyBuff;

    [Tooltip("携带这瓶饮料时，战斗结束后额外发放的遗物（如 饮料成瘾）")]
    public RelicData rewardRelic;

    [Header("内部备注")]
    [TextArea] public string devNotes;   // 策划备注，括号里玩家看不到的内容写这里

    // ============================================================
    //                          查询
    // ============================================================

    /// <summary>
    /// 这瓶饮料是不是"特殊"稀有度
    /// </summary>
    public bool IsSpecial => rarity == PotionRarity.特殊;

    /// <summary>
    /// 这瓶饮料是不是"稀有"档（青柠 / 蓝莓 / 橘子 / 桃子）
    /// </summary>
    public bool IsRare => rarity == PotionRarity.稀有;

    /// <summary>
    /// 有没有次数惩罚机制
    /// </summary>
    public bool HasPenalty => penaltyAfterUses > 0;

    /// <summary>
    /// 本场战斗已经喝了 usedCountThisBattle 瓶时，是否触发惩罚。
    /// （"劣质混合饮料"填 penaltyAfterUses = 3，第 3 瓶触发）
    /// </summary>
    public bool ShouldTriggerPenalty(int usedCountThisBattle)
    {
        return HasPenalty && usedCountThisBattle >= penaltyAfterUses;
    }

    /// <summary>
    /// 这瓶饮料有没有"每场战斗只能喝 N 瓶"的限制（maxUsesPerBattle &gt; 0 时才有限制）。
    /// </summary>
    public bool HasBattleUseLimit => maxUsesPerBattle > 0;

    /// <summary>
    /// 本场战斗已经喝了 usedCountThisBattle 瓶时，还能不能再喝一瓶。
    /// 由 PlayerUnit.UsePotion 在扣道具之前调用，避免"喝了但没效果"。
    /// </summary>
    public bool CanUseThisBattle(int usedCountThisBattle)
    {
        return !HasBattleUseLimit || usedCountThisBattle < maxUsesPerBattle;
    }

    // ============================================================
    //                          结算
    // ============================================================

    /// <summary>
    /// 喝下一瓶饮料（不带战斗内计数）——简单场景 / 编辑器测试用
    /// </summary>
    public bool Resolve(Unit user)
    {
        return Resolve(user, 1);
    }

    /// <summary>
    /// 喝下一瓶饮料。
    ///
    /// 流程：
    /// 1. 依次结算 effects 里的每一条效果（回复耐力/思绪、击退、挂 buff……）；
    /// 2. 如果本场战斗累计使用次数达到 penaltyAfterUses，再结算惩罚
    ///    （扣光耐力思绪 + 挂眩晕 buff + 登记战斗结束奖励）；
    /// 3. 调 OnPotionResolved 钩子（子类收尾）；
    /// 4. 广播 "PotionUsed" 事件，UI 和音效从这里接。
    ///
    /// 注意：第 2 步放在效果之后，是因为"劣质混合饮料"第 3 瓶
    /// 是先给 8 耐力 8 思绪，再被扣光。
    /// </summary>
    /// <param name="user">喝饮料的人（玩家单位）</param>
    /// <param name="usedCountThisBattle">本场战斗累计使用次数（含这一次）</param>
    public bool Resolve(Unit user, int usedCountThisBattle)
    {
        if (user == null) return false;

        // ---------- 第 1 步：结算所有正面效果 ----------
        foreach (var effect in effects)
        {
            if (effect == null) continue;   // 跳过没配置的空条目
            ApplyEffect(effect, user);
        }

        // ---------- 第 2 步：结算次数惩罚 ----------
        if (ShouldTriggerPenalty(usedCountThisBattle))
        {
            ApplyPenalty(user);
        }

        // ---------- 第 3 步：收尾钩子（子类覆写用） ----------
        OnPotionResolved(user, usedCountThisBattle);

        // ---------- 第 4 步：广播事件 ----------
        EventManager.Trigger("PotionUsed", this, user);
        return true;
    }

    /// <summary>
    /// 一瓶饮料结算完成后的钩子。基类什么都不做，
    /// 留给具体药水（如"劣质混合饮料"）补自己的收尾日志 / 特殊表现。
    /// 此时效果和惩罚都已结算完毕。
    /// </summary>
    /// <param name="user">喝饮料的人</param>
    /// <param name="usedCountThisBattle">本场战斗累计使用次数（含这一次）</param>
    protected virtual void OnPotionResolved(Unit user, int usedCountThisBattle) { }

    /// <summary>
    /// 结算单条效果
    /// </summary>
    protected virtual void ApplyEffect(PotionEffect effect, Unit user)
    {
        switch (effect.effectType)
        {
            // ---- 回复类：只有玩家单位有耐力和思绪 ----
            case PotionEffectType.回复耐力:
                if (user is PlayerUnit staminaTarget)
                    staminaTarget.RestoreStamina(effect.value);
                break;

            case PotionEffectType.回复思绪:
                if (user is PlayerUnit thoughtTarget)
                    thoughtTarget.RestoreThought(effect.value);
                break;

            // ---- 生命 / 护盾 ----
            case PotionEffectType.治疗:
                user.Heal(effect.value);
                Debug.Log($"{user.data.unitName} 喝下【{potionName}】，恢复 {effect.value} 点生命");
                break;

            case PotionEffectType.获得护盾:
                user.AddShield(effect.value);
                Debug.Log($"{user.data.unitName} 喝下【{potionName}】，获得 {effect.value} 点护盾");
                break;

            // ---- 控制 / 位移 ----
            case PotionEffectType.击退相邻目标:
                PushNeighborsAway(user, effect.value);
                break;

            // ---- Buff ----
            case PotionEffectType.施加Buff:
                ApplyBuffEffect(effect, user);
                break;

            case PotionEffectType.清除负面状态:
                user.RemoveAllNegativeBuffs();
                Debug.Log($"{user.data.unitName} 喝下【{potionName}】，清除了所有负面状态");
                break;

            // ---- 恢复技能使用次数（青柠饮料）----
            case PotionEffectType.恢复技能使用次数:
                RestoreSkillUses(user);
                break;

            // ---- 免费使用技能（"快走饮料"：免费使用 1 次"快走"） ----
            case PotionEffectType.免费使用技能:
                GrantFreeSkill(effect, user);
                break;
        }
    }

    /// <summary>
    /// 恢复所有技能的使用次数（青柠饮料）。
    /// 只有玩家有卡牌系统，敌人喝不出效果。
    /// </summary>
    protected virtual void RestoreSkillUses(Unit user)
    {
        int restored = 0;

        PlayerUnit playerUnit = user as PlayerUnit;
        if (playerUnit != null)
        {
            PlayerCardSystem cardSystem = playerUnit.GetComponent<PlayerCardSystem>();
            if (cardSystem != null)
                restored = cardSystem.RestoreAllSkillUses();
        }

        Debug.Log($"{user.data.unitName} 喝下【{potionName}】，补满了 {restored} 个技能的使用次数");
    }

    /// <summary>
    /// 发放"免费使用技能"次数（"快走饮料" = 免费使用 1 次"快走"）。
    ///
    /// 实现方式：不立刻自动放技能——位移类技能需要玩家自己挑落点，
    /// 所以改成往玩家卡牌系统里塞一张"免费卡"：不占技能槽位、不用灌注、
    /// 点下去不花思绪和耐力，用完这一次就消失。
    /// 出牌时的射程 / 目标 / 视线校验和正常出牌共用同一套。
    ///
    /// 次数取 effect.value（没填按 1 次算）。
    /// </summary>
    protected virtual void GrantFreeSkill(PotionEffect effect, Unit user)
    {
        if (effect.skill == null)
        {
            Debug.LogWarning($"【{potionName}】配了「免费使用技能」但没指定技能，本条效果落空");
            return;
        }

        int uses = effect.value > 0 ? effect.value : 1;

        // 只有玩家有卡牌系统（这几瓶都是玩家药水；敌人喝不出效果）
        PlayerUnit playerUnit = user as PlayerUnit;
        if (playerUnit != null)
        {
            PlayerCardSystem cardSystem = playerUnit.GetComponent<PlayerCardSystem>();
            if (cardSystem != null)
                cardSystem.GrantFreeCard(effect.skill, uses, potionName);
        }

        // 无论有没有卡牌系统都广播一次，UI / 音效 / 飘字从这里接
        EventManager.Trigger("FreeSkillGranted", user, effect.skill);
        Debug.Log($"{user.data.unitName} 喝下【{potionName}】，获得 {uses} 次免费使用【{effect.skill.cardName}】");
    }

    /// <summary>
    /// 把喝饮料的人周围 1 格内的所有单位往外推开 distance 格
    /// （"打嗝饮料"：将所有相邻目标推远 1 格）
    /// </summary>
    protected virtual void PushNeighborsAway(Unit user, int distance)
    {
        if (HexGrid.Instance == null || distance <= 0) return;

        HexCoord center = user.GetCoord();

        // GetAllNeighbors 拿到上下左右 6 个相邻格
        foreach (var neighbor in center.GetAllNeighbors())
        {
            HexCell cell = HexGrid.Instance.GetCell(neighbor);
            if (cell == null || cell.occupyUnit == null) continue;

            Unit pushed = cell.occupyUnit;
            if (pushed == user) continue;

            // PushBack(fromDirection) 内部算的是"远离 fromDirection"，
            // 所以把喝饮料的人自己当参照点，目标就会朝外飞
            pushed.PushBack(center, distance);
        }

        EventManager.Trigger("PotionPushed", user, distance);
    }

    /// <summary>
    /// 挂 buff。按 effect.targetType 分发：
    ///   自身     → 只挂给喝饮料的人自己
    ///   相邻目标 → 挂给喝饮料的人周围 1 格内的所有单位（不含自己）
    ///   全场敌人 → 挂给地图上所有活着的敌方单位
    ///
    /// 层数一律取 effect.value（见 GetBuffStacks）——
    /// "桃子饮料"是 3 层轻盈、"蓝莓饮料"是 3 层开悟，
    /// 以前这里调 ApplyBuff 用默认 1 层，这两瓶实际只挂上 1 层。
    ///
    /// 注意：只挑"还活着的"单位挂——Unity 的 Destroy 延迟到帧末执行，
    /// 刚被打死的单位对象还在场上，不判活会给尸体挂 buff。
    /// </summary>
    protected virtual void ApplyBuffEffect(PotionEffect effect, Unit user)
    {
        if (effect.buff == null) return;

        int stacks = GetBuffStacks(effect);

        switch (effect.targetType)
        {
            case PotionTargetType.全场敌人:
                foreach (var enemy in FindObjectsOfType<EnemyUnit>())
                {
                    if (enemy == null || enemy.IsDead) continue;
                    if (enemy.GetCurrentHealth() > 0)
                        enemy.ApplyBuff(effect.buff, stacks);
                }
                break;

            case PotionTargetType.相邻目标:
                ApplyBuffToNeighbors(user, effect.buff, stacks);
                break;

            default:   // 自身
                user.ApplyBuff(effect.buff, stacks);
                break;
        }
    }

    /// <summary>
    /// 本条效果要挂几层 buff。
    /// value 是"层数"，取自策划表里的"获得 N 层 XX"；
    /// 没填（&lt;= 0）时按 1 层兜底 —— 策划只写"挂个 XX"时语义就是 1 层。
    ///
    /// 实际能不能叠到 N 层还取决于 BuffData 自己的 canStack / maxStack：
    /// 轻盈、开悟 两个占位 buff 已按 canStack = true / maxStack = 99 配好。
    /// </summary>
    protected virtual int GetBuffStacks(PotionEffect effect)
    {
        return effect.value > 0 ? effect.value : 1;
    }

    /// <summary>
    /// 给 user 周围 1 格内的所有单位挂 stacks 层 buff（不含 user 自己）。
    /// 相邻判定用六边形距离 == 1，和"打嗝饮料"的击退判定是同一套标准，
    /// 保证"相邻"在 buff 和击退两处含义一致。
    /// </summary>
    protected virtual void ApplyBuffToNeighbors(Unit user, BuffData buff, int stacks)
    {
        if (HexGrid.Instance == null) return;

        HexCoord center = user.GetCoord();

        foreach (var unit in FindObjectsOfType<Unit>())
        {
            if (unit == null || unit == user) continue;
            if (unit.IsDead || unit.GetCurrentHealth() <= 0) continue;
            if (center.Distance(unit.GetCoord()) == 1)
                unit.ApplyBuff(buff, stacks);
        }
    }

    /// <summary>
    /// 结算次数惩罚：扣光耐力思绪 + 挂眩晕类负面 buff + 登记战斗结束奖励遗物。
    ///
    /// 由 ShouldTriggerPenalty 判定后才调用，所以这里不用再判断次数。
    /// </summary>
    protected virtual void ApplyPenalty(Unit user)
    {
        if (penaltyDrainResources && user is PlayerUnit player)
        {
            player.DrainAllResources();
        }

        if (penaltyBuff != null)
        {
            user.ApplyBuff(penaltyBuff);
        }

        // 惩罚附带的"战斗结束奖励"（劣质混合饮料 → 饮料成瘾）。
        // 这里只登记，不立即发放——策划写的是"战斗结束掉落"。
        // 真正发给玩家由 LootSystem 在战斗胜利时统一结算。
        if (rewardRelic != null)
            LootSystem.QueueBattleEndRelic(rewardRelic);

        Debug.Log($"{user.data.unitName} 因为连续喝【{potionName}】触发了副作用");
        EventManager.Trigger("PotionPenaltyTriggered", this, user);
    }
}
