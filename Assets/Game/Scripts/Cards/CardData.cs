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
    需要准备,
    啃咬类
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

public enum DefenseTargetRule
{
    自身 = 0,
    单位 = 1,
    地块 = 2,
    多个单位 = 3
}

/// <summary>防御技能的特殊结算编号；已写入资产后不得重排。</summary>
public enum DefenseEffectRule
{
    普通 = 0,
    寻觅 = 1,
    指定治疗 = 2,
    神圣护盾 = 3,
    荧光汇聚 = 4,
    抱抱喷射 = 5,
    换位 = 6,
    坚守 = 7,
    延迟亲密 = 8,
    失血护盾 = 9,
    肌肉突变 = 10,
    自动屏障 = 11,
    精神摧毁 = 12,
    步行 = 13,
    跳跃 = 14,
    临时残废 = 15,
    投掷织毯 = 16,
    群体疗愈 = 17,
    克勒妮的呼唤 = 18,
    风飑 = 19,
    岚匿 = 20,
    飔垣 = 21,
    错乱传送 = 22,
    精神压制 = 23,
    毒遁 = 24,
    炎障 = 25,
    神圣祝福 = 26
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
    [Tooltip("伤害类型：物理=最普通伤害（先扣护盾）；真实=无视护甲(护盾)但可被减伤；Buff=中毒/灼烧/流血等持续伤害")]
    public DamageType damageType = DamageType.物理;
    [Tooltip("攻击卡可以选择哪些目标类型，可多选（只勾敌方单位就不能打障碍物）")]
    public List<CardTargetType> targetTypes = new List<CardTargetType> { CardTargetType.敌方单位 };

    [Header("位移设置（位移类卡牌填写）")]
    [Tooltip("勾选后这张牌结算时会造成一次位移")]
    public bool causesDisplacement = false;
    [Tooltip("位移类型：击退=远离施法者；拉近=靠近施法者；传送=闪现（需落点，走 Unit.TeleportTo）")]
    public DisplacementType displacementType = DisplacementType.击退;
    [Tooltip("位移格数")]
    public int displacementDistance = 1;
    [Tooltip("位移对象：false=被打中的目标单位；true=施法者自己（例如'远跳'这类自身位移）")]
    public bool displaceSelf = false;

    [Header("防御设置（防御卡填写）")]
    [Tooltip("防御效果类型：治疗=恢复生命，护盾=获得护盾")]
    public DefenseEffectType defenseEffectType = DefenseEffectType.护盾;
    [Tooltip("防御数值：治疗时=恢复血量，护盾时=护盾层数。如格挡(护盾)=2、群体疗愈(治疗)=3")]
    public int defenseValue = 0;
    [Tooltip("防御卡可以选择哪些目标类型，可多选")]
    public List<DefenseTargetType> defenseTargetTypes = new List<DefenseTargetType> { DefenseTargetType.自身 };
    [Tooltip("玩家先点击自己；如需第二步，再按此规则选择目标")]
    public DefenseTargetRule defenseTargetRule = DefenseTargetRule.自身;
    public DefenseEffectRule defenseEffectRule = DefenseEffectRule.普通;
    public int maxDefenseTargets = 1;
    [Tooltip("开局装入槽位，且不参与普通抽牌")]
    public bool initialOnly;
    public UnitData summonUnit;
    public UnitData alternateSummonUnit;
    public TerrainData generatedTerrain;

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
    /// 怪物行动优先级（对应文档"怪物行动顺序"）：
    /// 瞄准技能(0) → 带位移的攻击技能(1) → 位移技能(2) → 特技技能(3) → 纯攻击技能(4)。
    /// 数字越小越先释放；同优先级由 EnemyUnit 随机排序。
    /// </summary>
    public int SkillPriority
    {
        get
        {
            bool hasMove = tags.Contains(CardTag.位移);
            bool isAim = tags.Contains(CardTag.需要瞄准);
            bool isStunt = tags.Contains(CardTag.特技);

            if (isAim) return 0;                        // ① 瞄准技能
            if (hasMove && IsAttack) return 1;          // ② 带位移的攻击技能
            if (hasMove) return 2;                      // ③ 纯位移技能
            if (isStunt) return 3;                      // ④ 特技技能
            return 4;                                   // ⑤ 纯攻击技能 / 其它
        }
    }

    // ============================================================
    //         buff 生效后的"最终数值"（影响类 buff 在这里落地）
    //
    //   力量 / 无力 / 临时力量 → GetFinalDamage        （攻击伤害加成）
    //   虚弱                  → GetFinalActionCost    （攻击耐力消耗倍率）
    //   残废 / 敏捷            → GetEffectiveMaxRange  （移动距离加成）
    //
    //   易伤、受到伤害减免属于"挨打方"的结算，在 Unit.TakeDamage 里算，
    //   这里不重复计算（否则两边都乘一次，伤害会翻倍）。
    // ============================================================

    /// <summary>是不是位移类技能（带"位移"标签）</summary>
    public bool IsMovementSkill => tags.Contains(CardTag.位移);

    /// <summary>是不是传送类技能——残废/敏捷明确写了"不影响传送类"，所以位移修正要跳过它</summary>
    public bool IsTeleportSkill => tags.Contains(CardTag.传送);

    /// <summary>
    /// 攻击技能的最终伤害。
    /// 「攻击伤害加成」按层数求和后，加在这张牌配置的 damage 上
    /// （力量 +1 / 无力 -1 / 临时力量 +1）。
    /// 查询不限触发时机 —— 策划把效果填成"立即生效"或"攻击时"都会算进来。
    /// 下限钳到 0：无力叠得比 damage 还高时打不出伤害，但不会反过来变成治疗。
    /// </summary>
    public int GetFinalDamage(Unit caster, Unit target = null)
    {
        if (caster == null) return damage;

        int bonus = Mathf.RoundToInt(caster.GetTotalEffectValue(BuffEffectType.攻击伤害加成));

        // 遗物加成：
        //   ① 常驻属性类（攻击伤害加成 / 技能基础伤害加成），可按标签 / 稀有度 / 相邻过滤；
        //   ② 运行时成长类（玛塔的板牙：用啃咬累计击杀 → 永久加伤），由 RelicController 记账。
        int relicBonus = 0;
        if (caster is PlayerUnit relicOwner)
        {
            relicBonus = RelicData.GetAttackDamageBonus(relicOwner.Relics, this, caster, target);

            RelicController relicController = caster.GetComponent<RelicController>();
            if (relicController != null)
                relicBonus += relicController.GetSkillDamageBonus(this);
        }

        return Mathf.Max(0, damage + bonus + relicBonus);
    }

    /// <summary>
    /// 攻击技能的最终耐力消耗。
    /// 「攻击耐力消耗倍率」会放大耐力消耗（虚弱 = 1.25），向上取整；
    /// 防御技能不受影响（策划写的是"攻击技能的耐力消耗增加25%"）。
    /// 该类型是倍率型：没挂虚弱时查询返回 1（不变），叠层按 1+0.25×层数 合并。
    /// </summary>
    public int GetFinalActionCost(Unit caster)
    {
        if (actionCost <= 0 || caster == null) return actionCost;
        if (!IsAttack) return actionCost;   // 只影响攻击技能

        float rate = Mathf.Max(0f, caster.GetTotalEffectValue(BuffEffectType.攻击耐力消耗倍率));
        return Mathf.Max(0, Mathf.CeilToInt(actionCost * rate));
    }

    /// <summary>
    /// 位移类技能生效后的最大距离。
    /// 「移动距离加成」只作用于位移类（敏捷 +1 / 残废 -1），传送类不受影响。
    /// 非位移类技能原样返回 maxRange。
    /// </summary>
    public int GetEffectiveMaxRange(Unit caster)
    {
        if (caster == null || !IsMovementSkill || IsTeleportSkill) return maxRange;

        int result = maxRange + caster.GetMoveBonus();
        // 残废最多把距离压到"至少还能走 1 格"，同时不能低于最小射程
        return Mathf.Max(Mathf.Max(1, minRange), result);
    }

    /// <summary>
    /// 射程判定（带 buff 修正版）：位移类会算上敏捷/残废。
    /// 玩家出牌、敌人放技能都走这一个方法，避免三处各写一份射程判断。
    /// </summary>
    public bool IsInEffectiveRange(Unit caster, HexCoord from, HexCoord to)
    {
        int distance = from.Distance(to);
        return distance >= minRange && distance <= GetEffectiveMaxRange(caster);
    }

    /// <summary>
    /// 结算一张卡牌（目标为单位）——玩家出牌和敌人放技能都走这同一个方法。
    ///
    /// 前置兜底：
    /// 0. 施法者被"禁止行动"（眩晕）→ 直接返回，什么牌都放不出来；
    ///
    /// 攻击卡：
    /// 1. 允许打单位 + 有目标 → 按"最终伤害"造成伤害。
    ///    最终伤害 = 卡牌 damage + 力量/无力/临时力量等「攻击伤害加成」（见 GetFinalDamage）；
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
    /// 射程校验和耐力消耗不在这里做：
    ///   - 射程 → CardData.IsInEffectiveRange（位移类要算敏捷/残废）；
    ///   - 耐力 → CardData.GetFinalActionCost（虚弱要放大攻击技能消耗），
    ///           由调用方在扣费时取用。
    ///
    /// 位移/传送/召唤/群体治疗等特殊效果以后接入对应系统时在这里扩展。
    /// </summary>
    /// <param name="caster">施法者（打出这张牌的人，可能是玩家也可能是敌人）</param>
    /// <param name="target">目标单位（防御/自身增益卡可为 null 或就是 caster 自己）</param>
    public void Resolve(Unit caster, Unit target)
    {
        // 没有施法者就无法结算（防御性判空）
        if (caster == null) return;

        // 禁止行动（眩晕）：被控住的单位打不出任何牌。
        // 玩家侧 PlayerCardSystem.ValidateSlot 已经拦过一次，这里兜底，
        // 保证敌人走 ExecutePlannedSkills → Resolve 这条路径时同样受约束。
        if (!caster.CanAttack())
        {
            Debug.Log($"{caster.data.unitName} 被禁止行动，【{cardName}】无法释放");
            return;
        }

        // 目标是否为"敌人"（和施法者不是同一个单位）
        bool targetIsEnemy = target != null && target != caster && target.GetCurrentHealth() > 0;

        // ---------- 第 1 步：攻击卡造成伤害 ----------
        if (IsAttack && CanTargetUnit && targetIsEnemy)
        {
            // 亲密：施法者身上挂着"攻击目标限制"时，不能用被列入名单的单位当目标。
            // 正常流程在 PlayerCardSystem 选目标时就已拦下（且拦在扣费之前），
            // 这里再兜一道，防止有别的调用方绕过。
            if (caster.IsAttackTargetForbidden(target))
            {
                Debug.Log($"{caster.data.unitName} 无法将 {target.data.unitName} 选为攻击目标");
                return;
            }

            caster.OnAttack();   // 触发施法者身上"攻击时"的 buff

            // 伤害 = 卡牌配置的 damage + 力量/无力/临时力量等"攻击伤害加成"（按层数叠加）+ 遗物加成，
            // 并按配置的伤害类型结算（真实伤害无视护盾）。
            // 走 DamageContext 结算：带上"谁打的 / 用什么牌 / 近战还是远程"，
            // 让按来源判定的遗物（免疫爆炸、远程减伤、近战增伤）能正确生效。
            int finalDamage = GetFinalDamage(caster, target);
            DamageContext ctx = DamageContext.FromCard(caster, this, finalDamage, damageType);
            target.TakeDamage(ctx);

            Debug.Log($"{caster.data.unitName} 使用【{cardName}】对 {target.data.unitName} 造成 {finalDamage} 点伤害");
        }

        // ---------- 第 1.5 步：位移效果 ----------
        // 位移类卡牌（如"推"/"撞击"）：默认推被打中的目标，也可配置成推自己（如"远跳"）。
        // 传送需要指定落点，走 Unit.TeleportTo，不在这里处理。
        if (causesDisplacement && displacementType != DisplacementType.传送)
        {
            Unit subject = displaceSelf ? caster : target;
            if (subject != null && subject.GetCurrentHealth() > 0)
                subject.ApplyDisplacement(displacementType, displacementDistance, caster);
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

        // 关键：上面第 1 步的伤害结算可能已经改变了生死局面 ——
        //   · 目标可能被打死（这次攻击就再也不该给它挂中毒/无力了）；
        //   · 施法者自己也可能被反伤/反弹打死。
        // 而 Unity 的 Destroy 是延迟到帧末执行的，此时 C# 对象还活着，
        // 不重新判活就会给一个已死亡的单位挂上 buff，并广播出带尸体的 UI 事件。
        // 所以这里每次都重新取一次存活状态，而不是复用第 275 行的旧快照。
        foreach (var buff in buffs)
        {
            if (buff == null) continue;   // 跳过没配置的空条目

            if (buff.polarity == BuffPolarity.负面)
            {
                // 负面 buff 挂给敌方目标（中毒、无力等）
                // 目标和施法者都必须还活着才施加
                if (canDebuffEnemy && targetIsEnemy &&
                    caster.GetCurrentHealth() > 0 &&
                    target.GetCurrentHealth() > 0)
                    target.ApplyBuff(buff);
            }
            else
            {
                // 正面 buff 挂给自己（护盾以外的强化类）
                if (caster.GetCurrentHealth() > 0)
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

        // 禁止行动（眩晕）：被控住同样打不出牌（和单位版保持一致的兜底）
        if (!caster.CanAttack())
        {
            Debug.Log($"{caster.data.unitName} 被禁止行动，【{cardName}】无法释放");
            return;
        }

        // 障碍物只承受伤害：攻击卡 + 允许以障碍物为目标 + 障碍物存在
        // 伤害同样算上"攻击伤害加成"——打障碍物和打单位不应该有两套伤害公式
        if (IsAttack && CanTargetObstacle && obstacle != null)
        {
            caster.OnAttack();

            int finalDamage = GetFinalDamage(caster);
            obstacle.TakeDamage(finalDamage);

            string obstacleName = obstacle.data != null ? obstacle.data.obstacleName : "障碍物";
            Debug.Log($"{caster.data.unitName} 使用【{cardName}】对 {obstacleName} 造成 {finalDamage} 点伤害");
        }

        // 正面 buff 依然挂给施法者自己；负面 buff 对障碍物无意义，跳过
        // 施法者可能已被反伤打死（Destroy 延迟到帧末，这里必须重新判活）
        foreach (var buff in buffs)
        {
            if (buff == null) continue;
            if (buff.polarity != BuffPolarity.负面 && caster.GetCurrentHealth() > 0)
                caster.ApplyBuff(buff);
        }

        EventManager.Trigger("CardResolvedOnObstacle", this, caster, obstacle);
    }
}
