using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  这个文件是"饮料（药水）的配置表"
//
//  PotionData 是一个 ScriptableObject（可在 Unity 编辑器里创建的资产文件），
//  项目里 ScriptableObjects/药水/ 文件夹下的每一个 .asset 文件，
//  比如"耐力饮料.asset""打嗝饮料.asset"，都是一瓶饮料。
//
//  它只存"静态配置"（名字、稀有度、效果），不存战斗中的临时状态。
//  真正喝下这瓶饮料时调用 Resolve() 结算效果。
//
//  数据来源：飞书多维表格《饮料（药水）及效果一览》
// ============================================================

/// <summary>
/// 药水稀有度——对应策划表里的"稀有度"列
/// 普通：常规掉落，效果直白
/// 特殊：带隐藏机制的稀有药水（如劣质混合饮料）
/// </summary>
public enum PotionRarity
{
    普通 = 0,
    稀有 = 1,
    特殊 = 2
}

/// <summary>
/// 药水的效果类型——一瓶饮料可以同时带多个效果
/// （例："混合饮料" = 回复耐力 + 回复思绪 两条）
/// </summary>
public enum PotionEffectType
{
    回复耐力 = 0,
    回复思绪 = 1,
    治疗 = 2,
    获得护盾 = 3,
    免费使用技能 = 4,
    击退相邻目标 = 5,
    施加Buff = 6,
    清除负面状态 = 7,
    恢复所有技能使用次数 = 8
}

public enum PotionPenaltyMode
{
    达到次数后每次触发 = 0,
    仅指定次数触发 = 1
}

/// <summary>配置战后奖励的资格；实际发放由战斗/掉落系统负责。</summary>
public enum PotionRewardCondition
{
    使用后 = 0,
    触发惩罚后 = 1
}

/// <summary>
/// 药水效果的作用对象
/// </summary>
public enum PotionTargetType
{
    自身 = 0,
    相邻目标 = 1,
    全场敌人 = 2
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

    [Header("数值（回复量 / 护盾量 / 击退格数）")]
    public int value;

    [Header("施加 Buff 时填（effectType = 施加Buff）")]
    public BuffData buff;

    [Min(1)] public int buffStacks = 1;
    [Tooltip("配置所需的 Buff 名称，便于补齐缺失引用；不会按名字自动生成效果。")]
    public string requiredBuffName;

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

    public PotionPenaltyMode penaltyMode;

    [Tooltip("触发惩罚时，是否把耐力和思绪全部扣光")]
    public bool penaltyDrainResources = true;

    [Tooltip("触发惩罚时附加的负面 buff（如眩晕 1 回合）")]
    public BuffData penaltyBuff;

    [Tooltip("符合 rewardCondition 后，战斗结束时掉落的遗物。需由战斗/掉落系统接入。")]
    public RelicData rewardRelic;

    public PotionRewardCondition rewardCondition;
    [Tooltip("缺少遗物资产时记录设计名称；不能替代实际遗物引用。")]
    public string rewardRelicName;

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
    /// 有没有次数惩罚机制
    /// </summary>
    public bool HasPenalty => penaltyAfterUses > 0;

    /// <summary>
    /// 本场战斗已经喝了 usedCountThisBattle 瓶时，是否触发惩罚。
    /// （"劣质混合饮料"填 penaltyAfterUses = 3，第 3 瓶触发）
    /// </summary>
    public bool ShouldTriggerPenalty(int usedCountThisBattle)
    {
        if (!HasPenalty) return false;
        return penaltyMode == PotionPenaltyMode.仅指定次数触发
            ? usedCountThisBattle == penaltyAfterUses
            : usedCountThisBattle >= penaltyAfterUses;
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
    ///    （扣光耐力思绪 + 挂眩晕 buff）；
    /// 3. 广播 "PotionUsed" 事件，UI 和音效从这里接。
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

        // ---------- 第 3 步：广播事件 ----------
        EventManager.Trigger("PotionUsed", this, user);
        return true;
    }

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

            // ---- 免费使用技能 ----
            case PotionEffectType.免费使用技能:
                // TODO：等卡牌系统开放"不消耗资源直接释放"的接口后接进来。
                // 目前只广播事件，由 PlayerCardSystem 侧监听处理。
                EventManager.Trigger("FreeSkillGranted", user, effect.skill);
                Debug.Log($"{user.data.unitName} 喝下【{potionName}】，获得 1 次免费使用【" +
                          $"{(effect.skill != null ? effect.skill.cardName : "未指定技能")}】");
                break;

            case PotionEffectType.恢复所有技能使用次数:
                RestoreSkillUses(user);
                break;
        }
    }

    /// <summary>恢复当前仍在槽位中的有限次数技能；已被卡牌系统移除的技能不凭空重建。</summary>
    protected virtual void RestoreSkillUses(Unit user)
    {
        var cards = user.GetComponent<PlayerCardSystem>();
        if (cards == null) return;
        foreach (var slot in cards.Slots)
        {
            if (slot != null && !slot.IsEmpty && slot.card.maxUses > 0)
                slot.remainingUses = slot.card.maxUses;
        }
        EventManager.Trigger("SlotChanged", cards);
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
    /// 挂 buff：自身目标挂给自己，全场敌人目标挂给地图上所有敌人
    /// </summary>
    protected virtual void ApplyBuffEffect(PotionEffect effect, Unit user)
    {
        if (effect.buff == null) return;

        if (effect.targetType == PotionTargetType.全场敌人)
        {
            // 找出地图上所有还活着的敌方单位
            foreach (var enemy in FindObjectsOfType<EnemyUnit>())
            {
                if (enemy.GetCurrentHealth() > 0)
                    enemy.ApplyBuff(effect.buff, Mathf.Max(1, effect.buffStacks));
            }
        }
        else
        {
            // 自身 / 相邻目标：MVP 先简化成给自己
            user.ApplyBuff(effect.buff, Mathf.Max(1, effect.buffStacks));
        }
    }

    /// <summary>
    /// 结算次数惩罚：扣光耐力思绪 + 挂眩晕类负面 buff
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

        Debug.Log($"{user.data.unitName} 因为连续喝【{potionName}】触发了副作用");
        EventManager.Trigger("PotionPenaltyTriggered", this, user);
    }
}
