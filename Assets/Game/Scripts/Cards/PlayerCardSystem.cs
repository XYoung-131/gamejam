using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  这个文件是"玩家的手牌/技能槽系统"
//
//  它负责三件事：
//  ① 灌注（把新技能装进技能槽）：
//     - 方式 A：花思绪选攻击/防御牌库 → 6 张展示 → 随机抽 3 张 → 选 1 张装入，其余作废
//     - 方式 B：从某个敌人"上回合用过的技能"里选 1 张窃取而装（敌方特有不可偷）
//  ② 出牌（使用槽位里的技能）：消耗思绪 + 耐力，可无限次重复使用同一技能
//  ③ 把数据通过 EventManager 广播给 UI（槽位变化、待选卡牌等事件）
//
//  挂载位置：和 PlayerUnit 挂在同一个 GameObject 上
//  （脚本头部的 [RequireComponent] 会自动保证这一点）
// ============================================================

/// <summary>
/// 槽位中一张已灌注技能的运行时状态。
/// 存"这是哪张牌"和"还能用几次"：
/// - card.maxUses <= 0 时表示不限次数，remainingUses 不参与计算；
/// - card.maxUses > 0 时 remainingUses 每出一次牌减 1，归零后槽位腾空。
/// 加 [System.Serializable] 是为了能在 Inspector 里看到（虽然 slots 是 protected）。
/// </summary>
[System.Serializable]
public class CardSlot
{
    public CardData card;          // 槽位里的技能；为 null 表示这是个空槽位
    public int remainingUses;      // 剩余使用次数（仅当 card.maxUses > 0 时有意义）

    /// <summary>槽位是否为空（没有技能）</summary>
    public bool IsEmpty => card == null;

    /// <summary>这张牌还能不能出（不限次数 或 剩余次数 > 0）</summary>
    public bool CanUse => !IsEmpty && (card.maxUses <= 0 || remainingUses > 0);

    /// <summary>
    /// 剩余次数（给 UI 显示用）。
    /// 返回 -1 表示不限次数；返回 0 表示已用完/空槽；正数 = 剩余次数。
    /// </summary>
    public int RemainingForUI => IsEmpty ? 0 : (card.maxUses <= 0 ? -1 : remainingUses);
}

/// <summary>
/// 当前灌注选择的来源——相当于一次灌注操作的"状态标记"，
/// 用来防止玩家在选择过程中又发起另一次抽卡。
/// </summary>
public enum InfuseSource
{
    无,                // 不在灌注流程中
    攻击牌库,          // 当前待选牌来自攻击牌库抽卡
    防御牌库,          // 当前待选牌来自防御牌库抽卡
    敌方上回合技能      // 当前待选牌来自窃取敌人技能
}

/// <summary>
/// 玩家卡牌系统（详细规则见文件顶部注释）。
/// </summary>
[RequireComponent(typeof(PlayerUnit))]   // 挂载时自动带上 PlayerUnit，防止漏挂
public class PlayerCardSystem : MonoBehaviour
{
    // -------------------- Inspector 可调参数 --------------------

    [Header("槽位")]
    [Tooltip("技能槽位总数")]
    public int slotCount = 5;
    [Tooltip("战斗开始时预装到槽位里的技能（在 Inspector 拖入卡牌资产）")]
    public List<CardData> initialCards = new List<CardData>();

    [Header("抽卡规则")]
    [Tooltip("每次从牌库展示多少张")]
    public int offerCount = 6;           // 规则里的"6 张"
    [Tooltip("从展示的牌中随机抽出多少张供选择")]
    public int pickCount = 3;            // 规则里的"抽 3 张"
    [Tooltip("发起一次抽卡消耗的思绪（灌注确认时还要再支付卡牌本身的思绪费用）")]
    public int drawThoughtCost = 1;      // 注意：这是"开抽"的费用，选牌时还要再付卡费

    [Header("牌库（在 Inspector 中配置）")]
    [Tooltip("攻击牌库包含的所有技能")]
    public List<CardData> attackPool = new List<CardData>();
    [Tooltip("防御牌库包含的所有技能")]
    public List<CardData> defensePool = new List<CardData>();

    // -------------------- 运行时数据 --------------------

    protected PlayerUnit player;   // 同一物体上的玩家单位引用，用来扣思绪/耐力

    // 技能槽位列表，长度 = slotCount。用 List 而不是数组是为了增删方便
    protected readonly List<CardSlot> slots = new List<CardSlot>();

    // 本次抽出来等待玩家选择的卡牌（抽卡后是 3 张，窃取后是若干张）
    // 玩家 ConfirmInfuse 选一张，或 AbandonInfuse 全放弃，之后这个列表会清空
    protected readonly List<CardData> currentChoices = new List<CardData>();

    /// <summary>
    /// 当前选择来源（无 = 不在灌注流程中）。
    /// public get + protected set：外部只读，只有本类能修改。
    /// </summary>
    public InfuseSource Source { get; protected set; } = InfuseSource.无;

    /// <summary>槽位列表（只读暴露给 UI 渲染，UI 不能直接改）</summary>
    public IReadOnlyList<CardSlot> Slots => slots;

    /// <summary>当前待选卡牌（只读暴露给 UI 渲染）</summary>
    public IReadOnlyList<CardData> CurrentChoices => currentChoices;

    /// <summary>
    /// 是否存在空槽位。
    /// 规则要求：没有空槽位时无法抽取/窃取（必须先用或放弃，否则技能无处可放）。
    /// </summary>
    public bool HasEmptySlot
    {
        get
        {
            foreach (var slot in slots)
                if (slot.IsEmpty) return true;
            return false;
        }
    }

    /// <summary>是否正处于灌注选择中（待选列表非空时为 true）</summary>
    public bool IsChoosing => Source != InfuseSource.无;

    // ============================================================
    //                       初始化
    // ============================================================

    /// <summary>
    /// Unity 生命周期：物体创建时最先调用。
    /// 拿到 PlayerUnit 引用，并按 slotCount 建出一排空槽位。
    /// </summary>
    protected virtual void Awake()
    {
        player = GetComponent<PlayerUnit>();

        slots.Clear();
        for (int i = 0; i < slotCount; i++)
            slots.Add(new CardSlot());
    }

    /// <summary>
    /// Unity 生命周期：比 Awake 晚，在第一帧 Update 之前调用。
    /// 把 Inspector 里配置的初始技能依次装进空槽位，然后通知 UI 刷新。
    /// </summary>
    protected virtual void Start()
    {
        foreach (var card in initialCards)
            PutIntoFirstEmptySlot(card);

        EventManager.Trigger("SlotChanged", this);
    }

    // ============================================================
    //          方式 A：抽卡灌注（攻击/防御牌库，6 选 3 再选 1）
    //
    //  完整调用链（由 UI 按钮驱动）：
    //  BeginDraw(选牌库) → 玩家看到 currentChoices → ConfirmInfuse(序号) 装牌
    //                                          └→ AbandonInfuse() 全放弃
    // ============================================================

    /// <summary>
    /// 发起一次抽卡（对应玩家点击"攻击抽卡/防御抽卡"按钮）。
    /// 成功后 currentChoices 里会有 3 张待选牌，等玩家调用 ConfirmInfuse / AbandonInfuse。
    /// </summary>
    /// <param name="pool">从攻击牌库还是防御牌库抽</param>
    /// <returns>是否抽卡成功（阶段不对、槽位满、思绪不足等都会返回 false）</returns>
    public virtual bool BeginDraw(CardPool pool)
    {
        // --- 校验 1：必须在玩家行动阶段 ---
        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能抽卡");
            return false;
        }

        // --- 校验 2：上一次选择还没结束，不能同时开两次 ---
        if (IsChoosing)
        {
            Debug.Log("当前已有一次灌注选择未完成");
            return false;
        }

        // --- 校验 3：必须有空槽位（规则要求） ---
        if (!HasEmptySlot)
        {
            Debug.Log("没有空槽位，无法抽取");
            EventManager.Trigger("DrawDenied", this);   // 通知 UI 弹提示
            return false;
        }

        // 三目运算根据参数选出要用的牌库
        List<CardData> sourcePool = pool == CardPool.攻击 ? attackPool : defensePool;
        if (sourcePool == null || sourcePool.Count == 0)
        {
            // 策划忘了在 Inspector 配牌
            Debug.LogWarning($"{pool}牌库为空，请在 Inspector 中配置");
            return false;
        }

        // --- 支付"开抽"费用（思绪不够直接失败，后面的都不执行） ---
        if (!player.TrySpendThought(drawThoughtCost))
            return false;

        // --- 核心抽卡流程 ---
        // 1. 从牌库生成 6 张展示牌（内部已洗牌）
        List<CardData> offered = BuildOffer(sourcePool);

        // 2. 再洗一次，从 6 张里取前 3 张作为待选
        Shuffle(offered);
        currentChoices.Clear();
        for (int i = 0; i < pickCount && i < offered.Count; i++)
            currentChoices.Add(offered[i]);

        // 3. 记录本次来源，标记"正在选择中"
        Source = pool == CardPool.攻击 ? InfuseSource.攻击牌库 : InfuseSource.防御牌库;

        Debug.Log($"从{pool}牌库展示 {offered.Count} 张，抽出 {currentChoices.Count} 张待灌注");

        // 4. 广播事件，UI 收到后弹出 3 张牌让玩家点
        EventManager.Trigger("CardsOffered", currentChoices);
        return true;
    }

    // ============================================================
    //          方式 B：窃取灌注（拿敌人上回合用过的技能）
    // ============================================================

    /// <summary>
    /// 发起窃取（对应玩家点击某个敌人的"窃取"按钮）。
    /// 会读取该敌人上回合实际释放的技能列表，去重并过滤掉"敌方特有"技能。
    /// </summary>
    /// <param name="enemy">要偷技能的敌人</param>
    public virtual bool BeginStealFromEnemy(EnemyUnit enemy)
    {
        // 和抽卡一样的三道前置校验
        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能窃取");
            return false;
        }

        if (IsChoosing)
        {
            Debug.Log("当前已有一次灌注选择未完成");
            return false;
        }

        if (!HasEmptySlot)
        {
            Debug.Log("没有空槽位，无法抽取");
            EventManager.Trigger("DrawDenied", this);
            return false;
        }

        // 敌人必须存在且活着
        if (enemy == null || enemy.GetCurrentHealth() <= 0)
        {
            Debug.Log("目标敌人无效");
            return false;
        }

        // --- 筛选敌人上回合技能：去重 + 排除敌方特有 ---
        currentChoices.Clear();
        foreach (var card in enemy.LastTurnUsedSkills)
        {
            if (card == null) continue;
            if (card.rarity == Rarity.敌方特有) continue;   // 规则：敌方特有不能偷
            if (!currentChoices.Contains(card))              // 去重（同技能用两次只显示一次）
                currentChoices.Add(card);
        }

        // 筛完一张都没有（比如敌人上回合只用了特有技能）
        if (currentChoices.Count == 0)
        {
            Debug.Log("该敌人上回合没有可窃取的技能");
            currentChoices.Clear();
            return false;
        }

        Source = InfuseSource.敌方上回合技能;

        Debug.Log($"从敌人上回合的 {enemy.LastTurnUsedSkills.Count} 个技能中筛出 {currentChoices.Count} 个可选");
        EventManager.Trigger("CardsOffered", currentChoices);   // 和抽卡用同一个事件，UI 处理方式一致
        return true;
    }

    // ============================================================
    //                    确认 / 放弃灌注
    // ============================================================

    /// <summary>
    /// 玩家在待选列表里点了一张牌，确认灌注。
    /// 支付这张牌的思绪费用，装进第一个空槽位，其余牌全部作废。
    /// </summary>
    /// <param name="choiceIndex">玩家选中的牌在 currentChoices 里的序号（0 开始）</param>
    public virtual bool ConfirmInfuse(int choiceIndex)
    {
        if (!IsChoosing)
        {
            Debug.Log("当前没有进行中的灌注选择");
            return false;
        }

        // 序号越界保护
        if (choiceIndex < 0 || choiceIndex >= currentChoices.Count)
        {
            Debug.Log("选择的卡牌序号无效");
            return false;
        }

        // 理论上抽卡前已校验，这里再保险查一次（槽位可能在选择期间被别的逻辑占用）
        if (!HasEmptySlot)
        {
            Debug.Log("没有空槽位，无法灌注");
            return false;
        }

        CardData card = currentChoices[choiceIndex];

        // 支付这张牌本身的思绪费用。
        // 注意：如果思绪不足，这里直接 return false，但 currentChoices 不清空——
        // 玩家可以改选另一张更便宜的牌。
        if (!player.TrySpendThought(card.thoughtCost))
            return false;

        // 装入空槽位
        PutIntoFirstEmptySlot(card);

        Debug.Log($"灌注【{card.cardName}】成功，消耗思绪 {card.thoughtCost}");
        EventManager.Trigger("CardInfused", card);

        // 本次灌注结束：没被选中的牌全部作废
        CloseChoice();
        return true;
    }

    /// <summary>
    /// 玩家放弃灌注（点"都不要"）。待选牌全部作废，
    /// 但 BeginDraw 时已经花掉的"开抽"思绪不退还。
    /// </summary>
    public virtual void AbandonInfuse()
    {
        if (!IsChoosing) return;

        Debug.Log("玩家放弃了本次灌注，待选卡牌全部作废");
        CloseChoice();
    }

    /// <summary>
    /// 收尾一次灌注选择：清空待选、状态归位、通知 UI。
    /// ConfirmInfuse 和 AbandonInfuse 最后都会调这里。
    /// </summary>
    protected virtual void CloseChoice()
    {
        currentChoices.Clear();
        Source = InfuseSource.无;
        EventManager.Trigger("CardOfferClosed");   // 关闭选牌弹窗
        EventManager.Trigger("SlotChanged", this); // 刷新槽位显示
    }

    // ============================================================
    //                       使用槽位技能（出牌）
    // ============================================================

    /// <summary>
    /// 使用某个槽位里的技能（目标为单位）。
    /// 技能不限次数——只要通过所有校验并支付得起费用，就能一直用。
    /// </summary>
    /// <param name="slotIndex">槽位序号（0 开始）</param>
    /// <param name="target">
    /// 指定目标：UI 点击敌人时传入该敌人；
    /// 传 null 时攻击卡会自动选最近的敌人，防御/增益卡则对自己生效。
    /// </param>
    public virtual bool UseSlotCard(int slotIndex, Unit target = null)
    {
        // --- 公共前置校验（阶段/槽位/禁止行动） ---
        if (!ValidateSlot(slotIndex, out CardData card))
            return false;

        // 确定最终目标（攻击卡/对敌防御卡→敌人；自身防御卡→自己）
        Unit finalTarget = ResolveTarget(card, target);

        if (card.IsAttack)
        {
            // ---------- 攻击卡 ----------
            if (finalTarget == null)
            {
                Debug.Log("没有可攻击的目标");
                return false;
            }

            if (!card.CanTargetUnit)
            {
                Debug.Log($"【{card.cardName}】不能以敌方单位为目标");
                return false;
            }
        }
        else
        {
            // ---------- 防御卡 ----------
            if (finalTarget == player)
            {
                // 目标是自己：必须勾了"自身"
                if (!card.CanDefenseSelf)
                {
                    Debug.Log($"【{card.cardName}】不能对自身使用");
                    return false;
                }
            }
            else if (finalTarget != null)
            {
                // 目标是敌人：必须勾了"敌方单位"（精神压制、绊倒等）
                if (!card.CanDefenseEnemy)
                {
                    Debug.Log($"【{card.cardName}】不能以敌方单位为目标");
                    return false;
                }
            }
            else
            {
                Debug.Log($"【{card.cardName}】没有可用的目标类型");
                return false;
            }
        }

        // 射程校验：只要目标是"别人"（攻击卡打敌人 / 防御卡给敌人挂 debuff）就检查距离；
        // 对自己用的卡（距离 0）不需要射程
        if (finalTarget != null && finalTarget != player &&
            !IsInRange(card, player.GetCoord(), finalTarget.GetCoord()))
            return false;

        // --- 扣资源（思绪 + 耐力一起判断，任一不足则两种都不扣） ---
        if (!player.TrySpendResource(card.thoughtCost, card.actionCost))
            return false;

        // --- 结算卡牌效果（伤害/buff 都在 CardData.Resolve 里） ---
        card.Resolve(player, finalTarget);
        EventManager.Trigger("CardUsed", card, player, finalTarget);

        // 出牌成功后扣减使用次数（maxUses<=0 的无限卡不扣）
        DecrementSlotUse(slotIndex);
        return true;
    }

    /// <summary>
    /// 使用某个槽位里的技能（目标为障碍物）。
    /// UI 点击障碍物时调用。只有 targetTypes 里勾了"障碍物"的攻击卡才能用。
    /// </summary>
    /// <param name="slotIndex">槽位序号（0 开始）</param>
    /// <param name="obstacle">目标障碍物</param>
    public virtual bool UseSlotCardOnObstacle(int slotIndex, Obstacle obstacle)
    {
        // --- 公共前置校验 ---
        if (!ValidateSlot(slotIndex, out CardData card))
            return false;

        if (obstacle == null)
        {
            Debug.Log("目标障碍物不存在");
            return false;
        }

        // 只有攻击卡且允许打障碍物才行（防御/增益卡不能对障碍物用）
        if (!card.IsAttack || !card.CanTargetObstacle)
        {
            Debug.Log($"【{card.cardName}】不能以障碍物为目标");
            return false;
        }

        // 射程校验（单位和障碍物都用六边形坐标算距离）
        if (!IsInRange(card, player.GetCoord(), obstacle.GetCoord()))
            return false;

        // 扣资源
        if (!player.TrySpendResource(card.thoughtCost, card.actionCost))
            return false;

        // 结算（障碍物专用重载：只吃伤害，不吃负面 buff）
        card.Resolve(player, obstacle);
        EventManager.Trigger("CardUsedOnObstacle", card, player, obstacle);

        // 出牌成功后扣减使用次数
        DecrementSlotUse(slotIndex);
        return true;
    }

    /// <summary>
    /// 出牌成功后扣减槽位剩余使用次数。
    /// maxUses <= 0 的牌不限次数，不扣；次数归零时清空槽位（腾出位置给新技能）。
    /// </summary>
    protected virtual void DecrementSlotUse(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count) return;
        CardSlot slot = slots[slotIndex];
        if (slot.IsEmpty) return;

        CardData card = slot.card;
        if (card.maxUses <= 0) return;   // 不限次数，不扣

        slot.remainingUses--;
        if (slot.remainingUses <= 0)
        {
            Debug.Log($"【{card.cardName}】使用次数耗尽，槽位腾空");
            slot.card = null;            // 清空槽位
        }

        EventManager.Trigger("SlotChanged", this);   // 通知 UI 刷新次数显示
    }

    /// <summary>
    /// 出牌公共前置校验：阶段正确 + 槽位有效 + 没被禁止行动。
    /// 校验通过时把槽位里的卡通过 out 参数返回。
    /// </summary>
    protected virtual bool ValidateSlot(int slotIndex, out CardData card)
    {
        card = null;

        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能出牌");
            return false;
        }

        if (slotIndex < 0 || slotIndex >= slots.Count)
        {
            Debug.Log("槽位序号无效");
            return false;
        }

        CardSlot slot = slots[slotIndex];
        if (slot.IsEmpty)
        {
            Debug.Log("该槽位没有技能");
            return false;
        }

        card = slot.card;

        // 中了"禁止行动"类 buff（眩晕等）不能出牌
        if (player.HasEffect(BuffEffectType.禁止行动))
        {
            Debug.Log("被禁止行动，无法出牌");
            return false;
        }

        // 使用次数校验：maxUses > 0 时必须有剩余次数
        if (!slot.CanUse)
        {
            Debug.Log($"【{card.cardName}】使用次数已用完");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 射程判定：目标坐标与施法者坐标的六边形距离是否在 [minRange, maxRange] 内。
    /// 单位和障碍物共用（两者都有六边形坐标）。
    /// </summary>
    protected virtual bool IsInRange(CardData card, HexCoord from, HexCoord to)
    {
        int distance = from.Distance(to);
        if (distance < card.minRange || distance > card.maxRange)
        {
            Debug.Log($"目标距离 {distance} 格，【{card.cardName}】射程为 {card.minRange}~{card.maxRange} 格");
            return false;
        }

        return true;
    }

    // ============================================================
    //                       内部工具方法
    // （这些方法不直接给 UI 调，只服务于上面的流程）
    // ============================================================

    /// <summary>
    /// 判断现在能不能进行卡牌操作。
    /// 优先看 TurnManager 的精确阶段；没有 TurnManager 时退回用 GameManager 判断。
    /// （这样即使回合管理器没挂，脚本也能独立工作）
    /// </summary>
    protected virtual bool CanActNow()
    {
        if (TurnManager.Instance != null)
            return TurnManager.Instance.Phase == TurnPhase.玩家行动;

        return GameManager.Instance != null &&
               GameManager.Instance.IsInBattle &&
               GameManager.Instance.isPlayerTurn;
    }

    /// <summary>
    /// 把一张牌放进从左数第一个空槽位。
    /// 灌注和初始装牌都走这里。装入时把剩余次数初始化为 card.maxUses。
    /// </summary>
    protected virtual void PutIntoFirstEmptySlot(CardData card)
    {
        if (card == null) return;

        foreach (var slot in slots)
        {
            if (slot.IsEmpty)
            {
                slot.card = card;
                slot.remainingUses = card.maxUses;   // 初始化剩余次数（maxUses<=0 时为不限）
                EventManager.Trigger("SlotChanged", this);
                return;   // 找到一个空位装入后立刻结束
            }
        }
    }

    /// <summary>
    /// 决定一张牌最终对谁生效：
    /// - 攻击卡：指定了有效目标→用指定的；没指定→自动选最近敌人；不能打单位→null；
    /// - 防御卡：指定了存活敌人且允许"敌方单位"→该敌人（精神压制/绊倒）；
    ///           否则允许"自身"→对自己；都不允许→null。
    /// </summary>
    protected virtual Unit ResolveTarget(CardData card, Unit specified)
    {
        if (card.IsAttack)
        {
            if (!card.CanTargetUnit)
                return null;

            if (specified != null && specified.GetCurrentHealth() > 0)
                return specified;

            return FindNearestEnemy();
        }

        // 防御卡：优先看是否点了敌人
        if (card.CanDefenseEnemy && specified != null && specified.GetCurrentHealth() > 0)
            return specified;

        if (card.CanDefenseSelf)
            return player;

        // 允许对敌但没点目标：自动选最近敌人（方便快速操作）
        if (card.CanDefenseEnemy)
            return FindNearestEnemy();

        return null;
    }

    /// <summary>
    /// 在所有敌人里找六边形距离最近的存活敌人（自动索敌用）。
    /// 初始最小距离设为 int.MaxValue，保证任何真实距离都比它小。
    /// </summary>
    protected virtual EnemyUnit FindNearestEnemy()
    {
        EnemyUnit[] allEnemies = FindObjectsOfType<EnemyUnit>();
        EnemyUnit nearest = null;
        int minDist = int.MaxValue;

        foreach (var enemy in allEnemies)
        {
            if (enemy.GetCurrentHealth() <= 0) continue;   // 跳过尸体

            int dist = player.GetCoord().Distance(enemy.GetCoord());
            if (dist < minDist)
            {
                minDist = dist;
                nearest = enemy;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 从牌库生成 6 张"展示牌"。
    /// 规则：牌库数量够 6 张时尽量不重复；牌库不足 6 张时允许重复抽到同一张。
    /// （题目要求"可以抽到槽位中已有的技能"，所以这里只过滤空引用，不去重已有槽位）
    /// </summary>
    protected virtual List<CardData> BuildOffer(List<CardData> pool)
    {
        // 先把牌库复制一份并洗牌，避免改动原始列表顺序
        List<CardData> bag = new List<CardData>();
        foreach (var card in pool)
            if (card != null) bag.Add(card);
        Shuffle(bag);

        List<CardData> offered = new List<CardData>();
        for (int i = 0; i < offerCount; i++)
        {
            if (i < bag.Count)
                offered.Add(bag[i]);                         // 牌库还够：按洗牌后顺序取，不重复
            else
                offered.Add(bag[Random.Range(0, bag.Count)]); // 牌库不够：随机补，允许重复
        }

        return offered;
    }

    /// <summary>
    /// Fisher-Yates 洗牌算法——从后往前，每张牌和它前面随机一张交换位置。
    /// 这是经典的公平洗牌，每种排列概率相等。static：不依赖实例数据。
    /// </summary>
    protected static void Shuffle(List<CardData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);   // 在 [0, i] 里随机挑一个位置
            CardData temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    // ============================================================
    //  Inspector 右键测试方法（没有 UI 时，点组件右上角齿轮即可手动触发）
    //  [ContextMenu] 只影响编辑器菜单，打包后不会出现。
    // ============================================================

    [ContextMenu("测试：从攻击牌库抽卡")]
    protected void DebugDrawAttack() => BeginDraw(CardPool.攻击);

    [ContextMenu("测试：从防御牌库抽卡")]
    protected void DebugDrawDefense() => BeginDraw(CardPool.防御);

    [ContextMenu("测试：灌注待选第 1 张")]
    protected void DebugInfuseFirst() => ConfirmInfuse(0);

    [ContextMenu("测试：放弃灌注")]
    protected void DebugAbandon() => AbandonInfuse();

    [ContextMenu("测试：使用槽位 1")]
    protected void DebugUseSlot0() => UseSlotCard(0);
}
