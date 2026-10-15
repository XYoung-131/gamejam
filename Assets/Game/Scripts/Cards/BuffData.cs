using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  这个文件是"Buff / Debuff 的配置表"
//
//  BuffData 是一个 ScriptableObject（可在 Unity 编辑器里创建的资产文件），
//  项目里 ScriptableObjects/Buff/ 文件夹下的每一个 .asset 文件，
//  比如"眩晕.asset""力量.asset""中毒.asset"，都是一个 buff。
//
//  它只存"静态配置"（名字、极性、持续、层数规则、效果），
//  不存战斗中的临时状态——临时状态由 Unit.cs 里的 BuffInstance 记。
//
//  ============================================================
//                       本项目的 buff 结算规则
//  ============================================================
//
//  buff 分两大类，由 category 字段决定：
//
//  ┌ 伤害类（category = 伤害类）
//  │   效果在【单位回合开始时】结算一次（中毒/灼烧/流血 在这里跳伤害），
//  │   持续时间 / 层数在【单位回合结束时】衰减。
//  └  对应 effects 里 trigger 填 = 回合开始
//
//  ┌ 影响类（category = 影响类）
//  │   从获得那一刻起持续生效（属性加成、控制、免疫等），
//  │   持续时间 / 层数在【单位回合结束时】衰减，减到 0 就消失。
//  └  对应 effects 里 trigger 填 = 立即生效
//
//  ============================================================
//                        数值填写约定（很重要）
//  ============================================================
//
//  · 名字里带"倍率"的字段 → 填【绝对乘数】
//      沉重（移动耐力消耗 +25%）填 1.25
//      力竭（耐力回复减半）   填 0.5
//      易伤（受到伤害 +25%）  填 1.25
//
//  · 名字里带"加成"/"减免"的字段 → 填【增量】
//      力量 填 +1、无力 填 -1、残废 填 -1、殊死一搏 填 3
//
//  · 多层合并规则（代码里自动处理，填表时心里有数就行）
//      加成型：数值直接相加              → 力量 3 层 = +3
//      倍率型：累加"偏离 1"的部分再乘回去 → 易伤 2 层 = 1 + 0.25×2 = 1.5
//      所以倍率型**不要**填成 0.25 这种增量，否则会被当成 0.25 倍（几乎免疫）。
//      哪些类型算"倍率型"见 BuffData.IsMultiplierType()。
//
//  · 查询时机不用纠结：属性类加成走 Unit.GetTotalEffectValue(effectType)（不限 trigger），
//    无论填"立即生效"还是"攻击时"都算数，不会因为填错时机而静默失效。
//
//  ⚠ 已知不一致（待统一）：RelicData 里同名的"技能耐力消耗倍率"用的是【增量】填法
//    （滑板鞋 -25% 填 -0.25），和这里的【绝对乘数】不一致。二者尚未统一。
//  ============================================================
//                   全部 20 个 buff 的填写对照
//  ============================================================
//
//  【负面 12 种】（没特殊标识 = 回合结束层数 -1）
//
//   力竭      影响类  耐力回复倍率 = 0.5        onlyForPlayerUnit = √（对敌人无效）
//   混乱      影响类  思绪回复倍率 = 0.5        onlyForPlayerUnit = √（对敌人无效）
//   残废      影响类  移动距离加成 = -1          halveStacksOnEliteAndBoss = √
//   沉重      影响类  移动耐力消耗倍率 = 1.25    canStack = ✗（不叠加）
//   无力      影响类  攻击伤害加成 = -1          halveStacksOnEliteAndBoss = √
//   虚弱      影响类  攻击耐力消耗倍率 = 1.25    canStack = ✗（不叠加）
//   中毒      伤害类  固定持续伤害 = 1（每层）   stackDecay = 未叠加则清空，clearIfNotStackedTurns = 3
//   灼烧      伤害类  固定持续伤害 = 1（每层）   stackDecay = 默认（即回合结束层数 -1）
//   流血      伤害类  固定持续伤害 = 1（每层）   stackDecay = 回合结束减半
//   易伤      影响类  受到伤害倍率 = 1.25
//   眩晕      影响类  禁止行动                  duration 一般填 1
//   亲密      影响类  攻击目标限制              stackDecay = 回合结束消失
//                                             forbiddenAttackTargets = 大抱抱/小抱抱/德亚/普兰
//
//  【正面 8 种】（没特殊标识 = 层数不减少，即永久）
//
//   殊死一搏  影响类  清除所有负面 + 免疫负面 + 受到伤害固定减免 3 + 回合结束死亡
//                     duration = 1（本回合）
//   力量      影响类  攻击伤害加成 = +1         duration = -1（永久）
//   临时力量  影响类  攻击伤害加成 = +1         stackDecay = 回合结束减1
//   敏捷      影响类  移动距离加成 = +1         duration = -1（永久）
//   临时敏捷  影响类  移动距离加成 = +1         stackDecay = 回合结束减1
//   无敌      影响类  免疫伤害 + 免疫死亡       （不免疫负面）
//   神圣      影响类  特殊机制                  具体效果写在《怪物一览》里
//   荧光      影响类  特殊机制                  荧光虫专用，用来释放特殊技能
//
// ============================================================

/// <summary>
/// buff 极性——正面还是负面
/// 影响 Resolve 时的默认层数衰减规则，也用于"免疫负面""清除所有负面"的判定
/// </summary>
public enum BuffPolarity
{
    正面,
    负面
}

/// <summary>
/// buff 大类——决定效果的结算时机（见文件头说明）
/// </summary>
public enum BuffCategory
{
    伤害类,   // 持续伤害：回合开始结算伤害，回合结束衰减
    影响类    // 属性 / 控制 / 免疫：获得即生效，回合结束衰减
}

/// <summary>
/// 效果触发时机
/// 注意：伤害类 buff 的持续伤害统一填【回合开始】，影响类统一填【立即生效】，
/// 这是本项目 buff 系统的约定，不要按策划文案里的"回合结束时"去填。
///
/// 属性类效果（攻击伤害加成、受到伤害倍率…）的 trigger 目前**不影响查询结果**——
/// Unit.GetTotalEffectValue(effectType) 不限时机，填哪个都能算上。
/// 这个约定只是为了让表格可读、便于以后接入"按事件触发"的新效果。
/// </summary>
public enum BuffTrigger
{
    立即生效,       // 获得时就生效 / 影响类的常驻生效
    回合开始,       // 伤害类持续伤害在这里跳
    回合结束,
    受到伤害时,
    攻击时,
    移动时,
    死亡时
}

/// <summary>
/// 层数衰减方式——决定"回合结束"时这个 buff 的层数怎么变
///
/// 默认值 默认 会自动跟随极性：
///   负面 → 回合结束减1
///   正面 → 不减少（永久挂到 duration 用完）
/// 策划文案里如果有特殊标识（"层数减半""回合结束消失""不会减少"），
/// 就在这里显式选对应的那一项。
/// </summary>
public enum BuffStackDecay
{
    默认,               // 负面=回合结束减1，正面=不减少
    不减少,             // 显式声明"没特殊标识不会减少"的正面 buff
    回合结束减1,        // 灼烧 / 临时力量 / 临时敏捷
    回合结束减半,       // 流血：向上取整，1 层时直接清空
    回合结束消失,       // 亲密
    未叠加则清空        // 中毒：N 回合内没有再叠层就清空全部层数
}

/// <summary>
/// 效果类型——策划表里 20 个 buff 用到的效果都收敛到这些成员上
/// 以后加新 buff 如果要新机制，直接往这里追加成员（务必追加在末尾，不要插队改顺序）
/// </summary>
public enum BuffEffectType
{
    // ---------- 属性类（影响类 buff 用，各系统实时反查数值）----------
    耐力回复倍率,       // 力竭 = 0.5（减半）
    思绪回复倍率,       // 混乱 = 0.5（减半）
    移动距离加成,       // 残废 = -1 / 敏捷 = +1（不影响传送类技能）
    移动耐力消耗倍率,   // 沉重 = 1.25
    攻击伤害加成,       // 无力 = -1 / 力量 = +1
    攻击耐力消耗倍率,   // 虚弱 = 1.25
    受到伤害倍率,       // 易伤 = 1.25（最终伤害 = 原伤害 × 该值）
    受到伤害固定减免,   // 殊死一搏 = 3

    // ---------- 持续伤害类（伤害类 buff 用，回合开始结算）----------
    固定持续伤害,       // 中毒 / 灼烧 / 流血，伤害 = value × 当前层数

    // ---------- 控制类 ----------
    禁止行动,           // 眩晕

    // ---------- 特殊效果类 ----------
    清除所有负面,       // 殊死一搏
    清除所有正面,
    免疫负面,           // 殊死一搏
    免疫伤害,           // 无敌
    免疫死亡,           // 无敌
    回合结束死亡,       // 殊死一搏（回合结束直接死亡）
    攻击目标限制,       // 亲密（不能把这些单位选为攻击目标）
    特殊机制            // 神圣 / 荧光——代码里单独处理
}

/// <summary>
/// 一条 buff 效果——一条效果 = "做什么 + 什么时机 + 数值多少"
/// 一个 buff 可以挂多条效果（比如"殊死一搏"就是 4 条）
/// </summary>
[System.Serializable]
public class BuffEffect
{
    [Header("效果类型")]
    public BuffEffectType effectType;

    [Header("触发时机")]
    [Tooltip("伤害类的持续伤害填 回合开始；影响类的属性效果填 立即生效")]
    public BuffTrigger trigger;

    [Header("数值")]
    [Tooltip("倍率类填绝对乘数（1.25 / 0.5），加成类填增量（+1 / -1），减免类填正数")]
    public float value;

    [Header("是否百分比")]
    [Tooltip("给 UI 显示用：勾上时界面显示为百分比（0.25 → 25%）")]
    public bool isPercent;
}

[CreateAssetMenu(fileName = "NewBuff", menuName = "Game/Buff Data")]
public class BuffData : ScriptableObject
{
    [Header("基础信息")]
    public string buffName;                     // buff 名称，如"眩晕"
    [TextArea] public string description;       // buff 描述（UI 上显示的说明文字）
    public Sprite icon;                         // 图标

    [Header("分类")]
    public BuffPolarity polarity;               // 正面 / 负面
    public BuffCategory category = BuffCategory.影响类;   // 伤害类 / 影响类

    [Header("持续与叠加")]
    [Tooltip("持续回合数。-1 = 永久不消失（力量、敏捷这类常驻正面 buff）")]
    public int duration = -1;

    [Tooltip("最大层数")]
    public int maxStack = 1;

    [Tooltip("能不能叠层。沉重、虚弱明确写了'不叠加'，这两件要取消勾选")]
    public bool canStack = false;

    [Tooltip("层数衰减方式。留'默认'即按极性走：负面回合结束减1，正面不减少")]
    public BuffStackDecay stackDecay = BuffStackDecay.默认;

    [Tooltip("仅 stackDecay = 未叠加则清空 时生效。中毒填 3（3 回合内没有再叠层就清空全部层数）")]
    public int clearIfNotStackedTurns = 0;

    [Header("施加规则")]
    [Tooltip("勾上：对精英怪和 BOSS 施加时层数减半（向上取整，单次至少 1 点）。残废、无力需要勾")]
    public bool halveStacksOnEliteAndBoss = false;

    [Tooltip("勾上：只对玩家单位生效，对敌人无效。力竭、混乱需要勾")]
    public bool onlyForPlayerUnit = false;

    [Header("攻击目标限制（effectType = 攻击目标限制 时填）")]
    [Tooltip("挂了『亲密』后，这些单位不能被选为攻击目标")]
    public List<UnitData> forbiddenAttackTargets = new List<UnitData>();

    [Header("效果（可多条）")]
    public List<BuffEffect> effects = new List<BuffEffect>();

    [Header("内部备注")]
    [TextArea] public string devNotes;          // 策划备注，不会在游戏里显示

    // ============================================================
    //                          查询属性
    // ============================================================

    /// <summary>
    /// 这个效果类型是不是【倍率型】。
    ///
    /// · 倍率型：value 是【绝对乘数】（易伤 1.25、力竭 0.5、虚弱 1.25）。
    ///   多层合并时累加"偏离 1"的部分，再乘回去：
    ///       最终倍率 = 1 + Σ( value - 1 ) × 层数
    ///   易伤叠 2 层 → 1 + 0.25×2 = 1.5。
    ///   不能把 1.25 直接相加 —— 那会得到 2.5，凭空多出一倍的基数。
    ///
    /// · 加成型（其余全部）：value 是【增量】（力量 +1、无力 -1、殊死一搏 3），直接求和。
    ///
    /// 以后新增带"倍率"字样的效果类型，记得同步加进这个 switch。
    /// </summary>
    public static bool IsMultiplierType(BuffEffectType effectType)
    {
        switch (effectType)
        {
            case BuffEffectType.耐力回复倍率:
            case BuffEffectType.思绪回复倍率:
            case BuffEffectType.移动耐力消耗倍率:
            case BuffEffectType.攻击耐力消耗倍率:
            case BuffEffectType.受到伤害倍率:
                return true;

            default:
                return false;
        }
    }

    /// <summary>是不是负面 buff</summary>
    public bool IsDebuff => polarity == BuffPolarity.负面;

    /// <summary>是不是持续伤害类（中毒 / 灼烧 / 流血）</summary>
    public bool IsDamageOverTime => category == BuffCategory.伤害类;

    /// <summary>是不是永久 buff（duration = -1）</summary>
    public bool IsPermanent => duration < 0;

    /// <summary>是不是控制类（眩晕这种不能动）</summary>
    public bool IsControl => HasEffectType(BuffEffectType.禁止行动);

    /// <summary>能不能叠层</summary>
    public bool CanStack => canStack && maxStack > 1;

    /// <summary>
    /// 实际生效的层数衰减方式。
    /// 填"默认"时按极性推导：负面 → 回合结束减1，正面 → 不减少。
    /// </summary>
    public BuffStackDecay ResolvedDecay
    {
        get
        {
            if (stackDecay != BuffStackDecay.默认) return stackDecay;
            return IsDebuff ? BuffStackDecay.回合结束减1 : BuffStackDecay.不减少;
        }
    }

    /// <summary>
    /// 有没有带某个效果类型
    /// </summary>
    public bool HasEffectType(BuffEffectType effectType)
    {
        foreach (var e in effects)
        {
            if (e != null && e.effectType == effectType) return true;
        }
        return false;
    }

    /// <summary>
    /// 取出单个 buff 配置里某个效果类型的总数值（一个 buff 挂多条同类效果时会加起来）。
    /// 注意：这里只看"这一份配置"，不含层数、也不跨 buff 叠加——
    /// 要算某个单位身上所有 buff 的合计，请用 Unit.GetTotalEffectValue()。
    ///
    /// 合并规则和 Unit 侧保持一致（倍率型累加偏离 1 的部分）：
    ///   倍率型 → 1 + Σ( value - 1 )，没有该效果时返回 1
    ///   加成型 → Σ value，没有该效果时返回 0
    ///
    /// 用法示例（写在 PlayerUnit 回合开始补耐力时）：
    ///   float rate = buffData.GetTotalValue(BuffEffectType.耐力回复倍率);
    ///   最终回复 = Mathf.FloorToInt(基础回复 × rate);
    /// </summary>
    public float GetTotalValue(BuffEffectType effectType, BuffTrigger? trigger = null)
    {
        bool isMultiplier = IsMultiplierType(effectType);
        float total = isMultiplier ? 1f : 0f;

        foreach (var e in effects)
        {
            if (e == null) continue;
            if (e.effectType != effectType) continue;
            if (trigger.HasValue && e.trigger != trigger.Value) continue;

            total += isMultiplier ? (e.value - 1f) : e.value;
        }

        return isMultiplier ? Mathf.Max(0f, total) : total;
    }

    // ============================================================
    //                          结算辅助
    // ============================================================

    /// <summary>
    /// 施加时计算真实层数。
    /// "残废""无力"写了：对精英怪和 BOSS 施加时层数减半（向上取整，单次至少 1 点）。
    /// </summary>
    /// <param name="target">被施加 buff 的单位</param>
    /// <param name="rawStacks">策划/技能原本要施加的层数</param>
    public virtual int CalcStacksOnApply(Unit target, int rawStacks)
    {
        // 最少也要有 1 层，否则等于没施加
        if (rawStacks < 1) rawStacks = 1;
        if (!halveStacksOnEliteAndBoss || target == null || target.data == null) return rawStacks;

        // 精英怪：层数减半，向上取整
        // TODO：UnitGrade 目前只有"小怪 / 精英怪"两档，还没有 BOSS。
        //       等 UnitData 补上 BOSS 档位后，这里把 BOSS 也一起判进来。
        if (target.data.grade == UnitGrade.精英怪)
        {
            return Mathf.Max(1, Mathf.CeilToInt(rawStacks / 2f));
        }

        return rawStacks;
    }

    /// <summary>
    /// 这个 buff 能不能挂到目标身上
    /// （力竭 / 混乱 写了"对敌人无效"，onlyForPlayerUnit 勾上后就只能给玩家）
    /// </summary>
    public virtual bool CanApplyTo(Unit target)
    {
        if (target == null) return false;
        if (onlyForPlayerUnit && !(target is PlayerUnit)) return false;
        return true;
    }

    /// <summary>
    /// 回合结束时，层数衰减到多少。
    /// 返回 0 表示这个 buff 该被移除了。
    /// </summary>
    public virtual int CalcStacksAfterTurnEnd(int currentStacks)
    {
        switch (ResolvedDecay)
        {
            case BuffStackDecay.回合结束减1:
                return currentStacks - 1;

            case BuffStackDecay.回合结束减半:
                // 流血：向上取整，但 1 层时会直接消失
                if (currentStacks <= 1) return 0;
                return Mathf.CeilToInt(currentStacks / 2f);

            case BuffStackDecay.回合结束消失:
                return 0;

            case BuffStackDecay.不减少:
            case BuffStackDecay.未叠加则清空:
            default:
                return currentStacks;
        }
    }

    /// <summary>
    /// "中毒"的判定：已经连续 turnsSinceLastStack 个回合没有再叠层时，
    /// 是否应该清空全部层数。
    /// </summary>
    public virtual bool ShouldClearForNoStack(int turnsSinceLastStack)
    {
        return ResolvedDecay == BuffStackDecay.未叠加则清空
            && clearIfNotStackedTurns > 0
            && turnsSinceLastStack >= clearIfNotStackedTurns;
    }

    /// <summary>
    /// 这个单位能不能被选为攻击目标（"亲密"用）。
    /// 返回 false 表示挂着这个 buff 的单位不能打 forbiddenAttackTargets 里的对象。
    /// </summary>
    public virtual bool IsForbiddenAttackTarget(Unit candidate)
    {
        if (candidate == null || candidate.data == null) return false;
        if (!HasEffectType(BuffEffectType.攻击目标限制)) return false;

        return forbiddenAttackTargets.Contains(candidate.data);
    }
}
