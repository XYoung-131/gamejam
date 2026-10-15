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
/// </summary>
public abstract class Unit : MonoBehaviour
{
    [Header("配置数据")]
    [Tooltip("引用 ScriptableObjects/Units/ 下的单位数据资产")]
    public UnitData data;

    [Header("运行时状态")]
    [SerializeField] protected int currentHealth;    // 当前血量
    [SerializeField] protected int shield;          // 护盾（=护甲）
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

    // 当前身上的所有 buff
    [SerializeField] protected List<BuffInstance> activeBuffs = new List<BuffInstance>();

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

        // 把自己注册到格子上
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(startCoord);
            if (cell != null)
                cell.occupyUnit = this;
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
        if (amount <= 0) return;

        // 免疫伤害 → 直接返回
        if (HasEffect(BuffEffectType.免疫伤害))
            return;

        // 受到伤害固定减免
        float damageReduce = GetTotalEffectValue(BuffEffectType.受到伤害固定减免, BuffTrigger.受到伤害时);
        amount = Mathf.Max(0, amount - Mathf.RoundToInt(damageReduce));

        // 受到伤害倍率（易伤 = >1，减伤 = <1）
        float damageMultiplier = GetTotalEffectValue(BuffEffectType.受到伤害倍率, BuffTrigger.受到伤害时);
        if (damageMultiplier <= 0) damageMultiplier = 1f;  // 没 buff 时倍率为 1
        amount = Mathf.CeilToInt(amount * damageMultiplier);   // 向上取整

        // 扣护盾（真实伤害无视护甲）
        if (type != DamageType.真实 && shield > 0)
        {
            int absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;
        }

        // 扣血（统一走 ApplyHealthLoss，内部处理免疫死亡 + 位移延迟死亡）
        ApplyHealthLoss(amount);

        // 触发"受到伤害时"的 buff 效果
        TriggerBuffs(BuffTrigger.受到伤害时);

        EventManager.Trigger("UnitDamaged", this);
    }

    /// <summary>
    /// 统一的扣血入口：所有伤害/持续伤害最终都从这里扣血，
    /// 保证"免疫死亡"和"位移期间延迟死亡"两条规则只写一次。
    /// </summary>
    protected virtual void ApplyHealthLoss(int amount)
    {
        if (amount <= 0) return;

        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;

            // 免疫死亡 → 血量锁在 1
            if (HasEffect(BuffEffectType.免疫死亡))
            {
                currentHealth = 1;
                return;
            }

            // 正在被位移 → 先记为"待死亡"，等位移结算完再消失
            if (isDisplacing)
            {
                pendingDeath = true;
                return;
            }

            Die();
        }
    }

    /// <summary>
    /// 清空护盾（文档："下个回合开始时护盾会消失"）。
    /// 由 OnTurnStart 统一调用。
    /// </summary>
    public virtual void ClearShield()
    {
        if (shield == 0) return;
        shield = 0;
        EventManager.Trigger("UnitShieldChanged", this);
    }

    /// <summary>
    /// 治疗
    /// </summary>
    public virtual void Heal(int amount)
    {
        currentHealth = Mathf.Min(currentHealth + amount, data.maxHealth);
        EventManager.Trigger("UnitHealed", this);
    }

    /// <summary>
    /// 添加护盾
    /// </summary>
    public virtual void AddShield(int amount)
    {
        shield += amount;
        EventManager.Trigger("UnitShieldChanged", this);
    }

    // ============================================================
    //                       Buff 系统
    // ============================================================

    /// <summary>
    /// 施加一个 buff
    /// </summary>
    public virtual void ApplyBuff(BuffData buffData)
    {
        // 免疫负面 → 如果是负面 buff 直接跳过
        if (buffData.polarity == BuffPolarity.负面 && HasEffect(BuffEffectType.免疫负面))
            return;

        // 查找是否已有同类型 buff
        BuffInstance existing = activeBuffs.Find(b => b.data == buffData);

        if (existing != null)
        {
            // 已存在：叠加持续时间
            if (existing.remainingDuration != -1 && buffData.duration != -1)
                existing.remainingDuration += buffData.duration;

            // 可叠加层数
            if (buffData.canStack)
            {
                existing.currentStacks = Mathf.Min(
                    existing.currentStacks + 1,
                    buffData.maxStack);
            }
        }
        else
        {
            // 不存在：新增
            BuffInstance newBuff = new BuffInstance
            {
                data = buffData,
                remainingDuration = buffData.duration,
                currentStacks = 1
            };
            activeBuffs.Add(newBuff);

            // 立即生效效果（触发时机 = 立即生效）
            TriggerBuffEffects(newBuff, BuffTrigger.立即生效);
        }

        OnBuffApplied?.Invoke(this, buffData);
        EventManager.Trigger("UnitBuffApplied", this, buffData);
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
            foreach (var effect in buff.data.effects)
            {
                if (effect.effectType == effectType)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 获取某种效果类型的总数值（叠加所有同类型 buff）
    /// 比如所有"攻击伤害加成" buff 的 value 总和
    /// </summary>
    /// <param name="trigger">只统计特定触发时机的效果</param>
    public virtual float GetTotalEffectValue(BuffEffectType effectType, BuffTrigger trigger = BuffTrigger.立即生效)
    {
        float total = 0f;
        foreach (var buff in activeBuffs)
        {
            foreach (var effect in buff.data.effects)
            {
                if (effect.effectType == effectType && effect.trigger == trigger)
                {
                    // 叠加层数
                    total += effect.value * buff.currentStacks;
                }
            }
        }
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
        foreach (var effect in buff.data.effects)
        {
            if (effect.trigger != trigger) continue;

            switch (effect.effectType)
            {
                case BuffEffectType.固定持续伤害:
                    int dmg = Mathf.RoundToInt(effect.value * buff.currentStacks);
                    TakeDamageInternal(dmg);
                    break;

                case BuffEffectType.清除所有负面:
                    RemoveAllNegativeBuffs();
                    break;

                case BuffEffectType.回合结束死亡:
                    // 在回合结束触发时直接死亡
                    if (trigger == BuffTrigger.回合结束)
                        Die();
                    break;

                // 属性类 buff 不在这里触发（在 GetAttack/TakeDamage 等方法里计算）
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
            if (activeBuffs[i].data.polarity == BuffPolarity.负面)
            {
                BuffData data = activeBuffs[i].data;
                activeBuffs.RemoveAt(i);
                OnBuffRemoved?.Invoke(this, data);
                EventManager.Trigger("UnitBuffRemoved", this, data);
            }
        }
    }

    // ============================================================
    //                    回合生命周期
    // ============================================================

    /// <summary>
    /// 回合开始时调用
    /// </summary>
    public virtual void OnTurnStart()
    {
        // 0. 护盾在下个回合开始时消失（文档规则）
        ClearShield();

        // 1. 触发"回合开始"效果（如持续伤害）
        TriggerBuffs(BuffTrigger.回合开始);

        // 2. 所有 buff 持续回合 -1（永久 buff 不减）
        for (int i = activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = activeBuffs[i];
            if (buff.remainingDuration == -1) continue;  // 永久 buff

            buff.remainingDuration--;
            if (buff.remainingDuration <= 0)
            {
                BuffData expiredData = buff.data;
                activeBuffs.RemoveAt(i);
                OnBuffRemoved?.Invoke(this, expiredData);
                EventManager.Trigger("UnitBuffExpired", this, expiredData);
            }
        }
    }

    /// <summary>
    /// 回合结束时调用
    /// </summary>
    public virtual void OnTurnEnd()
    {
        TriggerBuffs(BuffTrigger.回合结束);
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

        // 销毁物体
        Destroy(gameObject);
    }

    // ============================================================
    //                       移动 + 击退
    // ============================================================

    /// <summary>
    /// 移动到目标坐标（相邻格移动，寻路版以后加）
    /// </summary>
    public virtual void MoveTo(HexCoord target)
    {
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

        // 检查目标格是否可通行
        HexCell targetCell = HexGrid.Instance.GetCell(target);
        if (targetCell == null || !targetCell.isWalkable || targetCell.occupyUnit != null)
        {
            Debug.LogWarning("目标格无法通行");
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
        if (HexGrid.Instance == null) return;

        HexCell destCell = HexGrid.Instance.GetCell(dest);
        if (destCell == null || !destCell.isWalkable) return;
        if (destCell.obstacle != null) return;
        if (destCell.occupyUnit != null && destCell.occupyUnit != this) return;

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

    /// <summary>是否正在被位移（外部系统如击退/拉近时判断）</summary>
    public bool IsDisplacing => isDisplacing;
    public List<BuffInstance> GetActiveBuffs() => activeBuffs;

    /// <summary>
    /// 获取攻击力（考虑所有攻击伤害加成 buff）
    /// </summary>
    public virtual int GetAttack()
    {
        int baseAttack = 1;  // 基础攻击（MVP 简化，以后从 data 读）
        float bonus = GetTotalEffectValue(BuffEffectType.攻击伤害加成, BuffTrigger.攻击时);
        return baseAttack + Mathf.RoundToInt(bonus);
    }

    /// <summary>
    /// 是否能攻击
    /// </summary>
    public virtual bool CanAttack()
    {
        return !HasEffect(BuffEffectType.禁止行动);
    }

    /// <summary>
    /// 获取移动距离加成
    /// </summary>
    public virtual int GetMoveBonus()
    {
        return Mathf.RoundToInt(GetTotalEffectValue(BuffEffectType.移动距离加成, BuffTrigger.立即生效));
    }

    // ============================================================
    //                      内部辅助方法
    // ============================================================

    /// <summary>
    /// 内部扣血（跳过 buff 计算，防止递归）
    /// 用于中毒/灼烧/流血等持续伤害效果（这些属于 Buff 伤害，可被护盾格挡）
    /// </summary>
    protected void TakeDamageInternal(int amount)
    {
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
