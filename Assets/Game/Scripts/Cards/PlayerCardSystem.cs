using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  这个文件是"玩家的手牌/技能槽系统"
//
//  它负责三件事：
//  ① 灌注（把新技能装进技能槽）：
//     - 方式 A：花思绪选攻击/防御牌库 → 出 3 张卡 → 选 1 张装入，其余作废
//     - 方式 B：从某个敌人"上回合用过的技能"里选 1 张窃取而装（敌方特有不可偷）
//  ② 出牌（使用槽位里的技能）：只扣耐力和技能使用次数，不扣思绪
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
/// 一次"免费使用技能"的运行时状态（药水 / 遗物发进来的，如"快走饮料"）。
///
/// 和 CardSlot 的区别：
/// - 不占技能槽位（不需要空的槽位，也不会把槽位挤掉）；
/// - 不用抽牌和灌注，因此不付获取技能的思绪；
/// - 使用时也不扣耐力；
/// - 用完 remainingUses 次就整条消失。
/// </summary>
[System.Serializable]
public class FreeCardCharge
{
    public CardData card;          // 免费技能；为 null 表示这条已作废
    public int remainingUses;      // 还剩几次免费
    public string source;          // 来源说明（UI 上显示"来自【快走饮料】"）

    /// <summary>这条免费额度是不是已经用完（没技能 或 次数归零）</summary>
    public bool IsEmpty => card == null || remainingUses <= 0;
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
/// 一次性直接灌注的结果（左侧敌方技能卡面点击"确认"后的反馈）。
/// </summary>
public enum InfuseResult
{
    成功,
    槽位已满,      // 三个技能槽都装着技能
    思绪不足,
    非玩家回合,
    选牌进行中,    // 上一次抽卡/窃取还没确认完
    无效卡牌       // card == null
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
    [Tooltip("每次从牌库展示多少张（文档：选择牌堆后出现 3 张卡）")]
    public int offerCount = 3;           // 文档："选择牌堆后，出现3张卡"
    [Tooltip("从展示的牌中随机抽出多少张供选择（与 offerCount 相同 = 展示几张就选几张，不再二次抽）")]
    public int pickCount = 3;
    [Tooltip("发起一次抽卡消耗的思绪（文档：抽牌要消耗 5 点思绪。灌注确认时还要再支付卡牌本身的思绪费用）")]
    public int drawThoughtCost = 5;      // 注意：这是"开抽"的费用，选牌时还要再付卡费

    [Header("牌库（在 Inspector 中配置）")]
    [Tooltip("攻击牌库包含的所有技能")]
    public List<CardData> attackPool = new List<CardData>();
    [Tooltip("防御牌库包含的所有技能")]
    public List<CardData> defensePool = new List<CardData>();

    // -------------------- 运行时数据 --------------------

    protected PlayerUnit player;   // 同一物体上的玩家单位引用，用来支付抽牌、灌注和出牌费用

    // 技能槽位列表，长度 = slotCount。用 List 而不是数组是为了增删方便
    protected readonly List<CardSlot> slots = new List<CardSlot>();

    // 本次抽出来等待玩家选择的卡牌（抽卡后是 3 张，窃取后是若干张）
    // 玩家 ConfirmInfuse 选一张，或 AbandonInfuse 全放弃，之后这个列表会清空
    protected readonly List<CardData> currentChoices = new List<CardData>();

    // 药水 / 遗物塞进来的"免费技能"（不占槽位、不花资源）。
    // 目前只有"快走饮料"在用：免费使用 1 次"快走"。
    protected readonly List<FreeCardCharge> freeCards = new List<FreeCardCharge>();

    /// <summary>
    /// 当前选择来源（无 = 不在灌注流程中）。
    /// public get + protected set：外部只读，只有本类能修改。
    /// </summary>
    public InfuseSource Source { get; protected set; } = InfuseSource.无;

    /// <summary>槽位列表（只读暴露给 UI 渲染，UI 不能直接改）</summary>
    public IReadOnlyList<CardSlot> Slots => slots;

    /// <summary>
    /// 把所有槽位技能的使用次数补满到卡牌配置的上限（「青柠饮料」用）。
    ///
    /// 规则：
    ///  - 空槽位跳过；
    ///  - maxUses &lt;= 0（不限次数）的技能无所谓"用完"，跳过；
    ///  - 已经满次数的槽位不动，只统计真正被补过的。
    /// </summary>
    /// <returns>被补满的槽位数量</returns>
    public int RestoreAllSkillUses()
    {
        int restored = 0;
        foreach (CardSlot slot in slots)
        {
            if (slot == null || slot.IsEmpty) continue;
            if (slot.card == null || slot.card.maxUses <= 0) continue;
            if (slot.remainingUses >= slot.card.maxUses) continue;

            slot.remainingUses = slot.card.maxUses;
            restored++;
        }

        if (restored > 0)
            EventManager.Trigger("CardUsesRestored", restored);

        return restored;
    }

    /// <summary>当前待选卡牌（只读暴露给 UI 渲染）</summary>
    public IReadOnlyList<CardData> CurrentChoices => currentChoices;

    /// <summary>
    /// 当前持有的"免费技能"额度（只读暴露给 UI 渲染）。
    /// 药水 / 遗物发放，不需要占技能槽位，用掉一次就少一条。
    /// </summary>
    public IReadOnlyList<FreeCardCharge> FreeCards => freeCards;

    /// <summary>还有没有免费技能可用（UI 用它决定要不要显示"免费技能"按钮）</summary>
    public bool HasFreeCard
    {
        get
        {
            foreach (var charge in freeCards)
                if (!charge.IsEmpty) return true;
            return false;
        }
    }

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
    //          与饮料（药水）系统的接线（事件驱动）
    // ============================================================

    private void OnEnable()
    {
        // "快走饮料"等免费技能：PotionData 基类广播 FreeSkillGranted(喝的人, 技能)，
        // 这里翻译成"免费卡"额度——不占槽位、不付抽牌、灌注和使用费用、用完即走。
        EventManager.AddListener<Unit, CardData>("FreeSkillGranted", HandleFreeSkillGranted);
        EventManager.AddListener<int>("LevelStarted", HandleSkillLevelStarted);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<Unit, CardData>("FreeSkillGranted", HandleFreeSkillGranted);
        EventManager.RemoveListener<int>("LevelStarted", HandleSkillLevelStarted);
    }

    private void HandleSkillLevelStarted(int level)
    {
        if (player != null) player.ResetSkillLevelState();
    }

    /// <summary>
    /// 收到"免费使用技能"事件 → 发一条免费额度。
    /// 次数固定 1 次（策划原文"免费使用1次快走"）；
    /// 出牌时的射程 / 目标 / 视线校验照走。
    /// </summary>
    private void HandleFreeSkillGranted(Unit user, CardData skill)
    {
        if (skill == null) return;
        GrantFreeCard(skill, 1, "饮料");
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

        // --- 核心抽卡流程 ---
        // 1. 先过滤旧场景或手工配置中混入的敌方专属牌、错误牌库卡牌。
        //    无可用牌时不能先扣开抽费用。
        List<CardData> offered = BuildOffer(sourcePool, pool);
        if (offered.Count == 0)
        {
            Debug.LogWarning($"{pool}牌库没有玩家可抽取的技能");
            return false;
        }

        // --- 支付"开抽"费用（思绪不够直接失败，后面的都不执行） ---
        if (!player.TrySpendThought(drawThoughtCost))
            return false;

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
    //          方式 C：直接灌注（左侧敌方技能卡面点击确认）
    // ============================================================

    /// <summary>
    /// 不经过抽卡/待选流程，把一张指定技能直接装进第一个空槽位
    /// （对应 UI：点击左侧敌方技能卡面 → 确认"花费思绪灌注"）。
    /// 只扣这张卡的思绪费用，不占抽卡次数；槽位装的是同一张卡牌资产 +
    /// 各槽独立的剩余次数，等价于"复制了一份技能"。
    /// </summary>
    public virtual InfuseResult TryInfuseCardDirectly(CardData card)
    {
        if (card == null) return InfuseResult.无效卡牌;
        if (!CanActNow()) return InfuseResult.非玩家回合;
        if (IsChoosing) return InfuseResult.选牌进行中;
        if (!HasEmptySlot)
        {
            Debug.Log("直接灌注失败：技能槽已满");
            return InfuseResult.槽位已满;
        }

        if (!player.TrySpendThought(card.thoughtCost))
        {
            Debug.Log($"直接灌注失败：思绪不足（需要 {card.thoughtCost}）");
            return InfuseResult.思绪不足;
        }

        PutIntoFirstEmptySlot(card);
        Debug.Log($"直接灌注【{card.cardName}】成功，消耗思绪 {card.thoughtCost}");
        EventManager.Trigger("CardInfused", card);
        return InfuseResult.成功;
    }

    // ============================================================
    //                    确认 / 放弃灌注
    // ============================================================

    /// <summary>
    /// 玩家在待选列表里点了一张牌，确认灌注。
    /// 只支付这张牌的思绪费用，装进第一个空槽位，其余牌全部作废。
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

        // 灌注只扣思绪；不足时保留待选牌，玩家可以改选。
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
    /// 装入槽位后使用时只支付耐力；仍需符合目标、行动状态与使用次数限制。
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

        if (!card.IsAttack)
        {
            IReadOnlyList<HexCoord> choice = target != null && target != player
                ? new[] { target.GetCoord() } : new HexCoord[0];
            return TryUseDefenseCardOnCells(slotIndex, choice);
        }

        if (card.IsAttack && card.attackTargetRule != AttackTargetRule.单位)
        {
            Debug.Log($"【{card.cardName}】需要按范围选格使用");
            return false;
        }

        // 确定最终目标（攻击卡/对敌防御卡→敌人；自身防御卡→自己）
        Unit finalTarget = ResolveTarget(card, target);

        // 目标类型 + 射程/亲密/视线三道校验（和免费技能共用，见 ValidateCardTarget 注释）
        if (!ValidateCardTarget(card, finalTarget)) return false;
        if (!ValidateCardTargeting(card, finalTarget)) return false;

        int staminaCost = card.GetPlayActionCost(player);
        if (!player.TrySpendStamina(staminaCost)) return false;

        // --- 结算卡牌效果（伤害/buff 都在 CardData.Resolve 里） ---
        // 结算前广播一次：让遗物（玛塔的板牙：击杀归属统计）知道"现在用的是哪张牌"
        EventManager.Trigger("PlayerCardAboutToResolve", card, player);
        AttackOutcome outcome = card.ResolveWithOutcome(player, finalTarget);
        if (card.attackEffectRule == AttackEffectRule.飞身刺击冲刺 && outcome.KilledTarget)
            player.RestoreStamina(AttackEffectMath.HalfRefund(staminaCost));
        EventManager.Trigger("CardUsed", card, player, finalTarget);

        // 出牌成功后扣减使用次数（maxUses<=0 的无限卡不扣）
        bool keepUse = (card.attackEffectRule == AttackEffectRule.击杀返还使用次数 &&
                        outcome.KilledTarget) ||
                       (card.attackEffectRule == AttackEffectRule.低血量增伤概率保留次数 &&
                        AttackEffectMath.RollSucceeds(Random.value, 0.25f));
        if (!keepUse) DecrementSlotUse(slotIndex);
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

        if (card.attackTargetRule != AttackTargetRule.单位)
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

        // 视线校验（远程技能要求中间没有遮挡）
        if (!IsTargetReachable(card, player.GetCoord(), obstacle.GetCoord()))
            return false;

        if (!player.TrySpendStamina(card.GetPlayActionCost(player))) return false;

        // 结算（障碍物专用重载：只吃伤害，不吃负面 buff）
        // 结算前同样广播一次，保持和"打单位"路径一致
        EventManager.Trigger("PlayerCardAboutToResolve", card, player);
        card.Resolve(player, obstacle);
        EventManager.Trigger("CardUsedOnObstacle", card, player, obstacle);

        // 出牌成功后扣减使用次数
        DecrementSlotUse(slotIndex);
        return true;
    }

    /// <summary>
    /// 使用按格子确定范围的攻击牌。多目标传多个坐标；地块/穿透/连锁传一个坐标；
    /// 自身范围和全场技能传空列表。一次释放只扣一次耐力和技能使用次数。
    /// </summary>
    public virtual bool UseSlotCardOnCells(int slotIndex, IReadOnlyList<HexCoord> selectedCells)
    {
        if (!ValidateSlot(slotIndex, out CardData card) || HexGrid.Instance == null || selectedCells == null)
            return false;
        if (card.attackTargetRule == AttackTargetRule.单位)
            return false;
        if (!card.IsAttack || !card.CanTargetUnit) return false;

        var area = new List<AttackAreaCell>();
        Unit firstTarget = null;
        if (card.attackTargetRule == AttackTargetRule.多个单位)
        {
            int maxTargets = Mathf.Max(1, card.maxSelectedTargets);
            if (selectedCells.Count < 1 || selectedCells.Count > maxTargets) return false;
            var unique = new HashSet<HexCoord>();
            foreach (HexCoord coord in selectedCells)
            {
                if (!unique.Add(coord)) return false;
                if (!card.CanSelectAttackCell(player, coord)) return false;
                HexCell cell = HexGrid.Instance.GetCell(coord);
                Unit target = cell.occupyUnit;
                if (!ValidateCardTargeting(card, target)) return false;
                if (firstTarget == null) firstTarget = target;
                area.Add(new AttackAreaCell(coord, card.damage));
            }
        }
        else if (card.attackTargetRule == AttackTargetRule.相邻地块定向扇形)
        {
            if (selectedCells.Count != 1) return false;
            HexCoord chosen = selectedCells[0];
            if (HexGrid.Instance.GetCell(chosen) == null || player.GetCoord().Distance(chosen) != 1)
                return false;
            area.AddRange(card.GetAttackArea(player, chosen));
        }
        else if (card.attackTargetRule == AttackTargetRule.地块 ||
                 card.attackTargetRule == AttackTargetRule.穿透单位 ||
                 card.attackTargetRule == AttackTargetRule.连锁单位)
        {
            if (selectedCells.Count != 1 || !card.CanSelectAttackCell(player, selectedCells[0])) return false;
            HexCoord chosen = selectedCells[0];
            if (card.tags.Contains(CardTag.远程) && !card.ignoresLineBlock &&
                (HexGrid.Instance.IsLineBlocked(player.GetCoord(), chosen) || HexGrid.Instance.IsCellSmoky(chosen))) return false;
            HexCell cell = HexGrid.Instance.GetCell(chosen);
            firstTarget = cell != null ? cell.occupyUnit : null;
            area.AddRange(card.GetAttackArea(player, chosen));
        }
        else if (card.attackTargetRule == AttackTargetRule.自身范围 ||
                 card.attackTargetRule == AttackTargetRule.全场 ||
                 card.attackTargetRule == AttackTargetRule.全场随机 ||
                 card.attackTargetRule == AttackTargetRule.固定地图图案)
        {
            if (selectedCells.Count != 0) return false;
            area.AddRange(card.GetAttackArea(player, player.GetCoord(), Random.Range(0, 4)));
        }
        else
        {
            return false;
        }

        if (!player.TrySpendStamina(card.GetPlayActionCost(player))) return false;

        EventManager.Trigger("PlayerCardAboutToResolve", card, player);
        card.ResolveArea(player, area);
        EventManager.Trigger("CardUsed", card, player, firstTarget);
        DecrementSlotUse(slotIndex);
        return true;
    }

    public virtual bool TryUseDefenseCardOnCells(int slotIndex, IReadOnlyList<HexCoord> selected)
    {
        if (!ValidateSlot(slotIndex, out CardData card) || card.IsAttack ||
            !DefenseSkillResolver.CanResolve(card, player, selected)) return false;
        int staminaCost = player.DefenseState.GetStaminaCost(card, player);
        if (!player.TrySpendStamina(staminaCost)) return false;
        EventManager.Trigger("PlayerCardAboutToResolve", card, player);
        if (!DefenseSkillResolver.Resolve(card, player, selected))
        {
            player.RestoreStamina(staminaCost);
            return false;
        }
        Unit target = selected.Count > 0 && HexGrid.Instance != null
            ? HexGrid.Instance.GetCell(selected[0])?.occupyUnit : player;
        EventManager.Trigger("CardUsed", card, player, target);
        DecrementSlotUse(slotIndex);
        return true;
    }

    // ============================================================
    //        基础技能（推 / 跳：常驻按钮，不占槽位、不限次数）
    // ============================================================

    /// <summary>
    /// 使用基础攻击技能（如"推"）：不抽牌、不占槽位、不限次数，但**照常扣耐力**。
    /// 阶段 / 禁止行动 / 目标类型 / 射程 / 亲密 / 视线六道校验与槽位技能完全共用，
    /// 避免出现"常驻技能能打到槽位技能打不到的东西"。
    /// </summary>
    /// <param name="card">基础技能卡（推）</param>
    /// <param name="target">目标单位；传 null 时按 ResolveTarget 自动选</param>
    public virtual bool UseBasicAttack(CardData card, Unit target = null)
    {
        if (card == null || !card.IsAttack) return false;
        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能使用技能");
            return false;
        }
        if (player.HasEffect(BuffEffectType.禁止行动))
        {
            Debug.Log("被禁止行动，无法使用技能");
            return false;
        }
        if (card.attackTargetRule != AttackTargetRule.单位)
        {
            Debug.Log($"【{card.cardName}】不是单体技能，无法走基础技能入口");
            return false;
        }

        Unit finalTarget = ResolveTarget(card, target);
        if (!ValidateCardTarget(card, finalTarget)) return false;
        if (!ValidateCardTargeting(card, finalTarget)) return false;

        int staminaCost = card.GetPlayActionCost(player);
        if (!player.TrySpendStamina(staminaCost)) return false;

        EventManager.Trigger("PlayerCardAboutToResolve", card, player);
        AttackOutcome outcome = card.ResolveWithOutcome(player, finalTarget);
        if (card.attackEffectRule == AttackEffectRule.飞身刺击冲刺 && outcome.KilledTarget)
            player.RestoreStamina(AttackEffectMath.HalfRefund(staminaCost));
        EventManager.Trigger("CardUsed", card, player, finalTarget);
        return true;
    }

    /// <summary>
    /// 使用基础防御/位移技能（如"跳"）：按格子使用，不占槽位、不限次数，照常扣耐力。
    /// 校验与结算全部交给 DefenseSkillResolver，和槽位里的防御卡同一条路径
    /// （"跳" = DefenseEffectRule.跳跃 → Unit.TeleportTo）。
    /// </summary>
    /// <param name="card">基础技能卡（跳）</param>
    /// <param name="selected">选中的落点；"跳"只需 1 个格子</param>
    public virtual bool UseBasicDefense(CardData card, IReadOnlyList<HexCoord> selected)
    {
        if (card == null || card.IsAttack || HexGrid.Instance == null || selected == null) return false;
        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能使用技能");
            return false;
        }
        if (player.HasEffect(BuffEffectType.禁止行动))
        {
            Debug.Log("被禁止行动，无法使用技能");
            return false;
        }
        if (!DefenseSkillResolver.CanResolve(card, player, selected)) return false;

        int staminaCost = player.DefenseState.GetStaminaCost(card, player);
        if (!player.TrySpendStamina(staminaCost)) return false;

        EventManager.Trigger("PlayerCardAboutToResolve", card, player);
        if (!DefenseSkillResolver.Resolve(card, player, selected))
        {
            player.RestoreStamina(staminaCost);   // 结算失败要退费
            return false;
        }
        Unit target = selected.Count > 0 ? HexGrid.Instance.GetCell(selected[0])?.occupyUnit : player;
        EventManager.Trigger("CardUsed", card, player, target);
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
    /// 目标类型校验：这张牌能不能对"最终选中的这个目标"用。
    ///   · 攻击卡：必须有目标（没目标就没得打），且 targetTypes 里勾了"敌方单位"；
    ///   · 防御卡：目标是自己是必须勾"自身"；目标是敌人必须勾"敌方单位"；
    ///             两边都没勾、目标也是空 → 这张牌没有可用目标类型。
    ///
    /// 抽出来是因为"槽位里的技能"和"药水发的免费技能"必须用同一套规则，
    /// 否则会出现"免费技能能打到槽位技能打不到的东西"这种怪事。
    /// </summary>
    protected virtual bool ValidateCardTarget(CardData card, Unit finalTarget)
    {
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

            return true;
        }

        // ---------- 防御卡 ----------
        if (finalTarget == player)
        {
            // 目标是自己：必须勾了"自身"
            if (!card.CanDefenseSelf)
            {
                Debug.Log($"【{card.cardName}】不能对自身使用");
                return false;
            }

            return true;
        }

        if (finalTarget != null)
        {
            // 目标是敌人：必须勾了"敌方单位"（精神压制、绊倒等）
            if (!card.CanDefenseEnemy)
            {
                Debug.Log($"【{card.cardName}】不能以敌方单位为目标");
                return false;
            }

            return true;
        }

        Debug.Log($"【{card.cardName}】没有可用的目标类型");
        return false;
    }

    /// <summary>
    /// 射程 / 亲密 / 视线三道校验。只有目标是"别人"时才查
    /// （攻击卡打敌人、防御卡给敌人挂 debuff）；对自己用的卡（距离 0）直接通过。
    ///
    /// 顺序有讲究：亲密必须拦在扣费之前，否则会出现"扣了耐力却打不出去"。
    /// </summary>
    protected virtual bool ValidateCardTargeting(CardData card, Unit finalTarget)
    {
        if (finalTarget == null || finalTarget == player) return true;

        // 射程
        if (!IsInRange(card, player.GetCoord(), finalTarget.GetCoord()))
            return false;
        if (card.IsAttack && !card.CanSelectAttackCell(player, finalTarget.GetCoord()))
            return false;
        if (card.IsAttack && card.requiresStraightLine &&
            AttackAreaGeometry.StraightDirection(player.GetCoord(), finalTarget.GetCoord()) < 0)
            return false;

        // "亲密"（攻击目标限制）：名单里的单位不能被选为攻击目标
        if (player.IsAttackTargetForbidden(finalTarget))
        {
            Debug.Log($"【{card.cardName}】无法将 {finalTarget.data.unitName} 选为攻击目标");
            return false;
        }

        // 视线：远程技能要求中间没有单位/障碍物，且目标不在烟雾里
        if (!IsTargetReachable(card, player.GetCoord(), finalTarget.GetCoord()))
            return false;

        return true;
    }

    // ============================================================
    //              免费技能（药水 / 遗物发放，如"快走饮料"）
    // ============================================================

    /// <summary>
    /// 发放"免费使用技能"次数（"快走饮料" = 免费使用 1 次"快走"）。
    ///
    /// 为什么不做成"立刻自动放出来"：位移类技能要玩家自己挑落点和距离，
    /// 自动放等于替玩家做决定。所以改成发一张"免费卡"挂在这儿，
    /// 玩家点一下才用掉一次——出牌时的射程 / 目标 / 视线校验照走。
    /// </summary>
    /// <param name="card">免费开放的技能</param>
    /// <param name="uses">免费次数（默认 1 次）</param>
    /// <param name="source">来源说明，UI 上显示"来自【快走饮料】"，可留空</param>
    public virtual void GrantFreeCard(CardData card, int uses = 1, string source = null)
    {
        if (card == null || uses <= 0) return;

        // 同一张牌已经发过就往上叠次数，避免 UI 上出现两条一模一样的
        foreach (var charge in freeCards)
        {
            if (charge.card != card) continue;

            charge.remainingUses += uses;
            charge.source = source;   // 后发的来源覆盖旧的，UI 显示最近一次
            EventManager.Trigger("FreeCardChanged", this);
            Debug.Log($"免费技能【{card.cardName}】次数 +{uses}，当前共 {charge.remainingUses} 次");
            return;
        }

        freeCards.Add(new FreeCardCharge
        {
            card = card,
            remainingUses = uses,
            source = source
        });

        EventManager.Trigger("FreeCardChanged", this);
        Debug.Log($"获得免费技能【{card.cardName}】×{uses}" +
                  (string.IsNullOrEmpty(source) ? "" : $"（来自【{source}】）"));
    }

    /// <summary>
    /// 使用第 index 条免费技能（目标为单位）。
    ///
    /// 和 UseSlotCard 的区别：不用抽牌或灌注，不占槽位；用完额度后从 freeCards 移除。
    /// 其余（阶段校验、禁止行动、目标类型、射程、亲密、视线、结算）完全一致。
    /// </summary>
    /// <param name="index">免费技能在 FreeCards 里的序号（0 开始）</param>
    /// <param name="target">目标单位；传 null 时按 ResolveTarget 的规则自动选</param>
    public virtual bool UseFreeCard(int index, Unit target = null)
    {
        if (index < 0 || index >= freeCards.Count)
        {
            Debug.Log("免费技能序号无效");
            return false;
        }

        FreeCardCharge charge = freeCards[index];
        if (charge.IsEmpty)
        {
            Debug.Log("这条免费技能已用完");
            return false;
        }

        if (!CanActNow())
        {
            Debug.Log("不是玩家行动阶段，不能使用技能");
            return false;
        }

        // 被"禁止行动"（眩晕）时同样放不出来
        if (player.HasEffect(BuffEffectType.禁止行动))
        {
            Debug.Log("被禁止行动，无法出牌");
            return false;
        }

        CardData card = charge.card;
        if (!card.IsAttack)
        {
            IReadOnlyList<HexCoord> choice = target != null && target != player
                ? new[] { target.GetCoord() } : new HexCoord[0];
            return UseFreeDefenseCardOnCells(index, choice);
        }
        Unit finalTarget = ResolveTarget(card, target);

        if (!ValidateCardTarget(card, finalTarget)) return false;
        if (!ValidateCardTargeting(card, finalTarget)) return false;

        // 免费技能由药水或遗物授予，使用时不扣耐力。
        card.Resolve(player, finalTarget);
        EventManager.Trigger("CardUsed", card, player, finalTarget);
        Debug.Log($"免费使用【{card.cardName}】（来自{(string.IsNullOrEmpty(charge.source) ? "道具" : charge.source)}）");

        // 消耗一次免费额度，用完移除整条
        charge.remainingUses--;
        if (charge.IsEmpty)
            freeCards.RemoveAt(index);

        EventManager.Trigger("FreeCardChanged", this);
        return true;
    }

    public virtual bool UseFreeDefenseCardOnCells(int index, IReadOnlyList<HexCoord> selected)
    {
        if (index < 0 || index >= freeCards.Count || freeCards[index].IsEmpty ||
            !CanActNow() || player.HasEffect(BuffEffectType.禁止行动)) return false;
        FreeCardCharge charge = freeCards[index];
        if (charge.card == null || charge.card.IsAttack ||
            !DefenseSkillResolver.Resolve(charge.card, player, selected)) return false;
        Unit target = selected.Count > 0 && HexGrid.Instance != null
            ? HexGrid.Instance.GetCell(selected[0])?.occupyUnit : player;
        EventManager.Trigger("CardUsed", charge.card, player, target);
        charge.remainingUses--;
        if (charge.IsEmpty) freeCards.RemoveAt(index);
        EventManager.Trigger("FreeCardChanged", this);
        return true;
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
    /// 最大射程取"最终值"——位移类技能会被敏捷/残废修正（见 CardData.GetEffectiveMaxRange）。
    /// </summary>
    protected virtual bool IsInRange(CardData card, HexCoord from, HexCoord to)
    {
        int distance = from.Distance(to);
        int maxRange = card.GetEffectiveMaxRange(player);
        if (distance < card.minRange || distance > maxRange)
        {
            Debug.Log($"目标距离 {distance} 格，【{card.cardName}】射程为 {card.minRange}~{maxRange} 格");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 视线判定（对应文档"被阻挡"和烟雾地板规则）：
    /// 带"远程"标签的卡，如果施法者与目标之间有其他单位或障碍物，就打不到；
    /// 目标站在烟雾格子里时，也无法被远程技能选中。
    /// 近战/自身技能不受此限制。
    /// </summary>
    protected virtual bool IsTargetReachable(CardData card, HexCoord from, HexCoord to)
    {
        if (!card.tags.Contains(CardTag.远程) || card.ignoresLineBlock) return true;
        if (HexGrid.Instance == null) return true;

        if (HexGrid.Instance.IsLineBlocked(from, to))
        {
            Debug.Log($"【{card.cardName}】是远程技能，中间有其他单位/障碍物阻挡");
            return false;
        }

        if (HexGrid.Instance.IsCellSmoky(to))
        {
            Debug.Log($"【{card.cardName}】目标处于烟雾中，无法被远程技能选中");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 攻击范围预览高亮（文档："攻击范围内格子变成粉红色，有目标的格子变成深红色"）。
    /// UI 选中某张攻击卡时调用，center 传施法者坐标。
    /// </summary>
    public virtual void HighlightAttackRange(CardData card, HexCoord center)
    {
        if (HexGrid.Instance == null || card == null) return;
        HexGrid.Instance.ClearAllHighlights();

        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(center, card.maxRange))
        {
            if (cell == null) continue;
            if (center.Distance(cell.coord) < card.minRange) continue;   // 小于最小射程不显示

            // 有可攻击目标 → 深红；否则粉红
            bool hasTarget = cell.obstacle != null ||
                             (cell.occupyUnit != null && cell.occupyUnit != player);
            if (hasTarget)
                cell.SetTargetHighlight(true);
            else
                cell.SetAttackRangeHighlight(true);
        }
    }

    /// <summary>清除所有战斗高亮</summary>
    public virtual void ClearHighlights()
    {
        if (HexGrid.Instance != null)
            HexGrid.Instance.ClearAllHighlights();
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
            if (player.IsAttackTargetForbidden(enemy)) continue;  // 亲密：名单里的单位不能被自动索敌选中

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
    /// 过滤空引用、敌方特有和不属于本牌库的卡，但不排除槽位中已有的技能。
    /// </summary>
    protected virtual List<CardData> BuildOffer(List<CardData> pool, CardPool requestedPool)
    {
        // 先把牌库复制一份并洗牌，避免改动原始列表顺序
        List<CardData> bag = new List<CardData>();
        foreach (var card in pool)
            if (CardPoolRules.IsOfferEligible(card, requestedPool))
                bag.Add(card);
        if (bag.Count == 0)
            return new List<CardData>();
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

    [ContextMenu("测试：使用第 1 个免费技能")]
    protected void DebugUseFreeCard0() => UseFreeCard(0);
}
