using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  这个文件是"卡牌（技能）的配置表"
//
//  CardData 是一个 ScriptableObject（可在 Unity 编辑器里创建的资产文件），
//  项目里 ScriptableObjects/技能/ 文件夹下的每一个 .asset 文件，
//  比如"劈砍.asset""格挡.asset"，都是一张 CardData。
//
//  它只存"静态配置"（名字、消耗、效果），不存战斗中的临时状态。
//  真正打出这张牌时调用 Resolve() 结算效果。
// ============================================================

/// <summary>
/// 稀有度——决定卡牌的珍贵程度，也用来区分"敌方特有技能"
/// （敌方特有的牌玩家无法通过窃取获得）
/// </summary>
public enum Rarity
{
    普通,
    固有,
    稀有,
    传奇,
    敌方特有      // 敌人专属，玩家灌注时会被过滤掉
}

/// <summary>
/// 牌库类型——灌注抽卡时玩家要二选一：从攻击牌库抽，还是从防御牌库抽
/// </summary>
public enum CardPool
{
    攻击,
    防御
}

/// <summary>
/// 卡牌标签——一张牌可以同时带多个标签，用来做更细的规则判断
/// 例如"近战"标签会在出牌时检查目标是否相邻
/// </summary>
public enum CardTag
{
    近战,
    远程,
    位移,
    防御,
    特技,
    传送,
    需要瞄准,
    需要准备
}

/// <summary>
/// 攻击卡可以选择的目标类型（可多选）
/// </summary>
public enum CardTargetType
{
    敌方单位,   // 敌人（PlayerUnit / EnemyUnit 等 Unit）
    障碍物      // 地图上的 Obstacle，如炸药桶、柱子
}

/// <summary>
/// 防御卡可以选择的目标类型（可多选）
/// </summary>
public enum DefenseTargetType
{
    敌方单位,   // 对敌人使用的防御卡（如精神压制、绊倒——给敌人挂负面效果）
    自身        // 对自己使用的防御卡（如格挡——获得护盾）
}

/// <summary>
/// 防御卡的防御效果类型——决定 defenseValue 是治疗还是护盾
/// </summary>
public enum DefenseEffectType
{
    治疗,   // 恢复生命值（调用 Unit.Heal）
    护盾    // 获得护盾（调用 Unit.AddShield）
}

/// <summary>
/// 让 Unity 编辑器的右键菜单出现 "Game/Card Data" 选项，用来新建卡牌资产
/// </summary>
[CreateAssetMenu(fileName = "NewCard", menuName = "Game/Card Data")]
public class CardData : ScriptableObject
{
    [Header("基础信息")]
    public string cardName;          // 卡牌名称，如"劈砍"
    [TextArea] public string description;  // 卡牌描述（UI 上显示的说明文字）

    [Header("消耗")]
    [Tooltip("使用/灌注这张牌需要消耗的思绪")]
    public int thoughtCost;          // 思绪费用：灌注时付一次，每次出牌再付一次
    [Tooltip("使用这张牌需要消耗的耐力")]
    public int actionCost;           // 耐力费用：只在出牌时支付

    [Header("使用次数")]
    [Tooltip("这张牌装入槽位后能用的次数。0 或负数 = 不限次数；正数 N = 可用 N 次，用完槽位自动腾空")]
    public int maxUses = 0;          // 0/负数 = 不限；正数 = 可用 N 次

    [Header("射程（六边形格子距离）")]
    [Tooltip("最小射程（0 表示无最小限制）。例：'1格外3格内' 这里填 2")]
    public int minRange = 0;
    [Tooltip("最大射程（填 999 表示全场不限）。近战卡保持 1")]
    public int maxRange = 1;

    [Header("攻击设置（攻击卡填写）")]
    [Tooltip("造成的伤害数值（由策划填写）")]
    public int damage = 0;
    [Tooltip("攻击卡可以选择哪些目标类型，可多选（只勾敌方单位就不能打障碍物）")]
    public List<CardTargetType> targetTypes = new List<CardTargetType> { CardTargetType.敌方单位 };

    [Header("防御设置（防御卡填写）")]
    [Tooltip("防御效果类型：治疗=恢复生命，护盾=获得护盾")]
    public DefenseEffectType defenseEffectType = DefenseEffectType.护盾;
    [Tooltip("防御数值：治疗时=恢复血量，护盾时=护盾层数。如格挡(护盾)=2、群体疗愈(治疗)=3")]
    public int defenseValue = 0;
    [Tooltip("防御卡可以选择哪些目标类型，可多选")]
    public List<DefenseTargetType> defenseTargetTypes = new List<DefenseTargetType> { DefenseTargetType.自身 };

    [Header("分类")]
    [Tooltip("属于攻击牌库还是防御牌库（灌注抽卡用）")]
    public CardPool library = CardPool.攻击;   // 决定抽卡时从哪个牌库里被抽出来
    public Rarity rarity;            // 稀有度
    public List<CardTag> tags = new List<CardTag>();  // 标签列表，可多选

    [Header("相关Buff")]
    [Tooltip("打出这张牌时会附加的 buff：正面给施法者自己，负面给目标")]
    public List<BuffData> buffs = new List<BuffData>();

    [Header("内部备注")]
    [TextArea] public string devNotes;   // 策划备注，不会在游戏里显示

    // ============================================================
    //                       卡牌结算
    // ============================================================

    /// <summary>
    /// 是否为攻击卡。
    /// 判定方式（满足任意一个就算攻击卡）：
    /// 1. 这张牌被归在攻击牌库里；
    /// 2. 或者它带了"近战"/"远程"标签。
    /// 用属性 => 表达式写法，读取时实时计算，不需要单独存一个字段。
    /// </summary>
    public bool IsAttack =>
        library == CardPool.攻击 ||
        tags.Contains(CardTag.近战) ||
        tags.Contains(CardTag.远程);

    /// <summary>攻击卡能不能以敌方单位为目标</summary>
    public bool CanTargetUnit => targetTypes.Contains(CardTargetType.敌方单位);

    /// <summary>攻击卡能不能以障碍物为目标</summary>
    public bool CanTargetObstacle => targetTypes.Contains(CardTargetType.障碍物);

    /// <summary>防御卡能不能对自身使用（护盾/位移等）</summary>
    public bool CanDefenseSelf => defenseTargetTypes.Contains(DefenseTargetType.自身);

    /// <summary>防御卡能不能对敌方单位使用（精神压制、绊倒等负面效果）</summary>
    public bool CanDefenseEnemy => defenseTargetTypes.Contains(DefenseTargetType.敌方单位);

    /// <summary>
    /// 结算一张卡牌（目标为单位）——玩家出牌和敌人放技能都走这同一个方法。
    ///
    /// 攻击卡：
    /// 1. 允许打单位 + 有目标 → 按卡牌 damage 造成伤害；
    ///
    /// 防御卡：
    /// 2. 目标是自身且允许对自身使用 → 按 defenseValue 获得护盾（如格挡+2护盾）；
    /// 3. 目标是敌方单位且允许对敌使用 → 负面 buff（无力/残废等）挂给敌人；
    ///
    /// 通用：
    /// 4. 正面 buff（强化等）→ 始终挂给施法者自己；
    ///    负面 buff → 只有这张牌允许以敌方为目标时才挂给目标；
    /// 5. 广播 "CardResolved" 事件。
    ///
    /// 位移/传送/召唤/群体治疗等特殊效果以后接入对应系统时在这里扩展。
    /// </summary>
    /// <param name="caster">施法者（打出这张牌的人，可能是玩家也可能是敌人）</param>
    /// <param name="target">目标单位（防御/自身增益卡可为 null 或就是 caster 自己）</param>
    public void Resolve(Unit caster, Unit target)
    {
        // 没有施法者就无法结算（防御性判空）
        if (caster == null) return;

        // 目标是否为"敌人"（和施法者不是同一个单位）
        bool targetIsEnemy = target != null && target != caster && target.GetCurrentHealth() > 0;

        // ---------- 第 1 步：攻击卡造成伤害 ----------
        if (IsAttack && CanTargetUnit && targetIsEnemy)
        {
            caster.OnAttack();   // 触发施法者身上"攻击时"的 buff（例如加伤害）

            // 伤害值取卡牌上配置的 damage（策划逐张填写）
            target.TakeDamage(damage);

            Debug.Log($"{caster.data.unitName} 使用【{cardName}】对 {target.data.unitName} 造成 {damage} 点伤害");
        }

        // ---------- 第 2 步：防御卡对自身生效（治疗或护盾） ----------
        // 目标是自己 + 允许自身目标 + 防御数值大于 0
        if (!IsAttack && CanDefenseSelf && target == caster && defenseValue > 0)
        {
            if (defenseEffectType == DefenseEffectType.治疗)
            {
                caster.Heal(defenseValue);
                Debug.Log($"{caster.data.unitName} 使用【{cardName}】恢复 {defenseValue} 点生命");
            }
            else   // 护盾
            {
                caster.AddShield(defenseValue);
                Debug.Log($"{caster.data.unitName} 使用【{cardName}】获得 {defenseValue} 点护盾");
            }
        }

        // ---------- 第 3 步：结算卡牌携带的 buff ----------
        // 这张牌能不能把负面 buff 挂到敌人身上：
        // 攻击卡勾选了"敌方单位"，或防御卡勾选了"敌方单位"（精神压制/绊倒）
        bool canDebuffEnemy = (IsAttack && CanTargetUnit) || (!IsAttack && CanDefenseEnemy);

        foreach (var buff in buffs)
        {
            if (buff == null) continue;   // 跳过没配置的空条目

            if (buff.polarity == BuffPolarity.负面)
            {
                // 负面 buff 挂给敌方目标（中毒、无力等）
                if (canDebuffEnemy && targetIsEnemy)
                    target.ApplyBuff(buff);
            }
            else
            {
                // 正面 buff 挂给自己（护盾以外的强化类）
                caster.ApplyBuff(buff);
            }
        }

        // ---------- 第 4 步：广播事件 ----------
        EventManager.Trigger("CardResolved", this, caster, target);
    }

    /// <summary>
    /// 结算一张卡牌（目标为障碍物）。
    /// 障碍物不吃 buff、没有攻击力概念，只承受卡牌配置的 damage。
    /// 正面 buff（如自身增益）仍然正常挂给施法者。
    /// </summary>
    /// <param name="caster">施法者</param>
    /// <param name="obstacle">目标障碍物</param>
    public void Resolve(Unit caster, Obstacle obstacle)
    {
        if (caster == null) return;

        // 障碍物只承受伤害：攻击卡 + 允许以障碍物为目标 + 障碍物存在
        if (IsAttack && CanTargetObstacle && obstacle != null)
        {
            caster.OnAttack();
            obstacle.TakeDamage(damage);

            string obstacleName = obstacle.data != null ? obstacle.data.obstacleName : "障碍物";
            Debug.Log($"{caster.data.unitName} 使用【{cardName}】对 {obstacleName} 造成 {damage} 点伤害");
        }

        // 正面 buff 依然挂给施法者自己；负面 buff 对障碍物无意义，跳过
        foreach (var buff in buffs)
        {
            if (buff == null) continue;
            if (buff.polarity != BuffPolarity.负面)
                caster.ApplyBuff(buff);
        }

        EventManager.Trigger("CardResolvedOnObstacle", this, caster, obstacle);
    }
}
