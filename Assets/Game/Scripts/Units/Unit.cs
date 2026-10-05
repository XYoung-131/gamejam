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
    [SerializeField] protected int shield;          // 护盾
    [SerializeField] protected HexCoord currentCoord; // 当前所在六边形坐标

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
    /// 受到伤害
    /// 先扣护盾，再扣血；考虑各种 buff 效果
    /// </summary>
    public virtual void TakeDamage(int amount)
    {
        // 免疫伤害 → 直接返回
        if (HasEffect(BuffEffectType.免疫伤害))
            return;

        // 受到伤害固定减免
        float damageReduce = GetTotalEffectValue(BuffEffectType.受到伤害固定减免, BuffTrigger.受到伤害时);
        amount = Mathf.Max(0, amount - Mathf.RoundToInt(damageReduce));

        // 受到伤害倍率（易伤 = >1，减伤 = <1）
        float damageMultiplier = GetTotalEffectValue(BuffEffectType.受到伤害倍率, BuffTrigger.受到伤害时);
        if (damageMultiplier <= 0) damageMultiplier = 1f;  // 没 buff 时倍率为 1
        amount = Mathf.CeilToInt(amount * damageMultiplier);

        // 先扣护盾
        if (shield > 0)
        {
            int absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;
        }

        // 再扣血
        if (amount > 0)
        {
            currentHealth -= amount;
            if (currentHealth <= 0)
            {
                currentHealth = 0;
                // 免疫死亡 → 血量锁在 1
                if (HasEffect(BuffEffectType.免疫死亡))
                {
                    currentHealth = 1;
                }
                else
                {
                    Die();
                }
            }
        }

        // 触发"受到伤害时"的 buff 效果
        TriggerBuffs(BuffTrigger.受到伤害时);

        EventManager.Trigger("UnitDamaged", this);
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
    //                     属性访问器
    // ============================================================

    public HexCoord GetCoord() => currentCoord;
    public int GetCurrentHealth() => currentHealth;
    public int GetMaxHealth() => data != null ? data.maxHealth : 0;
    public int GetShield() => shield;
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
    /// 用于流血等持续伤害效果
    /// </summary>
    protected void TakeDamageInternal(int amount)
    {
        if (shield > 0)
        {
            int absorbed = Mathf.Min(shield, amount);
            shield -= absorbed;
            amount -= absorbed;
        }

        if (amount > 0)
        {
            currentHealth -= amount;
            if (currentHealth <= 0)
            {
                currentHealth = 0;
                if (!HasEffect(BuffEffectType.免疫死亡))
                    Die();
            }
        }
    }
}
