using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 运行时 buff 实例——记录某个 buff 在单位身上的状态
/// （不是 ScriptableObject，只在内存里存在）
/// </summary>
[System.Serializable]
public class BuffInstance
{
    public BuffData data;              // 引用 buff 配置资产
    public int remainingDuration;      // 剩余持续回合（-1 = 永久）
    public int currentStacks;          // 当前层数
    public int turnsSinceLastStack;    // 距上次叠层过了几个回合（"中毒"用它判"3 回合没叠就清空"）

    /// <summary>
    /// 本回合刚挂上、还没经历过一次"回合结束"衰减。
    /// TickBuffDuration 靠它跳过第一次衰减——否则"持续 2 回合"的控制效果
    /// 会在挂上的那一回合就直接掉一层，实际只活 1 个回合；
    /// 靠层数计命的负面 buff 同理（层数 = 存活回合数）。
    /// </summary>
    public bool justApplied;
}

/// <summary>
/// 伤害类型——决定伤害如何被护盾/减伤处理（对应设计文档"伤害类型"一节）
/// </summary>
public enum DamageType
{
    物理,   // 最普通的伤害：先扣护盾(护甲)，再受减伤/易伤影响
    真实,   // 无视护甲(护盾)，但仍受减伤/易伤影响
    Buff    // 中毒/灼烧/流血等持续伤害：受护盾格挡，也受减伤/易伤影响
}

/// <summary>
/// 位移类型——文档里的三种位移动画
/// </summary>
public enum DisplacementType
{
    击退,   // 沿远离施法者的方向一格一格后退（撞到东西会停下并结算伤害）
    拉近,   // 沿朝向施法者的方向一格一格靠近（同上）
    传送    // 闪现到目标格，途中穿过什么都没有影响
}

/// <summary>
/// 单位基类——所有单位（玩家、敌人、NPC）共有的属性和行为
/// 继承 MonoBehaviour，挂在单位 GameObject 上
/// 
/// Buff 结算逻辑：
///   - 伤害类 buff：回合开始时触发效果（中毒/灼烧/流血扣血）
///   - 影响类 buff：获得即生效，属性值在 GetAttack / TakeDamage 等方法里实时反查
///   - 所有 buff 在回合结束时衰减：① 层数（按 BuffData.stackDecay）② 持续时间（-1 = 永久）
///   - 护盾：回合开始时清零（特殊类，独立管理）
/// </summary>
public abstract class Unit : MonoBehaviour
{
    [Header("配置数据")]
    [Tooltip("引用 ScriptableObjects/Units/ 下的单位数据资产")]
    public UnitData data;

    [Header("运行时状态")]
    [SerializeField] protected int currentHealth;    // 当前血量
    [SerializeField] protected int shield;          // 护盾（回合结束清零）
    [SerializeField] protected HexCoord currentCoord; // 当前所在六边形坐标

    [Header("位移碰撞伤害（在 Inspector 里按单位类型填写）")]
    [Tooltip("被位移时撞到障碍物/其他单位，自己受到的伤害（玩家=1，敌人=2）")]
    public int collisionSelfDamage = 2;
    [Tooltip("被位移时撞到障碍物/其他单位，被撞的一方受到的伤害（文档=2）")]
    public int collisionBlockerDamage = 2;
    [Tooltip("是否具有反弹效果：被撞的一方受伤，自己反转方向继续位移")]
    public bool reflectsDisplacement = false;

    // 位移期间的"延迟死亡"标记：位移过程中血量归零不立刻消失，
    // 等整段位移全部结算完再统一死亡（文档："位移期间死亡，等位移结束再消失"）
    protected bool isDisplacing = false;   // 是否正在被位移
    protected bool pendingDeath = false;   // 位移中已归零，等位移结束再死

    // 死亡标记：Unity 的 Destroy 延迟到帧末执行，Die() 之后对象还会被调用若干次。
    // 所有会改动状态的公开方法开头都要判它，防止"给已死单位挂 buff"、
    // "死亡事件发两遍"这类问题。
    protected bool isDead = false;

    /// <summary>是否已死亡（Die() 已触发、GameObject 等待销毁）</summary>
    public bool IsDead => isDead;

    // 当前身上的所有 buff
    [SerializeField] protected List<BuffInstance> activeBuffs = new List<BuffInstance>();

    [Header("Buff 结算设置")]
    [Tooltip("本回合刚获得的 buff，是否跳过本回合结束的第一次衰减。\n关掉 = 沿用旧行为（当回合就衰减）；打开 = 从下一回合开始衰减")]
    public bool skipDecayOnAppliedTurn = false;

    // 事件
    public event System.Action<Unit> OnUnitDied;
    public event System.Action<Unit, BuffData> OnBuffApplied;
    public event System.Action<Unit, BuffData> OnBuffRemoved;

    /// <summary>
    /// 初始化单位，从 UnitData 读取配置
    /// </summary>
    public virtual void Initialize(UnitData unitData, HexCoord startCoord)
    {
        data = unitData;
        currentHealth = unitData.maxHealth;
        shield = 0;
        currentCoord = startCoord;
        activeBuffs.Clear();
        isDead = false;        // 复用单位时重置死亡标记
        pendingDeath = false;
        isDisplacing = false;

        // 把自己注册到格子上
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(startCoord);
            if (cell != null)
            {
                // 同一格放两个单位时，occupyUnit 只会记住后一个，
                // 前一个会变成"看得见但谁都不能站/移不出去"的幽灵，这里至少喊一声。
                if (cell.occupyUnit != null && cell.occupyUnit != this)
                {
                    Debug.LogWarning($"格子上已经有 {cell.occupyUnit.name}，" +
                                     $"{unitData.unitName} 仍然被放在了 {startCoord}（两个单位叠在一格）");
                }

                cell.occupyUnit = this;
            }
        }
    }

    // ============================================================
    //                    受伤 / 治疗 / 护盾
    // ============================================================

    /// <summary>
    /// 受到伤害（带伤害类型）。
    /// 处理顺序（对应文档"伤害类型"）：
    /// 1. 免疫伤害 → 直接不受伤；
    /// 2. 固定减免（受到伤害固定减免 buff）；
    /// 3. 倍率增减（易伤 +25% / 减伤），所有小数向上取整（文档："所有数值向上取整，0.1 也算 1"）；
    /// 4. 扣护盾：真实伤害无视护甲(护盾)，物理/Buff 伤害会被护盾格挡；
    /// 5. 扣血。
    /// </summary>
    /// <param name="type">默认物理伤害，保持旧调用方式不变</param>
    public virtual void TakeDamage(int amount, DamageType type = DamageType.物理)
    {
        TakeDamage(amount, type, null);
    }

    /// <summary>
    /// 受到伤害（带伤害类型 + 伤害来源）。
    /// 带 source 的版本给这些场景用：反伤（打人者=source 反吃伤害）、
    /// 遗物"击杀目标时回复"、伤害归属统计。
    /// source 可以传 null（环境伤害 / 不明来源）。
    /// </summary>
    public virtual void TakeDamage(int amount, DamageType type, Unit source)
    {
        if (amount <= 0) return;
        if (isDead) return;   // 已死亡的单位不再结算任何伤害

        // 免疫伤害 → 直接返回（放在这里是为了连"受到伤害时"事件一起不触发）
        if (HasEffect(BuffEffectType.免疫伤害))
            return;

        // 固定减免 + 倍率增减（易伤 / 减伤），统一走共享算法
        amount = CalcIncomingDamage(amount);
        if (amount <= 0) return;

        // 扣护盾（真实伤害无视护甲）
        if (type != DamageType.真实 && shield > 0)
        {
            int absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;

            if (absorbed > 0)
                EventManager.Trigger("UnitShieldChanged", this);
        }

        // 扣血（统一走 ApplyHealthLoss，内部处理免疫死亡 + 位移延迟死亡 + 血条事件）
        ApplyHealthLoss(amount);

        // 已经死了就不再触发"受到伤害时"——否则一击致死时会重复触发反伤类效果
        if (!isDead)
            TriggerBuffs(BuffTrigger.受到伤害时);

        EventManager.Trigger("UnitDamaged", this);
        EventManager.Trigger("UnitDamagedBy", this, source);   // 带来源，遗物/反伤监听这个
    }

    /// <summary>
    /// 计算"最终要吃下的伤害"：免疫 → 固定减免 → 倍率增减（易伤 / 减伤）。
    /// 只做数值计算、不扣血，因此不会递归。
    ///
    /// 抽出来是为了让【卡牌/普通攻击】和【中毒/灼烧/流血等持续伤害】共用同一套减伤规则——
    /// DamageType.Buff 的定义明确写了"也受减伤/易伤影响"，而原先 TakeDamageInternal
    /// 直接"跳过 buff 计算"，导致中了易伤时持续伤害并不会增加，两边对不上。
    /// 顺带也让"无敌（免疫伤害）"能挡住持续伤害。
    /// </summary>
    /// <returns>实际应扣的伤害值（已向上取整，下限 0）</returns>
    protected int CalcIncomingDamage(int amount)
    {
        if (amount <= 0) return 0;
        if (HasEffect(BuffEffectType.免疫伤害)) return 0;

        // 受到伤害固定减免（殊死一搏 = 3）。不限触发时机，填"立即生效"或"受到伤害时"都算。
        float damageReduce = GetTotalEffectValue(BuffEffectType.受到伤害固定减免);
        amount = Mathf.Max(0, amount - Mathf.RoundToInt(damageReduce));

        // 受到伤害倍率（易伤 = 1.25，减伤 < 1），倍率型叠加规则见 GetTotalEffectValue
        float damageMultiplier = GetTotalEffectValue(BuffEffectType.受到伤害倍率);
        if (damageMultiplier <= 0) damageMultiplier = 1f;   // 没有该类型 buff 时返回 1，这里再兜底

        return Mathf.CeilToInt(amount * damageMultiplier);   // 向上取整（文档："0.1 也算 1"）
    }

    /// <summary>
    /// 统一的扣血入口：所有伤害/持续伤害最终都从这里扣血，
    /// 保证"免疫死亡"和"位移期间延迟死亡"两条规则只写一次，
    /// 也保证 UI 的"血量变了"事件只在这里发一次。
    /// </summary>
    protected virtual void ApplyHealthLoss(int amount)
    {
        if (amount <= 0) return;

        int before = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);

        bool shouldDie = currentHealth <= 0;

        // 免疫死亡 → 血量锁在 1，不死
        if (shouldDie && HasEffect(BuffEffectType.免疫死亡))
        {
            currentHealth = 1;
            shouldDie = false;
        }

        if (currentHealth != before)
            BroadcastHealthChanged();

        if (!shouldDie) return;

        // 正在被位移 → 先记为"待死亡"，等位移结算完再消失
        if (isDisplacing)
        {
            pendingDeath = true;
            return;
        }

        Die();
    }

    /// <summary>
    /// 清空护盾。
    /// 【全项目唯一权威时机 = 单位回合开始时】（由 OnTurnStart 调用）。
    ///
    /// 以前 OnTurnStart 和 OnTurnEnd 各清一次，净效果是本回合刚加的护盾
    /// 在回合结束就被抹掉，"格挡/硬化"这类护盾牌一点用都没有。
    /// </summary>
    public virtual void ClearShield()
    {
        if (isDead) return;
        if (shield == 0) return;

        shield = 0;

        EventManager.Trigger("UnitShieldChanged", this);
        EventManager.Trigger("UnitShieldCleared", this);
        BroadcastHealthChanged();
    }

    /// <summary>
    /// 【已废弃】回合结束清护盾。保留空实现只为兼容旧调用/旧预制体上的 UnityEvent。
    /// 不要在任何地方再调它——护盾已经在回合开始时清了。
    /// </summary>
    public virtual void ClearShieldOnTurnEnd()
    {
        // 故意留空：护盾的清算时机统一在 OnTurnStart → ClearShield()
    }

    /// <summary>
    /// 治疗。
    /// 判活：已死亡的单位不能再被治疗——否则会出现 isDead = true 但 currentHealth > 0
    /// 的"僵尸状态"（Destroy 延迟到帧末，复活类/治疗类调用方可能踩到）。
    /// </summary>
    public virtual void Heal(int amount)
    {
        if (isDead || amount <= 0) return;

        if (data == null)
        {
            Debug.LogWarning($"{name} 没有配置 UnitData，无法治疗");
            return;
        }

        int before = currentHealth;
        currentHealth = Mathf.Min(currentHealth + amount, data.maxHealth);

        // 满血时治疗不该刷新事件（否则 UI 会一直闪"回复"特效）
        if (currentHealth != before)
        {
            BroadcastHealthChanged();
            EventManager.Trigger("UnitHealed", this);
        }
    }

    /// <summary>
    /// 添加护盾。
    /// 注意：护盾在单位【回合开始时】清零，不会保留到下一个自己的回合。
    /// </summary>
    public virtual void AddShield(int amount)
    {
        if (isDead || amount <= 0) return;

        shield += amount;

        EventManager.Trigger("UnitShieldChanged", this);
        BroadcastHealthChanged();
    }

    /// <summary>
    /// 血量或护盾变化后统一广播一次。
    /// UI 的血条 / 护盾条只需要监听 "UnitHealthChanged" 一个事件，
    /// 不用再分别订阅 UnitDamaged / UnitHealed / UnitShieldChanged / UnitShieldCleared。
    /// </summary>
    protected void BroadcastHealthChanged()
    {
        EventManager.Trigger("UnitHealthChanged", this);
    }

    // ============================================================
    //                       Buff 系统
    // ============================================================

    /// <summary>
    /// 施加一个 buff
    /// </summary>
    /// <param name="buffData">buff 配置</param>
    /// <param name="stacks">本次施加的层数（默认 1；对精英怪 / BOSS 会自动减半）</param>
    public virtual void ApplyBuff(BuffData buffData, int stacks = 1)
    {
        if (isDead) return;   // 已死亡的单位不再接受任何 buff
        if (buffData == null) return;

        // 免疫负面 → 如果是负面 buff 直接跳过
        if (buffData.polarity == BuffPolarity.负面 && HasEffect(BuffEffectType.免疫负面))
            return;

        // "对敌人无效"（力竭 / 混乱）：目标不符合要求就挂不上。
        // 这一步以前缺失，导致 buff 上 onlyForPlayerUnit 勾了也不起作用。
        if (!buffData.CanApplyTo(this)) return;

        // 真实层数：残废 / 无力 明确写了"对精英怪和 BOSS 施加时层数减半，单次至少 1 点"。
        // 这一步以前也缺失，导致精英怪身上的残废层数没打折。
        int realStacks = buffData.CalcStacksOnApply(this, stacks);

        // 查找是否已有同类型 buff
        BuffInstance existing = activeBuffs.Find(b => b.data == buffData);

        if (existing != null)
        {
            // 已存在：叠加持续时间
            if (existing.remainingDuration != -1 && buffData.duration != -1)
                existing.remainingDuration += buffData.duration;

            // 可叠加层数（不超过 maxStack 上限）
            if (buffData.canStack)
            {
                existing.currentStacks = Mathf.Min(
                    existing.currentStacks + realStacks,
                    buffData.maxStack);
            }

            // 又叠上了 → 中毒的"连续未叠层"计数归零
            existing.turnsSinceLastStack = 0;
        }
        else
        {
            // 不存在：新增
            BuffInstance newBuff = new BuffInstance
            {
                data = buffData,
                remainingDuration = buffData.duration,
                currentStacks = realStacks,
                turnsSinceLastStack = 0,
                justApplied = true          // 标记"本回合刚挂上"，供 TickBuffDuration 跳过首次衰减
            };
            activeBuffs.Add(newBuff);

            // 立即生效效果（触发时机 = 立即生效）
            TriggerBuffEffects(newBuff, BuffTrigger.立即生效);
        }

        OnBuffApplied?.Invoke(this, buffData);
        EventManager.Trigger("UnitBuffApplied", this, buffData);

        // 层数可能变了（中毒 1→3 层），UI 靠这个刷新 buff 图标上的数字
        EventManager.Trigger("UnitBuffStacksChanged", this, buffData);
    }

    /// <summary>
    /// 施加一个"永久"buff。
    /// 和 ApplyBuff 的唯一区别：不管 BuffData 资产上的 duration 填了什么，
    /// 实际持续时间都会被锁成 -1（永久）。遗物这种"拿了就一直生效"的加成走这里，
    /// 就不会因为策划在 BuffData 上填了回合数而到期消失。
    /// </summary>
    public virtual void ApplyPermanentBuff(BuffData buffData)
    {
        if (buffData == null) return;

        ApplyBuff(buffData);

        // ApplyBuff 会把 duration 原样写进实例，这里再强制改成永久
        BuffInstance instance = activeBuffs.Find(b => b.data == buffData);
        if (instance != null)
            instance.remainingDuration = -1;
    }

    /// <summary>
    /// 移除一个 buff
    /// </summary>
    public virtual void RemoveBuff(BuffData buffData)
    {
        BuffInstance existing = activeBuffs.Find(b => b.data == buffData);
        if (existing != null)
        {
            activeBuffs.Remove(existing);
            OnBuffRemoved?.Invoke(this, buffData);
            EventManager.Trigger("UnitBuffRemoved", this, buffData);
        }
    }

    /// <summary>
    /// 检查是否有某个 buff（按 BuffData 引用）
    /// </summary>
    public virtual bool HasBuff(BuffData buffData)
    {
        return activeBuffs.Exists(b => b.data == buffData);
    }

    /// <summary>
    /// 检查是否有某种效果类型的 buff
    /// （比如"有没有加攻击力的 buff"）
    /// </summary>
    public virtual bool HasEffect(BuffEffectType effectType)
    {
        foreach (var buff in activeBuffs)
        {
            if (buff == null || buff.data == null || buff.data.effects == null) continue;

            foreach (var effect in buff.data.effects)
            {
                if (effect != null && effect.effectType == effectType)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 获取某种效果类型的总数值（叠加所有同类型 buff）——【不限触发时机】。
    ///
    /// 属性类加成（攻击伤害加成、受到伤害倍率等）统一用这个重载：
    /// 无论策划把效果填成"立即生效"还是"攻击时"/"受到伤害时"，都算数，
    /// 彻底避免"填错 trigger 就静默失效"（按错误时机查询永远返回 0）。
    ///
    /// 叠加规则按效果类型区分（分类见 BuffData.IsMultiplierType）：
    ///   · 加成型（力量 +1、无力 -1、殊死一搏 3）→ 数值直接累加，没有 buff 时返回 0；
    ///   · 倍率型（易伤 1.25、力竭 0.5、虚弱 1.25）→ 累加"偏离 1"的部分再乘回去，
    ///     即 最终倍率 = 1 + Σ(value - 1) × 层数，没有 buff 时返回 1。
    ///     （易伤 1.25 叠 2 层 = 1.5 倍，而不是被错算成 2.5 倍）
    /// </summary>
    public virtual float GetTotalEffectValue(BuffEffectType effectType)
    {
        float total = 0f;

        foreach (var buff in activeBuffs)
        {
            if (buff.data == null || buff.data.effects == null) continue;

            foreach (var effect in buff.data.effects)
            {
                if (effect.effectType != effectType) continue;

                if (BuffData.IsMultiplierType(effectType))
                {
                    // 倍率型：累加偏离 1 的部分（0.5 → -0.5，1.25 → +0.25）
                    total += (effect.value - 1f) * buff.currentStacks;
                }
                else
                {
                    // 加成型：数值直接累加
                    total += effect.value * buff.currentStacks;
                }
            }
        }

        if (BuffData.IsMultiplierType(effectType))
        {
            // 倍率型：最终倍率 = 1 + 偏移和，下限 0（力竭叠太多 → 资源完全不回复）
            return Mathf.Max(0f, 1f + total);
        }
        return total;
    }

    /// <summary>
    /// 获取某种效果类型的总数值——【只统计指定触发时机】。
    /// 留给"按回合/按事件触发"的效果（固定持续伤害等）用；
    /// 属性类加成请不要用这个重载，用不限时机的版本。
    /// </summary>
    public virtual float GetTotalEffectValue(BuffEffectType effectType, BuffTrigger trigger)
    {
        float total = 0f;
        foreach (var buff in activeBuffs)
        {
            if (buff.data == null || buff.data.effects == null) continue;

            foreach (var effect in buff.data.effects)
            {
                if (effect.effectType != effectType || effect.trigger != trigger) continue;

                if (BuffData.IsMultiplierType(effectType))
                    total += (effect.value - 1f) * buff.currentStacks;
                else
                    total += effect.value * buff.currentStacks;
            }
        }

        if (BuffData.IsMultiplierType(effectType))
            return Mathf.Max(0f, 1f + total);
        return total;
    }

    /// <summary>
    /// 触发指定时机的所有 buff 效果
    /// </summary>
    protected virtual void TriggerBuffs(BuffTrigger trigger)
    {
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = activeBuffs[i];
            TriggerBuffEffects(buff, trigger);
        }
    }

    /// <summary>
    /// 触发单个 buff 指定时机的效果
    /// </summary>
    protected virtual void TriggerBuffEffects(BuffInstance buff, BuffTrigger trigger)
    {
        if (buff == null || buff.data == null) return;

        foreach (var effect in buff.data.effects)
        {
            if (effect == null || effect.trigger != trigger) continue;

            switch (effect.effectType)
            {
                case BuffEffectType.固定持续伤害:
                    int dmg = Mathf.RoundToInt(effect.value * buff.currentStacks);
                    TakeDamageInternal(dmg);
                    break;

                case BuffEffectType.清除所有负面:
                    RemoveAllNegativeBuffs();
                    break;

                case BuffEffectType.清除所有正面:
                    RemoveAllPositiveBuffs();
                    break;

                case BuffEffectType.回合结束死亡:
                    // 在回合结束触发时直接死亡
                    if (trigger == BuffTrigger.回合结束)
                        Die();
                    break;

                // 属性类 buff 不在这里触发（在 GetAttack/TakeDamage 等方法里实时计算）
            }
        }
    }

    /// <summary>
    /// 移除所有负面 buff
    /// </summary>
    public virtual void RemoveAllNegativeBuffs()
    {
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            if (activeBuffs[i] == null || activeBuffs[i].data == null)
            {
                activeBuffs.RemoveAt(i);
                continue;
            }

            if (activeBuffs[i].data.polarity == BuffPolarity.负面)
                RemoveBuffInstanceAt(i, "UnitBuffRemoved");
        }
    }

    /// <summary>
    /// 移除所有正面 buff
    /// </summary>
    public virtual void RemoveAllPositiveBuffs()
    {
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            if (activeBuffs[i] == null || activeBuffs[i].data == null)
            {
                activeBuffs.RemoveAt(i);
                continue;
            }

            if (activeBuffs[i].data.polarity == BuffPolarity.正面)
                RemoveBuffInstanceAt(i, "UnitBuffRemoved");
        }
    }

    /// <summary>
    /// 按索引移除一个 buff 实例并广播事件。
    /// 抽出来是为了让"层数衰减到 0 被移除"和"持续时间到期被移除"走同一套清理逻辑
    /// （两者的区别只有事件名：前者叫 Expired）。
    /// </summary>
    protected void RemoveBuffInstanceAt(int index, string eventName)
    {
        if (index < 0 || index >= activeBuffs.Count) return;

        BuffInstance instance = activeBuffs[index];
        activeBuffs.RemoveAt(index);

        BuffData removed = instance != null ? instance.data : null;
        if (removed == null) return;

        OnBuffRemoved?.Invoke(this, removed);
        EventManager.Trigger(eventName, this, removed);
    }

    // ============================================================
    //                    回合生命周期（新结算逻辑）
    // ============================================================

    /// <summary>
    /// 回合开始时调用
    /// 新逻辑：只触发伤害类 buff 的回合开始效果（流血扣血等）
    /// 持续时间递减移到回合结束做
    /// </summary>
    public virtual void OnTurnStart()
    {
        if (isDead) return;   // 已死亡的单位不参与回合结算（TurnManager 已过滤，这里再兜一道）

        // 0. 护盾在下个回合开始时消失（文档规则）
        ClearShield();

        // 1. 触发"回合开始"效果（如持续伤害）
        TriggerBuffs(BuffTrigger.回合开始);
    }

    /// <summary>
    /// 回合结束时调用
    /// 新逻辑：
    ///   ① 触发回合结束效果
    ///   ② 所有 buff 持续时间 / 层数衰减，过期移除
    /// （护盾不在这里清——权威时机是 OnTurnStart，见 ClearShield 的注释）
    /// </summary>
    public virtual void OnTurnEnd()
    {
        if (isDead) return;   // 已死亡的单位不参与回合结算

        // ① 触发"回合结束"时机的 buff 效果
        TriggerBuffs(BuffTrigger.回合结束);

        // ② 层数 + 持续时间衰减，归零的移除
        TickBuffDuration();
    }

    /// <summary>
    /// 回合结束的 buff 衰减结算，两条线：
    ///   ① 层数衰减 —— 按 BuffData.stackDecay 走（策划案："负面没特殊标识就是回合结束层数减1"）；
    ///   ② 持续时间衰减 —— duration 减 1，减到 0 移除（-1 = 永久，不动）。
    /// 层数或持续时间任一归零 → 移除该 buff。
    ///
    /// 注意：对负面 buff 来说"层数"就是它的存活回合数
    ///（正因如此，"残废/无力对精英怪和 BOSS 层数减半"等价于持续时间减半）。
    /// 所以做 asset 时负面 buff 的 duration 一般留 -1，靠层数走完生命周期；
    /// 正面 buff 反之，靠 duration（-1 = 永久）。
    /// </summary>
    public virtual void TickBuffDuration()
    {
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = activeBuffs[i];

            // 脏数据（配置丢了）直接丢弃，避免后面 NRE
            if (buff == null || buff.data == null)
            {
                activeBuffs.RemoveAt(i);
                continue;
            }

            // ---------- ⓿ 本回合刚挂上的 buff 跳过第一次衰减 ----------
            // 只对"刚 new 出来的实例"生效（叠层不会重置这个标记），
            // 受 Unit.skipDecayOnAppliedTurn 开关控制，默认关闭 = 沿用旧行为。
            if (buff.justApplied)
            {
                buff.justApplied = false;
                if (skipDecayOnAppliedTurn) continue;
            }

            // ---------- ① 层数衰减 ----------
            bool expired = false;

            if (buff.data.ResolvedDecay == BuffStackDecay.未叠加则清空)
            {
                // 中毒：不是每回合掉层，而是"连续 N 回合没再叠层"就整体清空
                buff.turnsSinceLastStack++;
                expired = buff.data.ShouldClearForNoStack(buff.turnsSinceLastStack);
            }
            else
            {
                // 其余全部走统一规则：回合结束减1 / 减半（1 层时清零）/ 消失 / 不减少
                int newStacks = buff.data.CalcStacksAfterTurnEnd(buff.currentStacks);
                if (newStacks <= 0) expired = true;
                else buff.currentStacks = newStacks;
            }

            if (expired)
            {
                RemoveBuffInstanceAt(i, "UnitBuffExpired");
                continue;
            }

            // ---------- ② 持续时间衰减 ----------
            if (buff.remainingDuration == -1) continue;   // 永久 buff 不减时间

            buff.remainingDuration--;
            if (buff.remainingDuration <= 0)
                RemoveBuffInstanceAt(i, "UnitBuffExpired");
        }
    }

    /// <summary>
    /// 攻击时调用（攻击前触发）
    /// </summary>
    public virtual void OnAttack()
    {
        TriggerBuffs(BuffTrigger.攻击时);
    }

    /// <summary>
    /// 移动时调用
    /// </summary>
    public virtual void OnMove()
    {
        TriggerBuffs(BuffTrigger.移动时);
    }

    // ============================================================
    //                        死亡
    // ============================================================

    /// <summary>
    /// 死亡
    /// </summary>
    protected virtual void Die()
    {
        // 防重复：持续伤害打死 + 卡牌打死两条路可能先后到达。
        // 不拦的话 UnitDied 会发两遍（LootSystem 会掉两次战利品）、
        // 子类也会重复判"全员阵亡 / 全员胜利"。
        if (isDead) return;
        isDead = true;

        // 触发"死亡时"效果
        TriggerBuffs(BuffTrigger.死亡时);

        // 从格子上移除
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(currentCoord);
            if (cell != null)
                cell.occupyUnit = null;
        }

        // 触发事件
        OnUnitDied?.Invoke(this);
        EventManager.Trigger("UnitDied", this);

        // 子类扩展点（只会在"首次死亡"时走到）
        OnDeath();

        // 销毁物体（延迟到帧末；在此之前 isDead 会拦住对本单位的一切调用）
        Destroy(gameObject);
    }

    /// <summary>
    /// 死亡时的子类扩展点——基类保证只在【首次死亡】时被调用一次。
    ///
    /// 子类要加死亡逻辑请覆写这里，**不要覆写 Die()**：
    /// Die() 顶部的 isDead 防重复判断只在基类方法体内生效，
    /// 覆写 Die() 后子类的代码会绕过它，重复调用时被走两遍
    /// （典型后果：战斗结算后又被判定一次胜利/失败、尸体重复掉落）。
    /// </summary>
    protected virtual void OnDeath() { }

    // ============================================================
    //                       移动 + 击退
    // ============================================================

    /// <summary>
    /// 移动到目标坐标（相邻格移动，寻路版以后加）
    /// </summary>
    public virtual void MoveTo(HexCoord target)
    {
        if (isDead)
        {
            Debug.Log($"{data.unitName} 已死亡，无法移动");
            return;
        }

        // 禁止行动 buff
        if (HasEffect(BuffEffectType.禁止行动))
        {
            Debug.Log($"{data.unitName} 无法行动");
            return;
        }

        // 检查是否相邻（移动距离加成在调用方计算，这里只走相邻格）
        if (currentCoord.Distance(target) != 1)
        {
            Debug.LogWarning("只能移动到相邻格");
            return;
        }

        // 检查目标格是否可通行（地形 + 障碍物 + 单位，统一走 HexCell）
        HexCell targetCell = HexGrid.Instance.GetCell(target);
        if (targetCell == null || !targetCell.CanEnter(this))
        {
            Debug.LogWarning("目标格无法通行（不可行走 / 有障碍物 / 有单位）");
            return;
        }

        // 离开旧格子
        HexCell oldCell = HexGrid.Instance.GetCell(currentCoord);
        if (oldCell != null)
            oldCell.occupyUnit = null;

        // 站上新格子
        currentCoord = target;
        targetCell.occupyUnit = this;

        // 更新位置
        transform.position = HexGrid.Instance.HexToWorld(target);

        // 触发"移动时"效果
        OnMove();

        EventManager.Trigger("UnitMoved", this);
    }

    /// <summary>
    /// 被击退
    /// </summary>
    public virtual void PushBack(HexCoord fromDirection, int distance)
    {
        if (isDead) return;   // 已死亡的单位不再被击退

        HexCoord dir = new HexCoord(
            currentCoord.q - fromDirection.q,
            currentCoord.r - fromDirection.r);

        HexCoord target = currentCoord;
        for (int i = 0; i < distance; i++)
        {
            HexCoord next = new HexCoord(target.q + dir.q, target.r + dir.r);
            HexCell nextCell = HexGrid.Instance.GetCell(next);
            if (nextCell != null && nextCell.isWalkable && nextCell.occupyUnit == null)
                target = next;
            else
                break;
        }

        if (target != currentCoord)
        {
            HexCell oldCell = HexGrid.Instance.GetCell(currentCoord);
            if (oldCell != null)
                oldCell.occupyUnit = null;

            currentCoord = target;
            HexCell newCell = HexGrid.Instance.GetCell(target);
            if (newCell != null)
                newCell.occupyUnit = this;

            transform.position = HexGrid.Instance.HexToWorld(target);
        }
    }

    // ============================================================
    //            位移系统（击退 / 拉近 / 传送，对应文档"移动"一节）
    //
    //  表现：
    //  - 击退/拉近：一格一格平移过去（越过被撞的格子会立即停下）；
    //  - 传送：直接闪现到目标格，途中不结算碰撞。
    //
    //  碰撞规则：
    //  - 撞上障碍物或敌方单位时立即停止位移；
    //  - 自己按 collisionSelfDamage 掉血（玩家=1，敌人=2，Inspector 里填）；
    //  - 撞到的一方按 collisionBlockerDamage 掉血（敌人和障碍物都=2）；
    //  - 若本方有 reflectsDisplacement（反弹），则只有被撞的一方掉血，
    //    自己反转方向继续位移。
    //
    //  位移期间血量归零不会立刻消失，等位移全部结算完再统一死亡。
    // ============================================================

    /// <summary>
    /// 对自身施加一次位移。reference 用来确定方向：
    /// 击退 = 远离 reference；拉近 = 靠近 reference。
    /// 传送不在本方法处理，请直接用 TeleportTo(dest)。
    /// </summary>
    public virtual void ApplyDisplacement(DisplacementType type, int distance, Unit reference)
    {
        if (isDead) return;   // 已死亡的单位不再被位移
        if (type == DisplacementType.传送 || distance <= 0) return;
        if (HexGrid.Instance == null) return;

        // 计算初始方向（在当前格的 6 个邻居里选最贴合目标方向的那个）
        HexCoord dir = GetDisplacementDir(type, reference);
        if (dir.q == 0 && dir.r == 0) return;   // 无法确定方向，直接放弃

        isDisplacing = true;      // 开启"延迟死亡"
        pendingDeath = false;

        int remaining = distance;
        int guard = 0;            // 防止反弹死循环的保险丝
        while (remaining > 0 && guard < 64)
        {
            guard++;
            HexCoord next = new HexCoord(currentCoord.q + dir.q, currentCoord.r + dir.r);
            HexCell nextCell = HexGrid.Instance.GetCell(next);

            // 越界（撞地图边界）：只对自己结算伤害，然后停下
            if (nextCell == null)
            {
                TakeDamage(collisionSelfDamage, DamageType.物理);
                break;
            }

            // 前方有障碍物或单位 → 结算碰撞
            if (nextCell.obstacle != null || nextCell.occupyUnit != null)
            {
                bool stop = HandleDisplacementCollision(nextCell, ref dir, ref remaining);
                if (stop) break;
                continue;   // 反弹后方向已取反，继续走
            }

            // 前方畅通 → 前进一格
            StepTo(next);
            remaining--;
        }

        isDisplacing = false;

        // 位移全部结算完，再处理位移期间累积的死亡
        if (pendingDeath)
        {
            pendingDeath = false;
            Die();
        }

        EventManager.Trigger("UnitDisplaced", this);
    }

    /// <summary>
    /// 传送：无视途中的障碍物和单位，直接闪现到目标格（文档："传送时会闪现到目标格子"）。
    /// 落点被占用或不可行走时传送失败。
    /// </summary>
    public virtual void TeleportTo(HexCoord dest)
    {
        if (isDead) return;   // 已死亡的单位不再传送
        if (HexGrid.Instance == null) return;

        HexCell destCell = HexGrid.Instance.GetCell(dest);
        if (destCell == null || !destCell.isWalkable) return;
        if (destCell.IsBlockedByObstacle()) return;                 // 只有"阻挡移动"的障碍物才算不能落点
        if (destCell.occupyUnit != null && destCell.occupyUnit != this && !destCell.occupyUnit.IsDead) return;

        HexCell oldCell = HexGrid.Instance.GetCell(currentCoord);
        if (oldCell != null)
            oldCell.occupyUnit = null;

        currentCoord = dest;
        destCell.occupyUnit = this;
        transform.position = HexGrid.Instance.HexToWorld(dest);

        OnMove();
        EventManager.Trigger("UnitTeleported", this);
    }

    /// <summary>
    /// 处理位移碰撞，返回"是否停止位移"：
    /// - 普通碰撞：自己和被撞方都掉血 → 停止；
    /// - 反弹：只有被撞方掉血，方向取反继续 → 不停止。
    /// </summary>
    protected virtual bool HandleDisplacementCollision(HexCell cell, ref HexCoord dir, ref int remaining)
    {
        Unit blocker = cell.occupyUnit;
        Obstacle obstacle = cell.obstacle;

        if (reflectsDisplacement)
        {
            // 反弹：只有被撞的一方受伤，自己反转方向继续位移
            if (blocker != null) blocker.TakeDamage(collisionBlockerDamage, DamageType.物理);
            if (obstacle != null) obstacle.TakeDamage(collisionBlockerDamage);

            dir = new HexCoord(-dir.q, -dir.r);   // 方向取反
            remaining--;                          // 反弹也算消耗一格
            return false;
        }

        // 普通碰撞：撞的一方和被撞的一方都掉血，然后停止
        TakeDamage(collisionSelfDamage, DamageType.物理);
        if (blocker != null) blocker.TakeDamage(collisionBlockerDamage, DamageType.物理);
        if (obstacle != null) obstacle.TakeDamage(collisionBlockerDamage);
        return true;
    }

    /// <summary>
    /// 计算位移方向（返回一格的方向向量）。
    /// 击退：远离 reference；拉近：靠近 reference。
    /// 做法：把"理想落点"当作目标，在 6 个邻居里挑离它最近的那一格。
    /// </summary>
    protected virtual HexCoord GetDisplacementDir(DisplacementType type, Unit reference)
    {
        HexCoord anchor = reference != null ? reference.GetCoord() : currentCoord;

        HexCoord idealTarget;
        if (type == DisplacementType.拉近)
        {
            idealTarget = anchor;   // 越靠近 reference 越好
        }
        else
        {
            // 击退：理想落点 = 当前格 + (当前格 - reference)，即背离 reference 的方向
            HexCoord away = new HexCoord(currentCoord.q - anchor.q, currentCoord.r - anchor.r);
            idealTarget = new HexCoord(currentCoord.q + away.q, currentCoord.r + away.r);
        }

        HexCoord best = currentCoord;
        int bestDist = int.MaxValue;
        for (int i = 0; i < 6; i++)
        {
            HexCoord n = currentCoord.GetNeighbor(i);
            int d = n.Distance(idealTarget);
            if (d < bestDist)
            {
                bestDist = d;
                best = n;
            }
        }

        return new HexCoord(best.q - currentCoord.q, best.r - currentCoord.r);
    }

    /// <summary>
    /// 移动到相邻格（位移专用，不做寻路和方向校验，也不消耗耐力）。
    /// 位移每前进一步都会触发"移动时"效果。
    /// </summary>
    protected virtual void StepTo(HexCoord next)
    {
        if (isDead) return;   // 位移中途被打死则立刻停下（延迟死亡标记见 pendingDeath）

        HexCell oldCell = HexGrid.Instance.GetCell(currentCoord);
        if (oldCell != null)
            oldCell.occupyUnit = null;

        currentCoord = next;
        HexCell newCell = HexGrid.Instance.GetCell(next);
        if (newCell != null)
            newCell.occupyUnit = this;

        transform.position = HexGrid.Instance.HexToWorld(next);

        OnMove();
        EventManager.Trigger("UnitMoved", this);
    }

    /// <summary>
    /// 受到位移碰撞伤害的便捷入口（外部系统若想直接造成碰撞伤害可调用）。
    /// </summary>
    public virtual void ApplyCollisionDamage(int amount)
    {
        TakeDamage(amount, DamageType.物理);
    }

    // ============================================================
    //                     属性访问器
    // ============================================================

    public HexCoord GetCoord() => currentCoord;
    public int GetCurrentHealth() => currentHealth;
    public int GetMaxHealth() => data != null ? data.maxHealth : 0;
    public int GetShield() => shield;

    /// <summary>血量百分比（0~1），UI 血条直接用</summary>
    public float GetHealthPercent()
    {
        if (data == null || data.maxHealth <= 0) return 0f;
        return Mathf.Clamp01((float)currentHealth / data.maxHealth);
    }

    /// <summary>是否满血</summary>
    public bool IsFullHealth()
    {
        return data != null && currentHealth >= data.maxHealth;
    }

    /// <summary>当前是否有护盾</summary>
    public bool HasShield()
    {
        return shield > 0;
    }

    /// <summary>是否正在被位移（外部系统如击退/拉近时判断）</summary>
    public bool IsDisplacing => isDisplacing;
    public List<BuffInstance> GetActiveBuffs() => activeBuffs;

    /// <summary>
    /// 获取攻击力（基础攻击力 + 所有攻击伤害加成 buff）。
    /// 效果类 buff 在这里实时计算。
    /// </summary>
    public virtual int GetAttack()
    {
        if (isDead) return 0;

        // 基础攻击力从 UnitData 读；data 缺失时退回 1（旧版硬编码值），
        // 保证"没配数据的测试预制体"仍能打出 1 点伤害而不是 0。
        int baseAttack = (data != null && data.attack > 0) ? data.attack : 1;

        // 力量 / 无力 / 临时力量都属于属性类加成，查询不限触发时机
        // （按"攻击时"查会漏掉填成"立即生效"的，反之亦然）
        float bonus = GetTotalEffectValue(BuffEffectType.攻击伤害加成);
        return Mathf.Max(0, baseAttack + Mathf.RoundToInt(bonus));
    }

    /// <summary>
    /// 是否能攻击（死亡单位与被眩晕单位都放不出牌；
    /// CardData.Resolve 用它做兜底拦截）
    /// </summary>
    public virtual bool CanAttack()
    {
        return !isDead && !HasEffect(BuffEffectType.禁止行动);
    }

    /// <summary>
    /// 获取移动距离加成
    /// </summary>
    public virtual int GetMoveBonus()
    {
        return Mathf.RoundToInt(GetTotalEffectValue(BuffEffectType.移动距离加成));
    }

    /// <summary>
    /// 亲密（攻击目标限制）：目标是否被自己身上"攻击目标限制"buff 列入禁打名单。
    /// PlayerCardSystem 选目标时调用（拦在扣费之前），CardData.Resolve 里再兜一道。
    /// </summary>
    public virtual bool IsAttackTargetForbidden(Unit target)
    {
        if (target == null) return false;

        foreach (var buff in activeBuffs)
        {
            if (buff.data == null) continue;
            if (buff.data.IsForbiddenAttackTarget(target))
                return true;
        }
        return false;
    }

    // ============================================================
    //                      内部辅助方法
    // ============================================================

    /// <summary>
    /// 内部扣血，用于中毒/灼烧/流血等持续伤害（这些属于 Buff 伤害，可被护盾格挡）。
    ///
    /// 与 TakeDamage 的区别只有两点：
    ///   1. 不触发"受到伤害时"的 buff 效果（那是给"被打"用的，持续伤害不算被打，
    ///      而且触发链会绕回自己造成递归）；
    ///   2. 不广播 UnitDamaged 事件。
    /// 减伤规则本身是共用的 —— 走 CalcIncomingDamage，所以易伤/减伤/免疫伤害对
    /// 持续伤害同样生效（对应 DamageType.Buff 的定义）。
    /// </summary>
    protected void TakeDamageInternal(int amount)
    {
        if (isDead || amount <= 0) return;

        amount = CalcIncomingDamage(amount);
        if (amount <= 0) return;   // 被打减免/免疫吃干净了

        // 护盾格挡 Buff 伤害（文档："护盾能格挡 buff 伤害"）
        if (shield > 0)
        {
            int absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;
        }

        ApplyHealthLoss(amount);
    }
}
