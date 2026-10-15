using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  这个文件是"思维碎片（遗物）的配置表"
//
//  RelicData 是一个 ScriptableObject（可在 Unity 编辑器里创建的资产文件），
//  项目里 ScriptableObjects/遗物/ 文件夹下的每一个 .asset 文件，
//  比如"强韧布料.asset""漫摄徽章.asset"，都是一件遗物。
//
//  它只存"静态配置"（名字、来源、触发时机、效果），不存战斗中的临时状态。
//  真正获得遗物时调用 OnAcquire()（或兼容入口 Apply()），
//  之后每个触发时机调用 Trigger()。
//
//  与 CardData 的区别：
//    - CardData 是"主动打出去的牌"，有消耗、射程、目标；
//    - RelicData 是"被动挂着的常驻效果"，靠触发时机自动生效，没有消耗。
//
//  数据来源：飞书多维表格《思维碎片（遗物）大全》
//  合并说明：包含两套配置路径——
//    ① effects（触发时机 × 效果类型，覆盖 22 件遗物的完整效果体系）；
//    ② permanentBuffs / instantEffect（简化路径：直接挂永久 buff / 即时结算，
//       LootSystem / PlayerUnit.AddRelic 走 Apply() 入口时生效）。
// ============================================================

/// <summary>
/// 遗物掉落池——LootSystem 按池子抽遗物时使用
/// </summary>
public enum RelicPool
{
    通用,   // 事件 / 商店 / 普通掉落
    精英,   // 精英战专属掉落
    BOSS    // BOSS 战专属掉落
}

/// <summary>
/// 遗物类型——对应策划表里的"类型"列，决定这件遗物从哪来
/// </summary>
public enum RelicType
{
    特殊,         // 特殊获取途径（如 饮料成瘾）
    事件掉落,     // 事件房里拿到
    精英怪掉落,   // 打精英怪掉
    BOSS掉落,     // 打 BOSS 掉
    专属          // 角色专属，跟着某个单位走
}

/// <summary>
/// 遗物的触发时机——遗物是被动的，必须告诉它"什么时候算一次"
/// 常驻 = 不需要触发，随时查询生效（如"受到的伤害减1"）
/// </summary>
public enum RelicTrigger
{
    常驻,           // 一直生效，随时被查询
    获得时,         // 刚拿到这件遗物的那一刻，结算一次
    关卡开始,
    波次开始,       // 新一波怪物出现时
    回合开始,
    回合结束,
    战斗结束,
    受到伤害时,
    造成伤害时,
    击杀敌人时,
    击破障碍物时,
    使用技能时,
    移动时,
    敌人入场时
}

/// <summary>
/// 遗物效果类型——策划表里 22 件遗物用到的效果都收敛到这些成员上
/// 以后加新遗物如果要新机制，直接往这里追加成员
/// </summary>
public enum RelicEffectType
{
    // ---------- 常驻属性类（trigger 一般填"常驻"）----------
    力量加成,               // 蓝头骨、肌肉强化
    攻击伤害加成,           // 黄铜指虎、回归本质（按 skillTag / skillRarity 过滤）
    受到伤害减免,           // 强韧布料、柔顺布料、防弹背心（按 damageSource 过滤）
    技能耐力消耗倍率,       // 滑板鞋（-25% 填 value = -0.25 + isPercent）
    抽牌思绪消耗减免,       // 漫摄徽章（-3）
    技能使用次数加成,       // X药（所有次数类技能 +1）

    // ---------- 回复类 ----------
    回复耐力,
    回复思绪,
    回复全部资源,           // 精力充沛

    // ---------- 免疫类 ----------
    免疫Buff,               // 酱烧虫尾（中毒）
    免疫伤害来源,           // 柔顺布料（爆炸）
    免疫地形效果,           // 小皮鞋（负面地形）

    // ---------- 触发 / 特殊类 ----------
    施加Buff,               // 酱烧虫尾、笑靥
    造成伤害,               // 次元抹杀（2 × 当前楼层数）
    掉落物品,               // 劣质饮料机（掉饮料）
    技能转换,               // 玛塔的板牙（攻击技能 → 啃咬）
    特殊机制                // 反射神经、垃圾毁灭者、笑靥——代码里单独处理
}

/// <summary>
/// 遗物效果的作用范围
/// </summary>
public enum RelicTargetScope
{
    自身,        // 遗物持有者自己
    相邻目标,    // 持有者周围 1 格
    范围内目标,  // 配合 radius 使用（笑靥 = 2 格）
    全场敌人,
    全场友军,
    全场
}

/// <summary>
/// 伤害来源——用于"只减免某一类伤害"的遗物
/// </summary>
public enum RelicDamageSource
{
    全部,
    近战,
    远程,
    物理,
    非物理,
    爆炸
}

/// <summary>
/// 一条遗物效果——参考 BuffEffect / PotionEffect 的写法，
/// 一条效果 = "什么时候 + 对谁 + 做什么 + 数值多少"
/// </summary>
[System.Serializable]
public class RelicEffect
{
    [Header("触发时机")]
    public RelicTrigger trigger = RelicTrigger.常驻;

    [Header("效果类型")]
    public RelicEffectType effectType;

    [Header("数值")]
    public float value;
    [Tooltip("勾上表示 value 是百分比（0.25 = 25%）。滑板鞋的 -25% 就填 -0.25")]
    public bool isPercent;

    [Header("作用范围")]
    public RelicTargetScope scope = RelicTargetScope.自身;
    [Tooltip("scope = 范围内目标 时生效的格数半径（笑靥填 2）")]
    public int radius = 1;

    [Header("伤害来源过滤（effectType = 受到伤害减免 / 免疫伤害来源）")]
    public RelicDamageSource damageSource = RelicDamageSource.全部;

    [Header("按楼层成长（effectType = 造成伤害）")]
    [Tooltip("勾上表示最终数值 = value × 当前楼层数（次元抹杀 = 2 × 楼层）")]
    public bool scaleWithFloor;

    [Header("按技能过滤（effectType = 攻击伤害加成 / 技能耐力消耗倍率）")]
    [Tooltip("只对带这个标签的技能生效。滑板鞋填 位移")]
    public CardTag skillTag;
    [Tooltip("只对这个稀有度的技能生效。回归本质填 普通")]
    public Rarity skillRarity;

    [Header("技能引用（effectType = 技能转换 / 技能使用次数加成）")]
    public CardData skill;

    [Header("Buff 引用（effectType = 施加Buff / 免疫Buff）")]
    public BuffData buff;

    [Header("掉落物引用（effectType = 掉落物品）")]
    public PotionData potion;
}

[CreateAssetMenu(fileName = "NewRelic", menuName = "Game/Relic Data")]
public class RelicData : ScriptableObject
{
    [Header("基础信息")]
    public string relicName;                        // 遗物名称，如"强韧布料"
    [TextArea] public string description;           // 遗物描述（UI 上显示的说明文字）
    public Sprite icon;                             // 图标

    [Header("分类")]
    public RelicType relicType = RelicType.精英怪掉落;   // 获取途径（策划表"类型"列）
    [Tooltip("掉落池分组：LootSystem 按通用/精英/BOSS 三个池子抽遗物")]
    public RelicPool pool = RelicPool.通用;

    [Header("效果（可多条）")]
    public List<RelicEffect> effects = new List<RelicEffect>();

    // ========================================================
    //   简化效果路径（掉落系统配套：不想配 effects 时走这里）
    // ========================================================

    [Header("永久效果（简化路径）")]
    [Tooltip("拾取时挂给持有者的永久 buff（用了 ApplyPermanentBuff，不会随回合减少）")]
    public List<BuffData> permanentBuffs = new List<BuffData>();

    [Header("即时效果（简化路径）")]
    [Tooltip("勾选后拾取时立刻结算一次下面的效果")]
    public bool instantEffect = false;
    [Tooltip("即时恢复的生命值")]
    public int instantHeal = 0;
    [Tooltip("即时挂上的 buff")]
    public List<BuffData> instantBuffs = new List<BuffData>();

    // ========================================================
    //   技能转换配置（"玛塔的板牙"专用：把攻击技能变成啃咬）
    // ========================================================

    [Header("技能转换")]
    [Tooltip("可以把哪些技能转掉。留空 = 只要符合 requireTag 的都能转")]
    public List<CardData> convertFrom = new List<CardData>();
    [Tooltip("转成哪个技能（啃咬）")]
    public CardData convertTo;
    [Tooltip("最多转几个（玛塔的板牙 = 3）")]
    public int convertMaxCount = 0;
    [Tooltip("被转的技能必须带这个标签（玛塔的板牙填 近战 / 远程 都可以，留空 = 不限）")]
    public List<CardTag> convertRequireTags = new List<CardTag>();

    // ========================================================
    //     成长计数配置（"玛塔的板牙"：击杀累计 → 永久加伤）
    // ========================================================

    [Header("成长计数")]
    [Tooltip("统计哪个技能的击杀数。留空 = 不统计")]
    public CardData growthCountSkill;
    [Tooltip("累计击杀多少个敌人后成长一次（玛塔的板牙 = 10）")]
    public int growthKillThreshold = 0;
    [Tooltip("每成长一次加多少伤害（玛塔的板牙 = 1）")]
    public float growthBonusPerStep = 0f;

    [Header("内部备注")]
    [TextArea] public string devNotes;   // 策划备注，玩家看不到的内容写这里

    // ============================================================
    //                          查询
    // ============================================================

    /// <summary>
    /// 这件遗物是不是"常驻"类的（不需要触发时机，随时查询生效）
    /// </summary>
    public bool HasPermanentEffect
    {
        get
        {
            foreach (var e in effects)
            {
                if (e != null && e.trigger == RelicTrigger.常驻) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 取出这件遗物里所有指定触发时机的效果
    /// </summary>
    public List<RelicEffect> GetEffectsByTrigger(RelicTrigger trigger)
    {
        List<RelicEffect> result = new List<RelicEffect>();
        foreach (var e in effects)
        {
            if (e != null && e.trigger == trigger) result.Add(e);
        }
        return result;
    }

    // ============================================================
    //                          结算
    // ============================================================

    /// <summary>
    /// 兼容入口：PlayerUnit.AddRelic / LootSystem 调用 Apply()。
    /// 先结算简化路径（永久 buff + 即时效果），再走完整的 OnAcquire 流程。
    /// </summary>
    public virtual void Apply(Unit holder)
    {
        if (holder == null) return;

        // 简化路径 ①：永久 buff（ApplyPermanentBuff 保证 duration = -1）
        foreach (var buff in permanentBuffs)
        {
            if (buff != null)
                holder.ApplyPermanentBuff(buff);
        }

        // 简化路径 ②：即时效果
        if (instantEffect)
        {
            if (instantHeal > 0)
                holder.Heal(instantHeal);

            foreach (var buff in instantBuffs)
            {
                if (buff != null)
                    holder.ApplyBuff(buff);
            }
        }

        // 完整路径：获得时效果（技能转换等）+ RelicAcquired 事件
        OnAcquire(holder);
    }

    /// <summary>
    /// 获得这件遗物时调用一次。
    /// 先结算"获得时"的效果（技能转换就发生在这里），
    /// 再广播 "RelicAcquired"，让技能栏 / UI 去刷新。
    /// </summary>
    /// <param name="owner">遗物持有者（玩家单位）</param>
    public virtual void OnAcquire(Unit owner)
    {
        if (owner == null) return;

        foreach (var effect in GetEffectsByTrigger(RelicTrigger.获得时))
        {
            ApplyEffect(effect, owner, null);
        }

        EventManager.Trigger("RelicAcquired", this, owner);
        Debug.Log($"{owner.data.unitName} 获得了遗物【{relicName}】");
    }

    /// <summary>
    /// 在某个触发时机结算这件遗物。
    /// 推荐做法：在回合流程 / 伤害结算里统一广播，
    /// 由遗物管理器遍历玩家已装备的遗物逐个调用本方法。
    /// </summary>
    /// <param name="trigger">当前触发时机</param>
    /// <param name="owner">遗物持有者</param>
    /// <param name="target">本次涉及的对方单位（如挨打的人 / 被击杀的敌人），可为 null</param>
    public virtual void Trigger(RelicTrigger trigger, Unit owner, Unit target = null)
    {
        if (owner == null) return;

        foreach (var effect in GetEffectsByTrigger(trigger))
        {
            ApplyEffect(effect, owner, target);
        }
    }

    /// <summary>
    /// 结算单条遗物效果。
    ///
    /// 说明：遗物效果跨了很多系统（楼层数、波次、掉落表、技能栏……），
    /// 目前只有一部分能直接落到已有接口上，其余先广播事件留作接入点，
    /// 等对应系统做好后在这里补上具体实现。
    /// </summary>
    protected virtual void ApplyEffect(RelicEffect effect, Unit owner, Unit target)
    {
        switch (effect.effectType)
        {
            // ---------- 回复类：可以立刻落地 ----------
            case RelicEffectType.回复耐力:
                if (owner is PlayerUnit staminaTarget)
                    staminaTarget.RestoreStamina(Mathf.RoundToInt(effect.value));
                break;

            case RelicEffectType.回复思绪:
                if (owner is PlayerUnit thoughtTarget)
                    thoughtTarget.RestoreThought(Mathf.RoundToInt(effect.value));
                break;

            case RelicEffectType.回复全部资源:
                if (owner is PlayerUnit fullTarget)
                    fullTarget.RestoreAllResources();
                Debug.Log($"{owner.data.unitName} 的遗物【{relicName}】回复了全部耐力和思绪");
                break;

            // ---------- Buff 类：可以立刻落地 ----------
            case RelicEffectType.施加Buff:
                ApplyBuffToScope(effect, owner);
                break;

            // ---------- 直接造成伤害（次元抹杀）----------
            case RelicEffectType.造成伤害:
            {
                float raw = effect.scaleWithFloor
                    ? effect.value * GetCurrentFloor()
                    : effect.value;
                int damage = Mathf.RoundToInt(raw);

                // 没指定目标时，按策划规则自动挑"血量最少的敌人"
                Unit victim = target;
                if (victim == null && effect.scope == RelicTargetScope.全场敌人)
                    victim = FindLowestHealthEnemy();

                if (victim != null)
                {
                    victim.TakeDamage(damage);
                    Debug.Log($"{owner.data.unitName} 的遗物【{relicName}】" +
                              $"对 {victim.data.unitName} 造成 {damage} 点伤害");
                }
                break;
            }

            // ---------- 常驻属性类：本身不"执行"，由其他系统反查数值 ----------
            case RelicEffectType.力量加成:
            case RelicEffectType.攻击伤害加成:
            case RelicEffectType.受到伤害减免:
            case RelicEffectType.技能耐力消耗倍率:
            case RelicEffectType.抽牌思绪消耗减免:
            case RelicEffectType.技能使用次数加成:
            case RelicEffectType.免疫Buff:
            case RelicEffectType.免疫伤害来源:
            case RelicEffectType.免疫地形效果:
                // 这些效果不在这里改数值，而是由伤害结算 / 卡牌系统
                // 通过 GetTotalValue() / HasEffect() 主动反查。
                break;

            // ---------- 掉落 / 技能改动 / 特殊机制 ----------
            case RelicEffectType.掉落物品:
                // TODO：接入掉落系统后，在这里把 effect.potion 加进结算掉落列表
                EventManager.Trigger("RelicDropItem", this, effect.potion);
                break;

            case RelicEffectType.技能转换:
                // TODO：接入技能栏系统后，在这里执行 convertFrom → convertTo 的替换
                EventManager.Trigger("RelicSkillConvert", this, owner);
                break;

            case RelicEffectType.特殊机制:
                // 反射神经（自动躲开近战）、垃圾毁灭者（一击拆障碍）等
                // 需要单独写逻辑，先广播事件给监听方处理
                EventManager.Trigger("RelicSpecialEffect", this, owner, target);
                break;
        }
    }

    /// <summary>
    /// 按作用范围挂 buff
    /// </summary>
    protected virtual void ApplyBuffToScope(RelicEffect effect, Unit owner)
    {
        if (effect.buff == null) return;

        switch (effect.scope)
        {
            case RelicTargetScope.自身:
                owner.ApplyBuff(effect.buff);
                break;

            case RelicTargetScope.相邻目标:
            case RelicTargetScope.范围内目标:
                ApplyBuffToUnitsAround(owner, effect.radius <= 0 ? 1 : effect.radius, effect.buff);
                break;

            case RelicTargetScope.全场敌人:
                foreach (var enemy in FindObjectsOfType<EnemyUnit>())
                {
                    if (enemy.GetCurrentHealth() > 0)
                        enemy.ApplyBuff(effect.buff);
                }
                break;

            case RelicTargetScope.全场友军:
            case RelicTargetScope.全场:
                foreach (var ally in FindObjectsOfType<PlayerUnit>())
                    ally.ApplyBuff(effect.buff);
                break;
        }
    }

    /// <summary>
    /// 给 owner 周围 radius 格内的所有单位挂 buff（"笑靥"用）
    /// </summary>
    protected virtual void ApplyBuffToUnitsAround(Unit owner, int radius, BuffData buff)
    {
        if (HexGrid.Instance == null) return;

        HexCoord center = owner.GetCoord();

        foreach (var unit in FindObjectsOfType<Unit>())
        {
            if (unit == null || unit == owner) continue;
            if (unit.GetCurrentHealth() <= 0) continue;
            if (center.Distance(unit.GetCoord()) <= radius)
                unit.ApplyBuff(buff);
        }
    }

    /// <summary>
    /// 找出地图上血量最少的敌人（次元抹杀用；同血量随机取一个由调用方决定）
    /// </summary>
    protected virtual Unit FindLowestHealthEnemy()
    {
        Unit lowest = null;
        int lowestHp = int.MaxValue;

        foreach (var enemy in FindObjectsOfType<EnemyUnit>())
        {
            int hp = enemy.GetCurrentHealth();
            if (hp <= 0) continue;
            if (hp < lowestHp)
            {
                lowestHp = hp;
                lowest = enemy;
            }
        }

        return lowest;
    }

    /// <summary>
    /// 当前楼层数（= 章节数）。关卡系统做好之前先返回 1。
    /// </summary>
    protected virtual int GetCurrentFloor()
    {
        // TODO：接入关卡进度系统后改成读真实章节数
        return 1;
    }

    // ============================================================
    //        静态查询：给伤害结算 / 卡牌系统反查遗物加成用
    // ============================================================

    /// <summary>
    /// 统计一组遗物里某类效果的总数值。
    /// 用法（示例，写在 Unit.TakeDamage 里）：
    ///   float reduce = RelicData.GetTotalValue(playerRelics,
    ///                                          RelicEffectType.受到伤害减免,
    ///                                          RelicDamageSource.近战);
    /// </summary>
    public static float GetTotalValue(IReadOnlyList<RelicData> relics,
                                      RelicEffectType effectType,
                                      RelicDamageSource damageSource = RelicDamageSource.全部,
                                      RelicTrigger trigger = RelicTrigger.常驻)
    {
        if (relics == null) return 0f;

        float total = 0f;
        foreach (var relic in relics)
        {
            if (relic == null) continue;

            foreach (var e in relic.effects)
            {
                if (e == null) continue;
                if (e.effectType != effectType) continue;
                if (e.trigger != trigger) continue;

                // 伤害来源过滤：填"全部"的遗物对所有来源都生效
                if (e.damageSource != RelicDamageSource.全部 &&
                    e.damageSource != damageSource) continue;

                total += e.value;
            }
        }
        return total;
    }

    /// <summary>
    /// 一组遗物里有没有某类效果（免疫类判定用）
    /// </summary>
    public static bool HasEffect(IReadOnlyList<RelicData> relics,
                                RelicEffectType effectType,
                                RelicDamageSource damageSource = RelicDamageSource.全部)
    {
        if (relics == null) return false;

        foreach (var relic in relics)
        {
            if (relic == null) continue;

            foreach (var e in relic.effects)
            {
                if (e == null || e.effectType != effectType) continue;
                if (e.damageSource != RelicDamageSource.全部 &&
                    e.damageSource != damageSource) continue;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 一组遗物里有没有"免疫这个 buff"的（酱烧虫尾免疫中毒）
    /// </summary>
    public static bool IsImmuneToBuff(IReadOnlyList<RelicData> relics, BuffData buff)
    {
        if (relics == null || buff == null) return false;

        foreach (var relic in relics)
        {
            if (relic == null) continue;

            foreach (var e in relic.effects)
            {
                if (e == null) continue;
                if (e.effectType == RelicEffectType.免疫Buff && e.buff == buff)
                    return true;
            }
        }
        return false;
    }
}
