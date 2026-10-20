using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ============================================================
//  战斗 HUD —— 按飞书《游戏系统》例图搭建的运行时 UI
//
//  布局（例图要点）：
//  · 左上角：红色圆 + 血量数字（下方一行耐力/思绪小字）
//  · 左下角：推 / 跳 两个基础技能按钮，右侧三张技能插槽，右下角插卡按钮
//  · 右下角：下一回合按钮（进入选择状态时变成"取消"），其正下方是插卡按钮
//  · 插卡按钮：点击先选攻击/防御牌堆 → 3 张待选卡 → 点击灌注进技能插槽
//  · 技能插槽 / 卡面：点击进入该技能的瞄准模式（按 attackTargetRule 高亮合法格）
//  · 推：基础攻击技能（CardData "推"）：红圈标出射程内的敌人，点击敌人 → 伤害 + 击退 1 格
//  · 跳：基础位移技能（CardData "跳"）：绿圈标出相邻可落点，点击格子 → 传送过去
//  · 选择技能 / 卡牌期间：所有小怪图像半透明化（例图要求），"下一回合"变"取消"
//  · 鼠标悬停敌人立绘：脚下红色血条 + 头顶显示它本回合准备释放的技能（其他地方不显示）
//
//  全部 UI 运行时生成（Canvas / EventSystem 自举），不依赖预制体和场景改动。
//  挂载位置：Battle.unity 的 Managers 物体上。
// ============================================================

public class BattleHUD : MonoBehaviour
{
    public static BattleHUD Instance { get; private set; }

    // -------------------- 瞄准模式 --------------------

    private enum SelectMode
    {
        无,      // 普通状态
        推,      // 基础攻击技能"推"：红圈标出射程内敌人，点击敌人
        跳,      // 基础位移技能"跳"：绿圈标出可落点，点击落点
        技能     // 槽位技能瞄准中：按 attackTargetRule 高亮合法格
    }

    private SelectMode mode = SelectMode.无;
    private int aimSlot = -1;                              // 技能模式下正在瞄准的槽位序号
    private readonly List<HexCoord> pendingAimCells = new List<HexCoord>();   // 多目标技能已选格子

    // -------------------- UI 引用 --------------------

    private Canvas canvas;
    private Text hpText;             // 血量槽圆心的 "当前/上限" 数字
    private Text staminaText;        // 耐力槽上的数字
    private Text thoughtText;        // 思绪槽上的数字
    private Text endTurnLabel;       // 下一回合 / 取消
    private Button endTurnButton;
    private Button drawButton;       // 思绪爆发按钮：下一回合正下方的抽牌按键
    private float drawButtonCenterX; // 该按钮水平中线（相对屏幕中心，画布参考系 1920 宽）
    private float drawButtonTopY;    // 该按钮上边沿的 y（相对屏幕中心，画布参考系 1080 高）
                                     // 上面两个值在 BuildUI 里算好，供悬停浮层定位到按钮正上方
    private GameObject deckPanel;    // 牌堆选择层（攻击/防御两个大按钮）
    private GameObject cardPanel;    // 3 张待选卡弹窗
    private Text deckTitleText;      // "选择要抽取的技能牌堆（消耗 N 点思绪）"
    private readonly List<Text> deckCostTexts = new List<Text>();   // 两个大按钮下方的费用小字
    private GameObject skillGroup;   // 左下角按钮 + 插槽 + 插卡按钮的父物体（选技能时整组隐藏）
    private Button[] slotButtons = new Button[3];   // 三张技能插槽（底部横条）
    private Text[] slotTexts = new Text[3];         // 插槽横条上的文字（名称已移到卡面，这里留空）
    private Button[] slotFaceButtons = new Button[3]; // 卡面本体：整张卡可点击 → 释放该槽技能
    private Text[] slotNameTexts = new Text[3];      // 卡面空白区的技能名（黑色）
    private Text[] slotUseTexts = new Text[3];       // 卡面下方的剩余次数小字

    // 左侧敌方技能栏：三张空技能卡面竖排，显示"上回合怪物准备释放的技能"
    private GameObject enemySkillGroup;
    private Button[] enemySkillFaces = new Button[3];
    private Text[] enemySkillNameTexts = new Text[3];
    private Text[] enemySkillCostTexts = new Text[3];
    private Button enemySkillUpArrow;
    private Button enemySkillDownArrow;
    private int enemySkillScroll;   // 滚动窗口起点（每次滚 1 张，怪物 > 3 个时翻找）

    /// <summary>左侧卡面上的一条技能记录：来源怪物 + 技能</summary>
    private struct EnemySkillEntry
    {
        public EnemyUnit Owner;
        public CardData Card;
    }

    private readonly List<EnemySkillEntry> enemySkillEntries = new List<EnemySkillEntry>();

    // -------------------- 敌人头顶：本回合预告技能小标签 --------------------

    /// <summary>一条悬停信息：跟随某只敌人，显示它本回合准备释放的技能名</summary>
    private class PlannedSkillLabel
    {
        public EnemyUnit Enemy;
        public RectTransform Root;
        public Text Content;
        public Bounds SpriteBounds;   // 立绘世界包围盒（每 20 帧重测；悬停时每帧重测）
        public string ShownText;      // 缓存文本，变化时才重算宽度
    }

    private GameObject plannedLabelLayer;
    private readonly List<PlannedSkillLabel> plannedLabels = new List<PlannedSkillLabel>();
    private RectTransform enemyHpBar;     // 敌人脚下血条（底槽）
    private Image enemyHpBarFill;         // 红色血量填充
    private Text enemyHpBarText;          // 血条上的数字（当前/上限）
    // 顶部饮料槽位（默认 3 个）。「药水腰带」会把 maxPotions 提到 5，
    // 所以这里改成可增长的 List，RefreshPotionSlots 时按上限补齐槽位。
    private readonly List<Image> potionSlotImages = new List<Image>();
    private readonly List<Button> potionSlotButtons = new List<Button>();
    private const int basePotionSlots = 3;        // 没吃遗物时的槽位数（= 玩家默认药水上限）
    private float potionSlotW = 67f;              // 饮料槽显示尺寸（BuildUI 里按素材比例算）
    private float potionSlotH = 77f;
    private GameObject potionTooltip;             // 鼠标停在饮料槽上：药水名 + 效果说明
    private RectTransform potionTooltipRt;
    private Text potionTooltipText;

    // 顶部：血量槽右侧的遗物图标（每件遗物一个，从左往右排）
    private GameObject relicRow;
    private readonly List<Image> relicIcons = new List<Image>();
    private const float relicIconSize = 64f;
    private const float relicIconGap = 5f;
    // 遗物图标的水平中心 y（= 饮料槽中心，长条 105 高的一半），悬停浮层也按它定位
    private const float relicRowY = -52.5f;
    private GameObject relicTooltip;             // 鼠标停在遗物图标上：遗物名 + 效果说明
    private RectTransform relicTooltipRt;
    private Text relicTooltipText;

    // 技能介绍浮层：底部三个技能槽 / 左侧怪物技能卡 悬停时显示（内容取 CardData.description）
    private GameObject skillTooltip;
    private RectTransform skillTooltipRt;
    private Text skillTooltipTitle;
    private Text skillTooltipBody;

    // buff 说明浮层：悬停的卡牌带 buff 时，出现在技能介绍浮层旁边的第二个弹窗
    private GameObject buffTooltip;
    private RectTransform buffTooltipRt;
    private Text buffTooltipText;

    // 玩家 buff 栏：左侧敌人技能栏与耐力槽之间的空白区域，常驻显示主角身上所有 buff。
    // 无图标素材 → 按极性染色的色块 + buff 名；悬停弹出 buff 说明（复用 buffTooltip）。
    private GameObject buffBar;
    private readonly List<RectTransform> buffIcons = new List<RectTransform>();
    private string buffBarSignature = "";        // 上次刷新的 buff 摘要，变了才重建
    private const float buffIconSize = 44f;
    private const float buffIconGap = 4f;
    private const float buffBarX = 200f;         // 左侧敌人技能栏右缘(约182)与耐力槽左缘(约351)之间
    private const float buffBarY = -110f;        // 深蓝长条下沿
    private const float buffBarW = 150f;         // 一行最多放 3 个图标，超出换行

    // 当前正在被悬停的来源按钮（技能卡 / 饮料槽 / 待选卡）。
    // UGUI 只在指针真正"移出"时才发 PointerExit，来源物体被隐藏或销毁时不会补发，
    // 所以记下来源，由每帧的 GuardTooltips() 兜底判断浮层该不该关。
    private GameObject tooltipHoverOwner;

    // 底部插槽 / 左侧卡面的布局参数（建 UI 时记下，悬停浮层要按它们定位）
    private float slotCardW = 203f;
    private float slotCardFaceH = 265f;
    private float slotRightGapX = 281f;
    private const float enemyCardW = 150f;
    private float enemyCardH = 196f;
    private float enemyCardFirstTopY = 230f;
    private const float enemyCardGap = 8f;

    // 左下角 推 / 跳 基础技能卡面（与左侧怪物技能卡同宽，纵向立起）
    // x 起点在左侧怪物技能栏（卡面右缘 174 + 底部翻页箭头）之右，避免与其重叠
    private const float baseCardX = 190f;
    private const float baseCardW = 150f;
    private float baseCardH = 150f * 495f / 379f;   // ≈196：与空技能卡面同比例
    private int plannedLabelSyncFrame = -999;   // 上次同步敌人列表的帧号

    private const float hpBarWidth = 96f;        // 血条外框宽
    private const float hpBarInnerWidth = 92f;   // 填充最大宽度（左右各留 2px 边）
    // 悬停敌人信息（脚下血条 + 头顶技能标签）整体放大倍数：只在鼠标悬停时出现的浮层，比 HUD 大一号更好读
    private const float hoverInfoScale = 1.2f;

    // 灌注确认弹窗 / 结果提示弹窗
    private GameObject confirmPanel;
    private Text confirmText;
    private CardData pendingInfuseCard;   // 确认弹窗当前对应的技能
    private GameObject messagePanel;
    private Text messageText;

    [Header("状态栏素材（引用已在 Battle.unity 里配好）")]
    public Sprite statusBarSprite;    // 深蓝色长条_2：顶部状态栏底板
    public Sprite hpSlotSprite;       // 血量槽_1：左上角红色圆
    public Sprite staminaSlotSprite;  // 耐力槽_2：长条下方左侧浅灰条
    public Sprite thoughtSlotSprite;  // 思绪槽_1：长条下方右侧浅蓝条

    [Header("插槽素材（引用已在 Battle.unity 里配好）")]
    public Sprite skillSlotSprite;    // 技能插槽：左下角三连槽
    public Sprite drawButtonSprite;   // 抽牌按键：下一回合下方的插卡按钮
    public Sprite emptyCardFaceSprite; // 空技能卡面：空槽位的卡面底图（显示"空"）
    public Sprite attackCardFaceSprite;  // 攻击牌：攻击类技能的卡面
    public Sprite defenseCardFaceSprite; // 防御牌：防御类技能的卡面
    public Sprite deckAttackIconSprite;  // 攻击.png：牌堆选择层左侧按钮顶部图标
    public Sprite deckDefenseIconSprite; // 技能.png：牌堆选择层右侧按钮顶部图标

    [Header("左侧敌方技能栏素材（引用已在 Battle.unity 里配好）")]
    public Sprite enemySkillArrowSprite;      // 淡蓝色箭头_1：翻页箭头常态底图（下箭头旋转 180°）
    public Sprite enemySkillArrowHoverSprite; // 淡蓝色箭头_2：鼠标悬停/按下时底图

    [Header("下一回合按钮素材（引用已在 Battle.unity 里配好）")]
    public Sprite endTurnNormalSprite;   // 淡蓝色按键_1：常态底图
    public Sprite endTurnHoverSprite;    // 淡蓝色按键_2：鼠标悬停/按下时底图

    [Header("药水栏素材（引用已在 Battle.unity 里配好）")]
    public Sprite potionSlotSprite;      // 空饮料槽位：顶部深蓝长条上的三只空瓶占位

    [Header("敌人悬停信息（可关）")]
    [Tooltip("勾选后鼠标悬停在敌人立绘上时：脚下显示红色血条、头顶显示本回合准备释放的技能；" +
             "瞄准 / 抽卡 / 敌方行动 / 弹窗期间自动隐藏，不影响格子点击")]
    public bool showEnemyPlannedSkills = true;

    [Header("基础技能卡（引用已在 Battle.unity 里配好）")]
    public CardData pushCard;   // 推：攻击技能，伤害 1 + 击退 1 格
    public CardData jumpCard;   // 跳：防御/位移技能，传送到相邻格

    private Font uiFont;

    // -------------------- 其他运行时状态 --------------------

    private PlayerCardSystem cardSystem;   // LevelManager 运行时才挂，懒查找
    private readonly Dictionary<SpriteRenderer, Color> dimmedSprites = new Dictionary<SpriteRenderer, Color>();

    // 双输入通道去重（同一帧两条通道只处理一次）
    private int lastClickFrame = -1;

    // ============================================================
    //                       初始化
    // ============================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 选牌流程收尾（灌注成功或放弃都会触发）：关弹窗、清卡牌按钮
        EventManager.AddListener("CardOfferClosed", OnCardOfferClosed);
        // 槽位变化（灌注/释放/次数耗尽）→ 刷新插槽显示
        EventManager.AddListener<PlayerCardSystem>("SlotChanged", OnSlotChanged);
        // 碰撞体点击通道（HexCell.OnMouseDown 广播），与坐标拾取互为兜底
        EventManager.AddListener<HexCell>("CellClicked", OnCellClickedEvent);
        // 玩家回合开始：重新收集"上回合怪物准备释放的技能"填左侧卡面
        EventManager.AddListener<int>("PlayerTurnStarted", OnPlayerTurnStarted);
        // 药水 / 遗物变化：刷新顶部饮料槽与血量槽右侧的遗物图标
        EventManager.AddListener<PlayerUnit>("PotionChanged", OnPotionChanged);
        EventManager.AddListener<PlayerUnit>("RelicChanged", OnRelicChanged);
        // 行动次数用尽（「黑鹅项圈」）：弹窗提示，别让玩家以为是卡了
        EventManager.AddListener<PlayerUnit>("PlayerActionLimitReached", OnActionLimitReached);

        uiFont = LoadBuiltinFont();
        BuildUI();

        // 战斗结束奖励界面（独立 Canvas 盖在 HUD 之上；胜利时自动弹出）
        if (FindObjectOfType<BattleEndPanel>() == null)
            new GameObject("BattleEndPanel").AddComponent<BattleEndPanel>();
    }

    private void OnDestroy()
    {
        EventManager.RemoveListener("CardOfferClosed", OnCardOfferClosed);
        EventManager.RemoveListener<PlayerCardSystem>("SlotChanged", OnSlotChanged);
        EventManager.RemoveListener<HexCell>("CellClicked", OnCellClickedEvent);
        EventManager.RemoveListener<int>("PlayerTurnStarted", OnPlayerTurnStarted);
        EventManager.RemoveListener<PlayerUnit>("PotionChanged", OnPotionChanged);
        EventManager.RemoveListener<PlayerUnit>("RelicChanged", OnRelicChanged);
        EventManager.RemoveListener<PlayerUnit>("PlayerActionLimitReached", OnActionLimitReached);
    }

    private void OnPotionChanged(PlayerUnit player) => RefreshPotionSlots();
    private void OnRelicChanged(PlayerUnit player) => RefreshRelicIcons();

    /// <summary>每回合行动次数用尽（「黑鹅项圈」=5 次）：弹窗说明，避免玩家反复点却没反应</summary>
    private void OnActionLimitReached(PlayerUnit player)
    {
        int max = player != null ? player.MaxActionsPerTurn : 0;
        ShowMessage(max > 0
            ? $"本回合行动次数已用完（上限 {max} 次）"
            : "本回合行动次数已用完");
    }

    /// <summary>选牌结束（成功/放弃）：关掉牌堆选择与待选卡弹窗，恢复小怪透明度</summary>
    private void OnCardOfferClosed()
    {
        if (cardPanel != null) cardPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);
        if (mode == SelectMode.无)
            DimEnemies(false);
    }

    /// <summary>槽位内容变化：刷新插槽文字（下一次 RefreshHud 也会刷，这里立即响应）</summary>
    private void OnSlotChanged(PlayerCardSystem system) => RefreshSlotTexts();

    /// <summary>玩家回合开始：重收左侧敌方技能卡面（此时 lastTurnUsedSkills 刚结算完）</summary>
    private void OnPlayerTurnStarted(int turnCount) => RebuildEnemySkillEntries();

    /// <summary>Unity 内置字体（2022 是 LegacyRuntime，老版本叫 Arial，逐个试）</summary>
    private static Font LoadBuiltinFont()
    {
        foreach (string name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
        {
            Font f = Resources.GetBuiltinResource<Font>(name);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>确保有 EventSystem（UGUI 按钮点击依赖它，场景里还没有）</summary>
    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // ============================================================
    //                       UI 构建
    // ============================================================

    private void BuildUI()
    {
        EnsureEventSystem();

        // 双保险：场景引用优先；编辑器下引用丢失时按 Art/UI 规范路径直接加载
        // （AssetDatabase 不依赖场景重载，也不会因目录结构调整失效）
#if UNITY_EDITOR
        const string uiDir = "Assets/Game/Art/UI/";
        if (statusBarSprite == null)
            statusBarSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "深蓝色长条_2.png");
        if (hpSlotSprite == null)
            hpSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "血量槽_1.png");
        if (staminaSlotSprite == null)
            staminaSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "耐力槽_2.png");
        if (thoughtSlotSprite == null)
            thoughtSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "思绪槽_1.png");
        if (skillSlotSprite == null)
            skillSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "技能插槽.png");
        if (drawButtonSprite == null)
            drawButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "抽牌按键.png");
        if (emptyCardFaceSprite == null)
            emptyCardFaceSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "空技能卡面.png");
        if (attackCardFaceSprite == null)
            attackCardFaceSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "攻击牌.png");
        if (defenseCardFaceSprite == null)
            defenseCardFaceSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "防御牌.png");
        if (deckAttackIconSprite == null)
            deckAttackIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "攻击.png");
        if (deckDefenseIconSprite == null)
            deckDefenseIconSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "技能.png");
        if (enemySkillArrowSprite == null)
            enemySkillArrowSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色箭头_1.png");
        if (enemySkillArrowHoverSprite == null)
            enemySkillArrowHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色箭头_2.png");
        if (endTurnNormalSprite == null)
            endTurnNormalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_1.png");
        if (endTurnHoverSprite == null)
            endTurnHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_2.png");
        if (potionSlotSprite == null)
            potionSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "空饮料槽位.png");
        // 基础技能卡：场景引用丢失时按资产路径兜底（推 / 跳 各只有一张）
        if (pushCard == null)
            pushCard = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>(
                "Assets/Game/ScriptableObjects/技能/攻击技能/推.asset");
        if (jumpCard == null)
            jumpCard = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>(
                "Assets/Game/ScriptableObjects/技能/防御技能/跳.asset");
#endif
        if (statusBarSprite == null || hpSlotSprite == null ||
            staminaSlotSprite == null || thoughtSlotSprite == null)
            Debug.LogWarning("[BattleHUD] 状态栏有素材未加载（打包版需保证场景引用有效），缺失项将以纯色块兜底显示");

        canvas = new GameObject("BattleHUDCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // 盖在所有世界物体上
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // HUD 保持 1920×1080 基准尺寸不放大（地图放大只走相机 startZoom）
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        // 敌人头顶标签层：最早创建 = UI 层级最低，被所有面板盖住，但仍然浮在战场之上
        plannedLabelLayer = new GameObject("EnemyPlannedLabels", typeof(RectTransform));
        plannedLabelLayer.transform.SetParent(canvas.transform, false);
        Stretch(plannedLabelLayer.GetComponent<RectTransform>());

        // ============================================================
        // 顶部状态栏（自底向上：深蓝长条 → 耐力/思绪槽 → 血量槽）
        // 素材若丢失引用则退化为半透明纯色块，布局不受影响
        // ============================================================

        // ---- 最底层：深蓝色长条_2 铺满屏幕顶部（Canvas 在两张战斗背景之上） ----
        MakeImage(canvas.transform, "StatusBar_Bar", statusBarSprite,
            statusBarSprite == null ? new Color(0.16f, 0.27f, 0.44f, 0.9f) : Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1920f, 105f));

        // ---- 长条偏右：饮料槽位，起点在中轴右侧 420px，间隔 5px ----
        // 空槽显示空饮料槽位素材；拿到药水后换成药水立绘。
        // 槽位数默认 3 个，吃完「药水腰带」（上限 +2）后由 RefreshPotionSlots 补到 5 个。
        // 鼠标停上去看效果说明，点一下直接喝掉，喝完 / 空槽自动换回空瓶素材。
        if (potionSlotSprite != null)
        {
            potionSlotH = 77f;
            potionSlotW = potionSlotH * 815f / 931f;
            EnsurePotionSlotCount(basePotionSlots, canvas.transform);
        }

        // ---- 长条下方：耐力槽（左） / 思绪槽（右），关于画面中轴 x=960 对称 ----
        // pivot=(0.5,1)（顶边锚点）；y=-110 = 长条底边(-105) 再下移 5px
        // 尺寸：横向长度 448×1.2≈538，竖向宽度 110×0.7=77（正好贴合素材 400×69 原始比例）
        Image staminaSlot = MakeImage(canvas.transform, "StatusBar_Stamina", staminaSlotSprite,
            staminaSlotSprite == null ? new Color(0.85f, 0.85f, 0.87f, 0.9f) : Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-340f, -110f), new Vector2(538f, 77f));
        staminaText = MakeText(staminaSlot.transform, "20/20", 30, TextAnchor.MiddleCenter,
            new Color(0.15f, 0.15f, 0.18f));
        Stretch(staminaText.rectTransform);
        // 条里只有数字认不出是什么资源，左侧补一个"耐力"小字（数字仍居中，互不叠）
        MakeText(staminaSlot.transform, "耐力", 28, TextAnchor.MiddleLeft,
            new Color(0.15f, 0.15f, 0.18f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f),
            new Vector2(0f, 0.5f), new Vector2(160f, 40f));

        Image thoughtSlot = MakeImage(canvas.transform, "StatusBar_Thought", thoughtSlotSprite,
            thoughtSlotSprite == null ? new Color(0.65f, 0.8f, 0.97f, 0.9f) : Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(340f, -110f), new Vector2(538f, 77f));
        thoughtText = MakeText(thoughtSlot.transform, "20/20", 30, TextAnchor.MiddleCenter,
            new Color(0.15f, 0.15f, 0.18f));
        Stretch(thoughtText.rectTransform);
        // 同上：蓝条左侧补"思绪"
        MakeText(thoughtSlot.transform, "思绪", 28, TextAnchor.MiddleLeft,
            new Color(0.15f, 0.15f, 0.18f),
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f),
            new Vector2(0f, 0.5f), new Vector2(160f, 40f));

        // ---- 左上角：血量槽红圆，压在深蓝长条之上，数字写圆心 ----
        Image hpSlot = MakeImage(canvas.transform, "StatusBar_HP", hpSlotSprite,
            hpSlotSprite == null ? new Color(0.89f, 0.23f, 0.23f, 0.95f) : Color.white,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(132f, 132f));
        hpText = MakeText(hpSlot.transform, "12/12", 30, TextAnchor.MiddleCenter, Color.white);
        Stretch(hpText.rectTransform);
        // 红圆里再补一个"血量"小字，压在数字上方：圆 + 数字不写清楚认不出是血量
        MakeText(hpSlot.transform, "血量", 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f),
            new Vector2(0.5f, 1f), new Vector2(132f, 26f));

        // ---- 血量槽右侧：遗物图标一列排开（距红圆右缘 10px，每个间隔 5px） ----
        BuildRelicRow();

        // ---- 左侧空白区：主角身上的 buff 常驻显示栏 ----
        BuildBuffBar();

        // ---- 左下角：攻击 / 位移 按钮 + 三张技能插槽 + 右下角插卡按钮 ----
        // （选技能时整组隐藏，只留状态条和"取消"）
        // 注意：必须显式带 RectTransform——纯 Transform 的物体 GetComponent<RectTransform>() 为 null
        skillGroup = new GameObject("SkillButtons", typeof(RectTransform));
        skillGroup.transform.SetParent(canvas.transform, false);
        Stretch(skillGroup.GetComponent<RectTransform>());   // 铺满画布，子按钮的左下角锚点才正确

        string[] labels = { "推", "跳" };
        // 推 = 攻击技能 → 攻击牌卡面；跳 = 防御/位移技能 → 防御牌卡面。
        // 卡面立起放在左下角（与左侧怪物技能卡同宽），黑字写卡名，悬停出介绍浮层。
        CardData[] baseCards = { pushCard, jumpCard };
        for (int i = 0; i < labels.Length; i++)
        {
            SelectMode targetMode = i == 0 ? SelectMode.推 : SelectMode.跳;
            Button faceBtn = MakeImageButton(skillGroup.transform, "BaseSkill_" + labels[i],
                FaceForCard(baseCards[i]), new Vector2(0f, 0f),
                new Vector2(baseCardX + i * (baseCardW + 10f), 28f), new Vector2(baseCardW, baseCardH),
                _ => EnterMode(targetMode), 0);

            // 卡名黑字：与底部插槽卡面同位（卡面浅灰文字区）
            Text nameT = MakeText(faceBtn.transform, labels[i], 22, TextAnchor.MiddleCenter,
                Color.black,
                new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.34f), Vector2.zero,
                new Vector2(0.5f, 0.34f), new Vector2(baseCardW * 0.84f, baseCardH * 0.2f));
            nameT.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 鼠标停在卡面上：上方浮层显示技能介绍（带 buff 时旁边加 buff 浮层）
            int capturedBase = i;
            AddHoverTrigger(faceBtn.gameObject,
                () => ShowBaseSkillTooltip(capturedBase), HideSkillTooltip);
        }

        // ---- 右下按钮组定位参数（插槽与按钮共用，都锚定右下角） ----
        // 下一回合按钮右缘距屏幕右边界 10px；插槽组右缘在其左侧再留 20px
        const float btnRightGap = 10f;
        // 淡蓝色按键裁剪后 1247×308，显示高 62 → 宽 ≈251
        float endTurnW = 62f * 1247f / 308f;
        float slotRightX = btnRightGap + endTurnW + 20f;   // 最右插槽右缘到屏幕右边的距离

        // ---- 三张技能插槽：右缘距右下按钮 20px，上下位置不变 ----
        // 素材裁剪后 383×117，显示高 62 → 宽 ≈203
        float slotW = 62f * 383f / 117f;
        // ---- 插槽低一图层：每槽一张空技能卡面 ----
        // 素材裁剪后 379×495：中上部是透明窗口（预留技能图标），下方浅灰区是文字区。
        // 卡面本体就是按钮（整张卡可点 → 释放该槽技能），插槽横条叠在上面只作边框。
        float faceH = slotW * 495f / 379f;

        // 记下布局参数：悬停技能卡时浮层要按这些数值定位
        slotCardW = slotW;
        slotCardFaceH = faceH;
        slotRightGapX = slotRightX;

        for (int i = 0; i < 3; i++)
        {
            // anchor/pivot=(1,0)，pos.x = 该槽右缘到屏幕右边的距离；i=2 最靠右
            Vector2 pos = new Vector2(-(slotRightX + (2 - i) * (slotW + 10f)), 25f);
            Button faceBtn = MakeImageButton(skillGroup.transform, "SlotCardFace_" + i,
                emptyCardFaceSprite, new Vector2(1f, 0f), pos, new Vector2(slotW, faceH),
                EnterSlotMode, i);
            faceBtn.transform.SetAsFirstSibling();   // 压到最底层：所有插槽元素都盖在卡面上
            slotFaceButtons[i] = faceBtn;

            // 鼠标停在卡面上：显示该技能的介绍（内容取 CardData.description）
            int capturedSlot = i;
            AddHoverTrigger(faceBtn.gameObject,
                () => ShowSlotSkillTooltip(capturedSlot), HideSkillTooltip);

            // 卡名：写进素材浅灰空白区（图像坐标 y251~470 → 归一化 0.25~0.04，取中部）
            // anchorY 0.34 ≈ 卡面高度 34% 处，正好落在插槽横条上沿之上，不会被框线压住
            Text nameT = MakeText(faceBtn.transform, "", 26, TextAnchor.MiddleCenter, Color.black,
                new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.34f), Vector2.zero,
                new Vector2(0.5f, 0.34f), new Vector2(slotW * 0.88f, faceH * 0.24f));
            nameT.horizontalOverflow = HorizontalWrapMode.Wrap;   // 长名字自动换行，不溢出卡面
            slotNameTexts[i] = nameT;

            // 剩余次数：卡面下方小字（插槽框中间是透明的，能看到）。卡面文字统一黑字
            Text useT = MakeText(faceBtn.transform, "", 17, TextAnchor.MiddleCenter,
                Color.black,
                new Vector2(0.5f, 0.19f), new Vector2(0.5f, 0.19f), Vector2.zero,
                new Vector2(0.5f, 0.19f), new Vector2(slotW * 0.8f, faceH * 0.12f));
            slotUseTexts[i] = useT;
        }
        for (int i = 0; i < 3; i++)
        {
            // 与卡面同位：-281 / -494 / -707（右缘距屏幕右边界），间隔 10px
            Vector2 pos = new Vector2(-(slotRightX + (2 - i) * (slotW + 10f)), 25f);
            Button slotBtn = MakeImageButton(skillGroup.transform, "SkillSlot_" + i,
                skillSlotSprite, new Vector2(1f, 0f), pos, new Vector2(slotW, 62f), EnterSlotMode, i);
            slotButtons[i] = slotBtn;
            slotTexts[i] = slotBtn.GetComponentInChildren<Text>();
        }

        // ---- 右下角：下一回合（淡蓝色按键素材，悬停/按下换成第二张），右缘距屏幕右边界 10px ----
        endTurnButton = MakeImageButton(canvas.transform, "下一回合", endTurnNormalSprite,
            new Vector2(1f, 0f), new Vector2(-btnRightGap, 86f), new Vector2(endTurnW, 62f),
            _ => OnEndTurnClicked(), 0);
        endTurnLabel = endTurnButton.GetComponentInChildren<Text>();
        if (endTurnLabel != null)
            endTurnLabel.color = new Color(0.14f, 0.22f, 0.36f);   // 浅蓝底上用深色字
        // 高亮（鼠标悬停）与按下都换成淡蓝色按键_2
        ApplyHoverSpriteSwap(endTurnButton, endTurnHoverSprite);

        // ---- 思绪爆发按钮：下一回合正下方（水平对齐其中线），点击后先选牌堆 ----
        // 素材裁剪后 862×705，显示高 62 → 宽 ≈76
        float drawW = 62f * 862f / 705f;
        // 下一回合中线 = -(btnRightGap + endTurnW/2)；本按钮右缘 = 中线 + drawW/2
        float drawRightGap = btnRightGap + endTurnW / 2f - drawW / 2f;
        const float drawY = 12f;    // 按钮下边沿到屏幕底边的距离
        drawButton = MakeImageButton(canvas.transform, "DrawButton", drawButtonSprite,
            new Vector2(1f, 0f),
            new Vector2(-drawRightGap, drawY), new Vector2(drawW, 62f),
            OnDrawButtonClicked, 0);

        // 记下按钮位置：锚点/pivot 都是 (1,0)，所以
        //   中线 x = 960 - 按钮右缘到屏幕右边的距离 - 半个按钮宽
        //   上边沿 y = -540(屏幕底) + 按钮下边沿间距 + 按钮高
        // 悬停浮层按这两个值摆到按钮正上方（参考分辨率 1920×1080）
        drawButtonCenterX = 960f - drawRightGap - drawW / 2f;
        drawButtonTopY = -540f + drawY + 62f;

        // 鼠标停在思绪爆发按钮上：显示它的名称和功能说明
        AddHoverTrigger(drawButton.gameObject, ShowThoughtBurstTooltip, HideSkillTooltip);

        // ---- 弹窗（先建好，默认隐藏） ----
        BuildDeckPanel();
        BuildCardPanel();
        BuildEnemySkillPanel();
        BuildInfuseConfirmPanel();
        BuildMessagePanel();

        // ---- 悬停浮层（药水说明 / 技能介绍）：最后建，保证压在所有元素之上 ----
        BuildTooltips();

        // 顶部饮料槽 / 遗物栏先按当前存档刷一次
        RefreshPotionSlots();
        RefreshRelicIcons();
    }

    // ============================================================
    //   顶部药水栏 / 遗物栏 + 悬停说明浮层
    // ============================================================

    /// <summary>血量槽右侧的遗物图标列：红圆右缘 +10px 起，每个间隔 5px，垂直居中于红圆</summary>
    private void BuildRelicRow()
    {
        relicRow = new GameObject("RelicRow", typeof(RectTransform));
        relicRow.transform.SetParent(canvas.transform, false);
        RectTransform rt = relicRow.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 0.5f);
        // 血量槽：左上角 (24,24)、132×132 → 右缘 156，再右移 10px；
        // y 取"饮料槽中心" -52.5（不是红圆圆心 -90）：遗物图标的水平中心要和空饮料槽对齐
        rt.anchoredPosition = new Vector2(24f + 132f + 10f, relicRowY);
        rt.sizeDelta = new Vector2(620f, relicIconSize);
    }

    /// <summary>按玩家当前持有的遗物刷新图标（拿到遗物 → 追加一个；换关重来 → 清空）</summary>
    private void RefreshRelicIcons()
    {
        if (relicRow == null) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        IReadOnlyList<RelicData> relics = player != null ? player.Relics : null;
        int count = relics != null ? relics.Count : 0;

        // 不够就补图标节点（够用时只重新排位置）
        while (relicIcons.Count < count)
        {
            int captured = relicIcons.Count;
            // anchor/pivot 都取 (0,0.5)：anchoredPosition.x 就是"图标左缘距遗物栏左边"的距离，
            // y=0 让图标垂直居中于遗物栏；栏本身的 y 已经是饮料槽中心，所以两者水平中心对齐。
            Image icon = MakeImage(relicRow.transform, "RelicIcon_" + captured, null, Color.white,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(relicIconSize, relicIconSize));
            icon.preserveAspect = true;   // 遗物立绘保持原始比例
            // MakeImage 默认把装饰图的 raycastTarget 关掉（防挡格子点击），
            // 但遗物图标要收 PointerEnter/Exit 才能弹说明，这里单独打开。
            icon.raycastTarget = true;
            relicIcons.Add(icon);

            // 悬停回调只在建图标时挂一次（刷新只改图和位置）：
            // 每次都挂会往同一个 EventTrigger 里堆重复 entry，刷新几次就弹几个浮层。
            AddHoverTrigger(icon.gameObject,
                () => ShowRelicTooltip(captured), HideRelicTooltip);
        }

        for (int i = 0; i < relicIcons.Count; i++)
        {
            bool used = i < count;
            if (relicIcons[i].gameObject.activeSelf != used)
                relicIcons[i].gameObject.SetActive(used);
            if (!used) continue;

            RelicData relic = relics[i];
            if (relic.icon == null)
                Debug.LogWarning($"[BattleHUD] 遗物【{relic.relicName}】的 icon 没有配置（或引用丢失），长条上只能用色块占位");
            relicIcons[i].sprite = relic.icon;
            // 没有配图标的遗物先用浅色方块占位，至少能看出"有这一件"
            relicIcons[i].color = relic.icon != null ? Color.white : new Color(0.78f, 0.84f, 0.95f, 0.9f);
            relicIcons[i].rectTransform.anchoredPosition =
                new Vector2(i * (relicIconSize + relicIconGap), 0f);
        }
    }

    // 遗物栏的每帧刷新签名：实例 ID + 图标 ID 变了才重建，
    // 兜底一切"漏掉 RelicChanged 事件"的时序（结算面板发遗物 / 跨场景快照恢复等）。
    private string relicIconSignature = "";

    private void RefreshRelicIconsIfChanged()
    {
        if (relicRow == null) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        IReadOnlyList<RelicData> relics = player != null ? player.Relics : null;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (relics != null)
            foreach (RelicData r in relics)
            {
                if (r == null) { sb.Append("null;"); continue; }
                sb.Append(r.GetInstanceID()).Append('|')
                  .Append(r.icon != null ? r.icon.GetInstanceID() : 0).Append(';');
            }
        string sig = sb.ToString();
        if (sig == relicIconSignature) return;
        relicIconSignature = sig;
        RefreshRelicIcons();
    }

    /// <summary>药水槽：有药水就换成立绘，没药水（或刚喝完）换回空瓶素材</summary>
    private void RefreshPotionSlots()
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        int count = player != null ? player.Potions.Count : 0;

        // 「药水腰带」把药水携带上限 +2 → 槽位跟着补出来（3 → 5）
        if (player != null)
            EnsurePotionSlotCount(Mathf.Max(basePotionSlots, player.maxPotions));

        for (int i = 0; i < potionSlotImages.Count; i++)
        {
            Image img = potionSlotImages[i];
            if (img == null) continue;

            PotionData potion = i < count ? player.Potions[i] : null;
            Sprite want = potion != null && potion.icon != null ? potion.icon : potionSlotSprite;
            if (img.sprite != want) img.sprite = want;
            img.color = Color.white;
        }
    }

    /// <summary>
    /// 把饮料槽补齐到 want 个（只增不减）。
    /// parent 传空时沿用已有槽位的父节点；「药水腰带」把上限提到 5 时靠它多建两个槽。
    /// </summary>
    private void EnsurePotionSlotCount(int want, Transform parent = null)
    {
        if (potionSlotSprite == null) return;
        if (want <= potionSlotImages.Count) return;

        if (parent == null && potionSlotImages.Count > 0)
            parent = potionSlotImages[0].transform.parent;
        if (parent == null) return;

        for (int i = potionSlotImages.Count; i < want; i++)
        {
            int captured = i;
            float x = 420f + potionSlotW / 2f + i * (potionSlotW + 5f);   // 第 i 只瓶子中心的 x 偏移
            Button slot = MakeImageButton(parent, "PotionSlot_" + i, potionSlotSprite,
                new Vector2(0.5f, 1f), Vector2.zero, new Vector2(potionSlotW, potionSlotH),
                OnPotionSlotClicked, captured);
            RectTransform rt = slot.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);              // 让 x/y 直接表示瓶子中心
            rt.anchoredPosition = new Vector2(x, -52.5f);    // 垂直居中于 105 高的长条
            slot.GetComponent<Image>().preserveAspect = true;
            potionSlotButtons.Add(slot);
            potionSlotImages.Add(slot.GetComponent<Image>());

            AddHoverTrigger(slot.gameObject,
                () => ShowPotionTooltip(captured), HidePotionTooltip);
        }
    }

    /// <summary>点饮料槽：喝掉这一瓶（没药水的空槽点了没反应）</summary>
    private void OnPotionSlotClicked(int i)
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || i < 0 || i >= player.Potions.Count) return;
        if (BattleEndPanel.IsOpen) return;   // 结算界面盖住时不吃药（提示框会被压在下面看不见）

        PotionData potion = player.Potions[i];

        if (!IsPlayerPhase())
        {
            ShowMessage("现在不是你的行动阶段，不能使用药水");
            return;
        }

        if (player.UsePotion(i))
        {
            HidePotionTooltip();
            ShowStatus($"使用了【{potion.potionName}】");
        }
        else
        {
            // UsePotion 会挡下"本场次数已用完"的情况，此时道具没有被消耗
            ShowMessage($"【{potion.potionName}】本场已经喝过了，不能再喝");
        }
    }

    /// <summary>建两个悬停浮层：药水说明（饮料槽）/ 技能介绍（插槽卡面 + 左侧怪物技能卡）</summary>
    private void BuildTooltips()
    {
        potionTooltip = MakeTooltipRoot("PotionTooltip", 360f, out potionTooltipRt);
        potionTooltipText = MakeText(potionTooltip.transform, "", 18, TextAnchor.UpperLeft, Color.white);
        RectTransform pt = potionTooltipText.rectTransform;
        Stretch(pt);
        pt.offsetMin = new Vector2(14f, 10f);
        pt.offsetMax = new Vector2(-14f, -10f);
        potionTooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;

        // 遗物图标悬停浮层（生命槽右侧那排）——深蓝长条里没有多余空间，
        // 所以挂在长条下沿之外，和药水浮层一样往下弹。
        relicTooltip = MakeTooltipRoot("RelicTooltip", 360f, out relicTooltipRt);
        relicTooltipText = MakeText(relicTooltip.transform, "", 18, TextAnchor.UpperLeft, Color.white);
        RectTransform rtt = relicTooltipText.rectTransform;
        Stretch(rtt);
        rtt.offsetMin = new Vector2(14f, 10f);
        rtt.offsetMax = new Vector2(-14f, -10f);
        relicTooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;

        skillTooltip = MakeTooltipRoot("SkillTooltip", 430f, out skillTooltipRt);
        skillTooltipTitle = MakeText(skillTooltip.transform, "", 22, TextAnchor.MiddleLeft,
            new Color(1f, 0.94f, 0.72f),
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -12f),
            new Vector2(0.5f, 1f), new Vector2(-28f, 30f));
        skillTooltipBody = MakeText(skillTooltip.transform, "", 18, TextAnchor.UpperLeft,
            new Color(0.93f, 0.95f, 0.98f),
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -48f),
            new Vector2(0.5f, 1f), new Vector2(-28f, 80f));
        skillTooltipBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        skillTooltipBody.verticalOverflow = VerticalWrapMode.Truncate;

        // 第二个浮层：卡牌附带 buff 的说明（悬停带 buff 的卡时出现在技能浮层旁边）
        buffTooltip = MakeTooltipRoot("BuffTooltip", 360f, out buffTooltipRt);
        buffTooltipText = MakeText(buffTooltip.transform, "", 18, TextAnchor.UpperLeft,
            new Color(0.93f, 0.95f, 0.98f));
        RectTransform bt = buffTooltipText.rectTransform;
        Stretch(bt);
        bt.offsetMin = new Vector2(14f, 10f);
        bt.offsetMax = new Vector2(-14f, -10f);
        buffTooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    /// <summary>半透明深色浮层底座（raycastTarget=false，绝不挡点击）</summary>
    private GameObject MakeTooltipRoot(string name, float width, out RectTransform rt)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.13f, 0.95f);
        bg.raycastTarget = false;
        rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 120f);
        go.SetActive(false);
        return go;
    }

    /// <summary>鼠标停在饮料槽：显示药水名 + 效果说明 + 点击提示</summary>
    private void ShowPotionTooltip(int i)
    {
        if (potionTooltip == null) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || i < 0 || i >= player.Potions.Count) { HidePotionTooltip(); return; }

        PotionData potion = player.Potions[i];
        potionTooltipText.text = $"【{potion.potionName}】\n{potion.description}\n点击使用";

        const float w = 360f;
        potionTooltipRt.sizeDelta = new Vector2(w, 120f);            // 先定宽，下面按换行后高度收紧
        potionTooltipRt.sizeDelta = new Vector2(w, potionTooltipText.preferredHeight + 20f);

        // 挂在对应瓶子正下方（瓶子中心 x 与长条里一致）
        float x = 420f + potionSlotW / 2f + i * (potionSlotW + 5f);
        potionTooltipRt.anchorMin = new Vector2(0.5f, 1f);
        potionTooltipRt.anchorMax = new Vector2(0.5f, 1f);
        potionTooltipRt.pivot = new Vector2(0.5f, 1f);
        potionTooltipRt.anchoredPosition = new Vector2(x, -(75f + potionSlotH / 2f + 8f));
        potionTooltip.SetActive(true);

        // 带 buff 效果的药水：第二个弹窗解释 buff（贴在药水浮层正下方）
        ShowPotionBuffTooltip(potion);
    }

    /// <summary>药水说明里附带的 buff 解释文本（施加Buff 效果 + 次数惩罚 buff；无则返回 null）</summary>
    private static string BuildPotionBuffTooltipText(PotionData potion)
    {
        if (potion == null) return null;

        List<string> parts = new List<string>();
        if (potion.effects != null)
        {
            foreach (PotionEffect e in potion.effects)
            {
                if (e == null || e.effectType != PotionEffectType.施加Buff || e.buff == null) continue;
                string line = string.IsNullOrEmpty(e.buff.description)
                    ? "【" + e.buff.buffName + "】"
                    : "【" + e.buff.buffName + "】\n" + e.buff.description;
                if (e.buffStacks > 1) line += "\n层数：" + e.buffStacks;
                parts.Add(line);
            }
        }
        if (potion.HasPenalty && potion.penaltyBuff != null)
        {
            string line = "副作用：【" + potion.penaltyBuff.buffName + "】";
            if (!string.IsNullOrEmpty(potion.penaltyBuff.description))
                line += "\n" + potion.penaltyBuff.description;
            parts.Add(line);
        }
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    /// <summary>悬停药水槽时：buff 说明浮层贴在药水浮层正下方（没有 buff 则隐藏）</summary>
    private void ShowPotionBuffTooltip(PotionData potion)
    {
        if (buffTooltip == null) return;

        string text = BuildPotionBuffTooltipText(potion);
        if (string.IsNullOrEmpty(text)) { buffTooltip.SetActive(false); return; }

        buffTooltipText.text = text;
        const float bw = 360f;
        buffTooltipRt.sizeDelta = new Vector2(bw, 60f);
        buffTooltipRt.sizeDelta = new Vector2(bw, Mathf.Max(60f, buffTooltipText.preferredHeight + 20f));

        buffTooltipRt.anchorMin = potionTooltipRt.anchorMin;
        buffTooltipRt.anchorMax = potionTooltipRt.anchorMax;
        buffTooltipRt.pivot = new Vector2(0.5f, 0f);   // 顶边贴住药水浮层底边
        buffTooltipRt.anchoredPosition = new Vector2(
            potionTooltipRt.anchoredPosition.x,
            potionTooltipRt.anchoredPosition.y - potionTooltipRt.sizeDelta.y - 6f);
        buffTooltip.SetActive(true);
    }

    private void HidePotionTooltip()
    {
        if (potionTooltip != null) potionTooltip.SetActive(false);
        if (buffTooltip != null) buffTooltip.SetActive(false);   // 药水附带的 buff 弹窗一起关
    }

    /// <summary>鼠标停在遗物图标上：显示遗物名 + 效果（内容取 RelicData.description）</summary>
    private void ShowRelicTooltip(int index)
    {
        if (relicTooltip == null) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || index < 0 || index >= player.Relics.Count) { HideRelicTooltip(); return; }

        RelicData relic = player.Relics[index];
        if (relic == null) { HideRelicTooltip(); return; }

        relicTooltipText.text = "【" + relic.relicName + "】\n" +
                                (string.IsNullOrEmpty(relic.description) ? "效果待策划补充" : relic.description);

        const float w = 360f;
        relicTooltipRt.sizeDelta = new Vector2(w, 120f);   // 先定宽，再按换行后的高度收紧
        relicTooltipRt.sizeDelta = new Vector2(w, relicTooltipText.preferredHeight + 20f);

        // 挂在图标正下方：图标中心 x = 栏左缘(166) + i×(64+5) + 32，栏 y = 饮料槽中心
        float cx = 24f + 132f + 10f + index * (relicIconSize + relicIconGap) + relicIconSize / 2f;
        relicTooltipRt.anchorMin = new Vector2(0f, 1f);
        relicTooltipRt.anchorMax = new Vector2(0f, 1f);
        relicTooltipRt.pivot = new Vector2(0.5f, 1f);
        relicTooltipRt.anchoredPosition = new Vector2(cx, relicRowY - relicIconSize / 2f - 8f);
        relicTooltip.SetActive(true);
    }

    private void HideRelicTooltip()
    {
        if (relicTooltip != null) relicTooltip.SetActive(false);
    }

    // ============================================================
    //   玩家 buff 栏（左侧空白区：敌人技能栏与耐力槽之间）
    // ============================================================

    /// <summary>建 buff 栏容器（图标由 RefreshBuffBar 按需补建）</summary>
    private void BuildBuffBar()
    {
        buffBar = new GameObject("PlayerBuffBar", typeof(RectTransform));
        buffBar.transform.SetParent(canvas.transform, false);
        RectTransform rt = buffBar.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(buffBarX, buffBarY);
        rt.sizeDelta = new Vector2(buffBarW, 200f);
    }

    /// <summary>主角 buff 列表（无效实例过滤掉）</summary>
    private static List<BuffInstance> CollectPlayerBuffs()
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        List<BuffInstance> src = player != null ? player.GetActiveBuffs() : null;
        List<BuffInstance> result = new List<BuffInstance>();
        if (src == null) return result;
        foreach (BuffInstance b in src)
            if (b != null && b.data != null && !string.IsNullOrEmpty(b.data.buffName))
                result.Add(b);
        return result;
    }

    /// <summary>
    /// 每帧刷新 buff 栏：签名（名字+层数+剩余回合）没变就跳过，变了才重建图标。
    /// buff 的增减 / 叠层 / 衰减都体现在签名里，不用逐个事件监听。
    /// </summary>
    private void RefreshBuffBar()
    {
        if (buffBar == null) return;

        List<BuffInstance> buffs = CollectPlayerBuffs();
        System.Text.StringBuilder sig = new System.Text.StringBuilder();
        foreach (BuffInstance b in buffs)
            sig.Append(b.data.buffName).Append('|').Append(b.currentStacks)
               .Append('|').Append(b.remainingDuration).Append(';');

        string sigStr = sig.ToString();
        if (sigStr == buffBarSignature) return;
        buffBarSignature = sigStr;

        // 重建：先清掉旧图标（数量 / 顺序都可能变，重建比逐个对齐省心）
        foreach (RectTransform icon in buffIcons)
            if (icon != null) Destroy(icon.gameObject);
        buffIcons.Clear();

        buffBar.SetActive(buffs.Count > 0);

        int perRow = Mathf.Max(1, Mathf.FloorToInt((buffBarW + buffIconGap) / (buffIconSize + buffIconGap)));
        for (int i = 0; i < buffs.Count; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            RectTransform icon = CreateBuffIcon(buffs[i],
                new Vector2(col * (buffIconSize + buffIconGap), -row * (buffIconSize + buffIconGap)));
            buffIcons.Add(icon);
        }
    }

    /// <summary>一个 buff 图标：极性染色块 + 名字（自适应缩字）+ 层数角标，悬停弹说明</summary>
    private RectTransform CreateBuffIcon(BuffInstance buff, Vector2 pos)
    {
        BuffData data = buff.data;

        Image icon = MakeImage(buffBar.transform, "Buff_" + data.buffName, null,
            data.IsDebuff ? new Color(0.72f, 0.24f, 0.24f, 0.95f)   // 负面：暗红
                          : new Color(0.22f, 0.55f, 0.38f, 0.95f),  // 正面：墨绿
            new Vector2(0f, 1f), new Vector2(0f, 1f), pos,
            new Vector2(buffIconSize, buffIconSize));
        icon.raycastTarget = true;   // 要收悬停

        // 名字：块内居中，字号自适应（长名缩字，不溢出）
        Text nameT = MakeText(icon.transform, data.buffName, 15, TextAnchor.MiddleCenter, Color.white,
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero);
        nameT.resizeTextForBestFit = true;
        nameT.resizeTextMaxSize = 15;
        nameT.resizeTextMinSize = 8;

        // 层数角标（>1 时才显示）
        if (buff.currentStacks > 1)
        {
            Text stackT = MakeText(icon.transform, "×" + buff.currentStacks, 13,
                TextAnchor.LowerRight, new Color(1f, 0.92f, 0.6f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 1f), new Vector2(0f, 0f),
                new Vector2(-4f, 16f));
            stackT.rectTransform.pivot = Vector2.zero;
        }

        // 悬停：弹出 buff 说明（复用 buffTooltip，贴在图标正下方）
        int captured = buffIcons.Count;
        AddHoverTrigger(icon.gameObject,
            () => ShowBuffIconTooltip(captured), HideSkillTooltip);

        return icon.rectTransform;
    }

    /// <summary>悬停 buff 图标：名称 + 层数 / 剩余回合 + 描述，浮层贴图标下方</summary>
    private void ShowBuffIconTooltip(int index)
    {
        List<BuffInstance> buffs = CollectPlayerBuffs();
        if (buffTooltip == null || index < 0 || index >= buffs.Count || buffBar == null) return;

        BuffInstance buff = buffs[index];
        BuffData data = buff.data;

        string life;
        if (buff.remainingDuration < 0) life = "永久";
        else life = "剩余 " + buff.remainingDuration + " 回合";
        string title = "【" + data.buffName + "】" +
                       (buff.currentStacks > 1 ? " ×" + buff.currentStacks : "") +
                       "（" + life + "）";
        buffTooltipText.text = string.IsNullOrEmpty(data.description)
            ? title
            : title + "\n" + data.description;

        const float bw = 360f;
        buffTooltipRt.sizeDelta = new Vector2(bw, 60f);
        buffTooltipRt.sizeDelta = new Vector2(bw, Mathf.Max(60f, buffTooltipText.preferredHeight + 20f));

        // buff 栏容器锚在画布左上 (buffBarX, buffBarY)，容器原点在画布局部系里是：
        //   x = -960 + buffBarX, y = 540 + buffBarY；图标 pos 是相对容器左上角的偏移
        RectTransform iconRt = buffIcons[index];
        Vector2 p = iconRt.anchoredPosition;
        float canvasX = -960f + buffBarX + p.x;
        float canvasY = 540f + buffBarY + p.y - buffIconSize - 6f;   // 图标底缘再往下 6px

        buffTooltipRt.anchorMin = new Vector2(0.5f, 0.5f);
        buffTooltipRt.anchorMax = new Vector2(0.5f, 0.5f);
        buffTooltipRt.pivot = new Vector2(0f, 1f);
        // 靠屏幕左缘时往右收进屏幕
        buffTooltipRt.anchoredPosition = new Vector2(Mathf.Max(canvasX, -960f + 10f), canvasY);
        buffTooltip.SetActive(true);
    }


    /// <summary>鼠标停在底部技能插槽卡面：浮层显示在卡面上方</summary>
    private void ShowSlotSkillTooltip(int slotIndex)
    {
        slotIndex += 2;
        if (cardSystem == null) cardSystem = FindCardSystem();
        if (cardSystem == null || slotIndex >= cardSystem.Slots.Count) { HideSkillTooltip(); return; }

        CardSlot slot = cardSystem.Slots[slotIndex];
        if (slot == null || slot.IsEmpty) { HideSkillTooltip(); return; }

        SetSkillTooltipText(slot.card);

        float rightGap = slotRightGapX + (4 - slotIndex) * (slotCardW + 10f);
        float cx = 960f - rightGap - slotCardW / 2f;     // 卡面中线相对屏幕中心
        float halfW = skillTooltipRt.sizeDelta.x / 2f;
        cx = Mathf.Clamp(cx, -960f + halfW + 12f, 960f - halfW - 12f);

        skillTooltipRt.anchorMin = new Vector2(0.5f, 0f);
        skillTooltipRt.anchorMax = new Vector2(0.5f, 0f);
        skillTooltipRt.pivot = new Vector2(0.5f, 0f);
        skillTooltipRt.anchoredPosition = new Vector2(cx, 25f + slotCardFaceH + 8f);
        skillTooltip.SetActive(true);
        PlaceBuffTooltipBesideSkill();
    }

    /// <summary>鼠标停在左侧怪物技能卡面：浮层显示在卡面右侧</summary>
    private void ShowEnemySkillTooltip(int faceIndex)
    {
        int idx = enemySkillScroll + faceIndex;
        if (idx < 0 || idx >= enemySkillEntries.Count) { HideSkillTooltip(); return; }

        CardData card = enemySkillEntries[idx].Card;
        if (card == null) { HideSkillTooltip(); return; }

        SetSkillTooltipText(card);

        float top = enemyCardFirstTopY + faceIndex * (enemyCardH + enemyCardGap);
        skillTooltipRt.anchorMin = new Vector2(0f, 1f);
        skillTooltipRt.anchorMax = new Vector2(0f, 1f);
        skillTooltipRt.pivot = new Vector2(0f, 1f);
        skillTooltipRt.anchoredPosition = new Vector2(24f + enemyCardW + 8f, -top);
        skillTooltip.SetActive(true);
        PlaceBuffTooltipBesideSkill();
    }

    /// <summary>鼠标停在左下角 推 / 跳 卡面：浮层显示在卡面上方（卡面位置固定，直接算）</summary>
    private void ShowBaseSkillTooltip(int i)
    {
        CardData card = i == 0 ? pushCard : jumpCard;
        if (card == null) { HideSkillTooltip(); return; }

        SetSkillTooltipText(card);

        // 卡面锚定左下角 (0,0)：pos.x = 左缘到屏幕左边的距离 → 中线相对屏幕中心
        float cx = baseCardX + i * (baseCardW + 10f) + baseCardW / 2f - 960f;
        float halfW = skillTooltipRt.sizeDelta.x / 2f;
        cx = Mathf.Clamp(cx, -960f + halfW + 12f, 960f - halfW - 12f);

        skillTooltipRt.anchorMin = new Vector2(0.5f, 0f);
        skillTooltipRt.anchorMax = new Vector2(0.5f, 0f);
        skillTooltipRt.pivot = new Vector2(0.5f, 0f);
        skillTooltipRt.anchoredPosition = new Vector2(cx, 28f + baseCardH + 8f);
        skillTooltip.SetActive(true);
        PlaceBuffTooltipBesideSkill();
    }

    /// <summary>
    /// 鼠标停在「思绪爆发」按钮（下一回合正下方的抽牌按键）上：浮层摆在按钮正上方，
    /// 显示它的名称和功能。说明里的消耗数值取抽牌系统的策划配置（drawThoughtCost），
    /// 不在代码里写死具体数字。
    /// </summary>
    private void ShowThoughtBurstTooltip()
    {
        if (skillTooltip == null) return;

        if (cardSystem == null) cardSystem = FindCardSystem();
        // 配置读不到时退回与 RefreshDeckCostTexts 相同的默认值（真正的数值由策划在 Inspector 填）
        int cost = cardSystem != null ? cardSystem.drawThoughtCost : 5;

        skillTooltipTitle.text = "【思绪爆发】";
        skillTooltipBody.text = "抽牌需要消耗 " + cost + " 点思绪：\n" +
                                "点击后先选攻击 / 防御牌堆，再从弹出的待选卡里挑 1 张装入技能槽。";

        // 与技能介绍浮层同一套写法：先定宽，再按正文换行后的实际高度收紧
        const float w = 430f;
        skillTooltipRt.sizeDelta = new Vector2(w, 150f);
        float bodyH = Mathf.Max(24f, skillTooltipBody.preferredHeight);
        float total = 12f + 30f + 6f + bodyH + 12f;
        skillTooltipRt.sizeDelta = new Vector2(w, total);
        skillTooltipBody.rectTransform.sizeDelta = new Vector2(-28f, bodyH + 2f);

        // 定位：按钮中线 → 相对屏幕中心的 x，夹在屏幕内；浮层底边贴按钮上沿 8px
        float cx = Mathf.Clamp(drawButtonCenterX, -960f + w / 2f + 12f, 960f - w / 2f - 12f);
        skillTooltipRt.anchorMin = new Vector2(0.5f, 0f);
        skillTooltipRt.anchorMax = new Vector2(0.5f, 0f);
        skillTooltipRt.pivot = new Vector2(0.5f, 0f);
        skillTooltipRt.anchoredPosition = new Vector2(cx, 540f + drawButtonTopY + 8f);

        if (buffTooltip != null) buffTooltip.SetActive(false);   // 无 buff 内容，别留下上次的残留
        skillTooltip.SetActive(true);
    }

    /// <summary>填充技能介绍：标题 = 卡名 + 思绪/耐力消耗，正文 = CardData.description；
    /// 卡牌带 buff 时同步填充 buff 说明浮层（没有则藏起来）</summary>
    private void SetSkillTooltipText(CardData card)
    {
        if (skillTooltip == null || card == null) return;

        skillTooltipTitle.text = $"【{card.cardName}】  思绪 {card.thoughtCost} · 耐力 {card.actionCost}";
        skillTooltipBody.text = card.description;

        // 先定宽，再按正文换行后的实际高度收紧浮层（Text.preferredHeight 只跟宽度有关）
        const float w = 430f;
        skillTooltipRt.sizeDelta = new Vector2(w, 150f);
        float bodyH = Mathf.Max(24f, skillTooltipBody.preferredHeight);
        float total = 12f + 30f + 6f + bodyH + 12f;
        skillTooltipRt.sizeDelta = new Vector2(w, total);
        skillTooltipBody.rectTransform.sizeDelta = new Vector2(-28f, bodyH + 2f);

        // ---- 附带 buff：填进旁边的第二个浮层 ----
        if (buffTooltip == null) return;
        string buffText = BuildBuffTooltipText(card);
        if (string.IsNullOrEmpty(buffText))
        {
            buffTooltip.SetActive(false);
            return;
        }
        buffTooltipText.text = buffText;
        const float bw = 360f;
        buffTooltipRt.sizeDelta = new Vector2(bw, 60f);
        buffTooltipRt.sizeDelta = new Vector2(bw, Mathf.Max(60f, buffTooltipText.preferredHeight + 20f));
        buffTooltip.SetActive(true);
    }

    /// <summary>卡牌附带 buff 的说明文本（每个 buff 一段：名称 + 描述；无 buff 返回 null）</summary>
    private static string BuildBuffTooltipText(CardData card)
    {
        if (card == null || card.buffs == null || card.buffs.Count == 0) return null;

        List<string> parts = new List<string>();
        foreach (BuffData b in card.buffs)
        {
            if (b == null) continue;
            parts.Add(string.IsNullOrEmpty(b.description)
                ? "【" + b.buffName + "】"
                : "【" + b.buffName + "】\n" + b.description);
        }
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    /// <summary>把 buff 浮层摆到技能浮层右侧（贴 8px）；右侧放不下就改贴左侧</summary>
    private void PlaceBuffTooltipBesideSkill()
    {
        if (buffTooltip == null || skillTooltip == null) return;
        if (!buffTooltip.activeSelf) return;

        // 与技能浮层共用锚点和 pivot，纵向对齐后横向平移一个浮层宽 + 8px
        buffTooltipRt.anchorMin = skillTooltipRt.anchorMin;
        buffTooltipRt.anchorMax = skillTooltipRt.anchorMax;
        buffTooltipRt.pivot = skillTooltipRt.pivot;

        float w = skillTooltipRt.sizeDelta.x;
        float bw = buffTooltipRt.sizeDelta.x;
        float skillX = skillTooltipRt.anchoredPosition.x;
        float y = skillTooltipRt.anchoredPosition.y;

        // 技能浮层右缘（锚点系内）：pivot.x=0.5 时 = x + w/2；pivot.x=0 时 = x + w
        float rightEdge = skillX + w * (1f - skillTooltipRt.pivot.x);
        buffTooltipRt.anchoredPosition = new Vector2(rightEdge + 8f + bw / 2f, y);

        // 超出屏幕右边 → 改贴技能浮层左侧
        Vector3[] c = new Vector3[4];
        buffTooltipRt.GetWorldCorners(c);
        if (c[2].x > Screen.width - 8f)
            buffTooltipRt.anchoredPosition =
                new Vector2(skillX - w * skillTooltipRt.pivot.x - 8f - bw / 2f, y);
    }

    /// <summary>按卡牌类型选卡面：防御卡用防御牌，攻击卡用攻击牌，空位用空技能卡面</summary>
    private Sprite FaceForCard(CardData card)
    {
        if (card == null) return emptyCardFaceSprite;
        Sprite s = card.IsAttack ? attackCardFaceSprite : defenseCardFaceSprite;
        return s != null ? s : emptyCardFaceSprite;   // 素材缺失时退回空技能卡面
    }

    private void HideSkillTooltip()
    {
        if (skillTooltip != null) skillTooltip.SetActive(false);
        if (buffTooltip != null) buffTooltip.SetActive(false);
    }

    /// <summary>关掉所有悬停浮层并清掉来源（弹模态框 / 切换界面时统一用这个）</summary>
    private void HideAllTooltips()
    {
        tooltipHoverOwner = null;
        HideSkillTooltip();
        HidePotionTooltip();
        HideRelicTooltip();
    }

    /// <summary>
    /// 悬停浮层的保底隐藏（每帧调用）。
    ///
    /// UGUI 的 PointerExit 只在"指针真的移出"时由 EventSystem 派发，下面这些情况收不到，
    /// 浮层就会一直贴在屏幕上，必须重新悬停一次再移开才能关掉（此前的 bug）：
    /// ① 进入瞄准 / 打牌堆 / 选卡时，左下角按钮组与敌方技能栏被整体 SetActive(false)
    ///    ——物体失活不会补发 PointerExit；
    /// ② 卡面 / 待选卡被销毁重建 —— 销毁同样不会补发 PointerExit；
    /// ③ 指针瞬间移出窗口、或被后来居上的全屏层盖住。
    /// 所以这里直接检查"来源按钮还在不在、指针是不是还在它上面"，不满足就关。
    /// </summary>
    private void GuardTooltips()
    {
        bool visible = (skillTooltip != null && skillTooltip.activeSelf)
                       || (buffTooltip != null && buffTooltip.activeSelf)
                       || (potionTooltip != null && potionTooltip.activeSelf)
                       || (relicTooltip != null && relicTooltip.activeSelf);
        if (!visible) return;

        GameObject owner = tooltipHoverOwner;

        // ① 来源已隐藏 / 已销毁：立刻关（这是"选范围时浮层不消失"的主因）
        if (owner == null || !owner.activeInHierarchy)
        {
            HideAllTooltips();
            return;
        }

        // ② 指针已经不在来源按钮的矩形内：补一次 PointerExit 该做的事
        RectTransform rt = owner.transform as RectTransform;
        if (rt == null)
        {
            HideAllTooltips();
            return;
        }

        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam))
            HideAllTooltips();
    }

    /// <summary>给按钮挂"指针进入 / 离开"回调（EventTrigger，不干扰 Button 自己的点击）</summary>
    private void AddHoverTrigger(GameObject go, UnityAction onEnter, UnityAction onExit)
    {
        if (go == null) return;
        EventTrigger et = go.GetComponent<EventTrigger>();
        if (et == null) et = go.AddComponent<EventTrigger>();

        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            tooltipHoverOwner = go;   // 记下来源，供 GuardTooltips() 兜底判断
            onEnter();
        });
        et.triggers.Add(enter);

        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ =>
        {
            if (tooltipHoverOwner == go) tooltipHoverOwner = null;
            onExit();
        });
        et.triggers.Add(exit);
    }

    private Image MakeImage(Transform parent, string name, Sprite sprite, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        Image img = new GameObject(name).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite;
        img.color = color;
        // 纯装饰图不参与射线检测：否则会挡住世界里的格子点击
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        return img;
    }

    private Text MakeText(Transform parent, string content, int size, TextAnchor anchor, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 pivot, Vector2 sizeDelta)
    {
        Text t = new GameObject("Text").AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = uiFont;
        t.text = content;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        // 关键：所有文字都是纯显示，绝不能参与射线检测。
        // hpText 撑满红圈、resourceText 的 RectTransform 铺满整块画布，
        // 一旦 raycastTarget=true，全屏都会被判成"指针在 UI 上"，格子点击全部被吞。
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        return t;
    }

    private Text MakeText(Transform parent, string content, int size, TextAnchor anchor, Color color)
    {
        Text t = MakeText(parent, content, size, anchor, color,
            Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero);
        Stretch(t.rectTransform);
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>按屏幕角锚点生成一个灰底白字按钮（例图：左下灰块 / 右下下一回合）</summary>
    private Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size,
        Color color, UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        RectTransform rt = go.GetComponent<RectTransform>();
        bool right = pos.x < 0;
        bool top = pos.y < 0;
        rt.anchorMin = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        rt.anchorMax = rt.anchorMin;
        rt.pivot = rt.anchorMin;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Text t = MakeText(go.transform, label, 24, TextAnchor.MiddleCenter, Color.white);
        Stretch(t.rectTransform);

        btn.onClick.AddListener(onClick);
        return btn;
    }

    /// <summary>
    /// 用美术素材生成按钮：素材铺满按钮区域，文字叠在中央（插槽显示卡名，插卡按钮无文字）。
    /// index 通过闭包传给回调（三个插槽共用一个回调）。
    /// </summary>
    private Button MakeImageButton(Transform parent, string name, Sprite sprite, Vector2 anchor,
        Vector2 pos, Vector2 size, UnityAction<int> onClick, int index)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = sprite == null ? new Color(0.4f, 0.42f, 0.48f, 0.9f) : Color.white;
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        Text t = MakeText(go.transform, "", 22, TextAnchor.MiddleCenter, Color.white);
        Stretch(t.rectTransform);

        int captured = index;
        btn.onClick.AddListener(() => onClick(captured));
        return btn;
    }

    /// <summary>按钮悬停/按下换成另一张底图（SpriteSwap 过渡；不改颜色，禁用态另用透明度表现）</summary>
    private static void ApplyHoverSpriteSwap(Button btn, Sprite hover)
    {
        if (btn == null || hover == null) return;
        btn.transition = Selectable.Transition.SpriteSwap;
        SpriteState st = btn.spriteState;
        st.highlightedSprite = hover;
        st.pressedSprite = hover;
        btn.spriteState = st;
    }

    /// <summary>翻页箭头可用性：SpriteSwap 不会自动压暗，禁用时手动降透明度</summary>
    private static void SetArrowInteractable(Button btn, bool on)
    {
        if (btn == null) return;
        btn.interactable = on;
        if (btn.targetGraphic != null)
            btn.targetGraphic.color = on ? Color.white : new Color(1f, 1f, 1f, 0.35f);
    }

    /// <summary>屏幕居中锚点的按钮（牌堆选择 / 待选卡用）</summary>
    private Button MakeCenteredButton(Transform parent, string label, Vector2 center, Vector2 size,
        Color color, UnityAction onClick, int fontSize = 24)
    {
        Button btn = MakeButton(parent, label, center, size, color, onClick);
        RectTransform rt = btn.GetComponent<RectTransform>();
        SetCenterAnchor(rt);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;

        Text t = btn.GetComponentInChildren<Text>();
        if (t != null) t.fontSize = fontSize;
        return btn;
    }

    // ============================================================
    //        插卡按钮 → 选牌堆（攻击/防御）→ 3 张待选卡
    // ============================================================

    /// <summary>
    /// 牌堆选择层：点插卡按钮后先清掉画面上的其它弹窗，
    /// 只留两个大型按钮，按屏幕中轴对称排布。
    /// </summary>
    private void BuildDeckPanel()
    {
        deckPanel = new GameObject("DeckPanel");
        deckPanel.transform.SetParent(canvas.transform, false);
        Image bg = deckPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);   // 轻遮罩：压暗场景但仍能看见棋盘
        Stretch(bg.rectTransform);

        // 标题：居中偏上
        deckTitleText = MakeText(deckPanel.transform, "选择要抽取的技能牌堆（消耗 5 点思绪）",
            34, TextAnchor.MiddleCenter, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 220f),
            new Vector2(0.5f, 0.5f), new Vector2(900f, 60f));

        // 两个大型按钮：中轴对称，各距中轴 300px。
        // 顶部放牌堆图标（攻击.png / 技能.png），文字在图标下一行；防御按钮底色改绿色。
        Button atk = MakeCenteredButton(deckPanel.transform, "攻击牌堆\n抽取攻击技能",
            new Vector2(-300f, 20f), new Vector2(420f, 240f),
            new Color(0.55f, 0.2f, 0.2f, 0.96f), () => DrawFromDeck(CardPool.攻击), 34);
        Button def = MakeCenteredButton(deckPanel.transform, "防御牌堆\n抽取防御技能",
            new Vector2(300f, 20f), new Vector2(420f, 240f),
            new Color(0.2f, 0.52f, 0.3f, 0.96f), () => DrawFromDeck(CardPool.防御), 34);
        AddDeckIcon(atk, deckAttackIconSprite);
        AddDeckIcon(def, deckDefenseIconSprite);

        deckCostTexts.Add(MakeText(atk.transform, "消耗思绪 5", 22, TextAnchor.LowerCenter,
            new Color(1f, 0.9f, 0.9f, 0.85f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f),
            new Vector2(0.5f, 0f), new Vector2(380f, 30f)));
        deckCostTexts.Add(MakeText(def.transform, "消耗思绪 5", 22, TextAnchor.LowerCenter,
            new Color(0.9f, 1f, 0.92f, 0.85f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f),
            new Vector2(0.5f, 0f), new Vector2(380f, 30f)));

        // 取消：底部小按钮
        MakeCenteredButton(deckPanel.transform, "取消", new Vector2(0f, -240f),
            new Vector2(240f, 56f), new Color(0.3f, 0.3f, 0.32f), CloseDeckPanel);

        deckPanel.SetActive(false);
    }

    private static void SetCenterAnchor(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void BuildCardPanel()
    {
        cardPanel = new GameObject("CardPanel");
        cardPanel.transform.SetParent(canvas.transform, false);
        Image bg = cardPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        Stretch(bg.rectTransform);

        Button cancel = MakeCenteredButton(cardPanel.transform, "放弃（开抽的思绪不退）",
            new Vector2(0f, -300f), new Vector2(460f, 56f),
            new Color(0.3f, 0.3f, 0.32f), () => cardSystem?.AbandonInfuse());

        cardPanel.SetActive(false);
    }

    // ============================================================
    //      左侧敌方技能栏：上回合怪物准备释放的技能（可灌注）
    // ============================================================

    /// <summary>
    /// 屏幕左侧一列三张空技能卡面：顶部一个尖头朝上的淡蓝色箭头，
    /// 底部一个旋转 180°（尖头朝下）的箭头；怪物超过 3 个时点箭头上下翻找。
    /// 卡面显示"上回合怪物准备释放的技能"——即上回合右侧预告、敌人刚执行完的清单。
    /// </summary>
    private void BuildEnemySkillPanel()
    {
        enemySkillGroup = new GameObject("EnemySkillPanel", typeof(RectTransform));
        enemySkillGroup.transform.SetParent(canvas.transform, false);
        Stretch(enemySkillGroup.GetComponent<RectTransform>());

        const float cardW = 150f;
        const float cardH = 150f * 495f / 379f;   // 196：与底部插槽卡面同比例
        const float gap = 8f;
        const float arrowH = 40f;
        const float topY = 185f;                  // 血量槽（顶边 -24 + 高 132）之下
        float colX = 24f + cardW / 2f;            // 卡面列中线 x
        float arrowW = arrowH * 587f / 447f;      // 箭头素材裁剪后 587×447

        // 顶部箭头：素材本身就是尖头朝上
        enemySkillUpArrow = MakeImageButton(enemySkillGroup.transform, "EnemySkillUpArrow",
            enemySkillArrowSprite, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(arrowW, arrowH), _ => ScrollEnemySkills(-1), 0);
        PlaceByTopCenter(enemySkillUpArrow.GetComponent<RectTransform>(), colX, topY + arrowH / 2f);
        ApplyHoverSpriteSwap(enemySkillUpArrow, enemySkillArrowHoverSprite);   // 悬停换淡蓝色箭头_2

        // 三张卡面：整张可点 → 弹"是否灌注"确认
        float firstCardY = topY + arrowH + 5f;    // 第一张卡面上沿：距顶部箭头下沿 5px

        // 记下布局参数：悬停技能介绍浮层要按这些数值定位到卡面右侧
        enemyCardH = cardH;
        enemyCardFirstTopY = firstCardY;

        for (int i = 0; i < 3; i++)
        {
            float y = firstCardY + i * (cardH + gap);
            Button face = MakeImageButton(enemySkillGroup.transform, "EnemySkillFace_" + i,
                emptyCardFaceSprite, new Vector2(0f, 1f),
                new Vector2(24f, -y), new Vector2(cardW, cardH), OnEnemySkillCardClicked, i);
            enemySkillFaces[i] = face;

            // 鼠标停在这张卡上：右侧浮层显示该技能的介绍
            int capturedFace = i;
            AddHoverTrigger(face.gameObject,
                () => ShowEnemySkillTooltip(capturedFace), HideSkillTooltip);

            // 技能名：素材浅灰文字区（图像 y251~470 → 距底 5%~49%，取中部）
            Text nameT = MakeText(face.transform, "", 20, TextAnchor.MiddleCenter, Color.black,
                new Vector2(0.5f, 0.30f), new Vector2(0.5f, 0.30f), Vector2.zero,
                new Vector2(0.5f, 0.30f), new Vector2(cardW * 0.84f, cardH * 0.20f));
            nameT.horizontalOverflow = HorizontalWrapMode.Wrap;
            enemySkillNameTexts[i] = nameT;

            // 思绪费用：文字区下部小字（卡面文字统一黑字）
            Text costT = MakeText(face.transform, "", 14, TextAnchor.MiddleCenter,
                Color.black,
                new Vector2(0.5f, 0.13f), new Vector2(0.5f, 0.13f), Vector2.zero,
                new Vector2(0.5f, 0.13f), new Vector2(cardW * 0.84f, cardH * 0.10f));
            enemySkillCostTexts[i] = costT;
        }

        // 底部箭头：同一素材旋转 180°（尖头朝下），距最后一张卡面下沿 5px。
        // 必须先把 pivot 挪到中心再旋转——MakeImageButton 默认 pivot = 锚点(0,1)，
        // 绕左上角转 180° 会把整张图甩到左边偏下一格，看起来就没对齐卡面。
        float lastCardBottom = firstCardY + 3 * cardH + 2 * gap;
        enemySkillDownArrow = MakeImageButton(enemySkillGroup.transform, "EnemySkillDownArrow",
            enemySkillArrowSprite, new Vector2(0f, 1f),
            Vector2.zero, new Vector2(arrowW, arrowH), _ => ScrollEnemySkills(1), 0);
        RectTransform downRt = enemySkillDownArrow.GetComponent<RectTransform>();
        PlaceByTopCenter(downRt, colX, lastCardBottom + 5f + arrowH / 2f);
        downRt.localRotation = Quaternion.Euler(0f, 0f, 180f);
        ApplyHoverSpriteSwap(enemySkillDownArrow, enemySkillArrowHoverSprite);   // 悬停同样换图（旋转不受影响）

        // 初始（第 1 回合）没有"上回合技能"：先把卡面刷成"空"
        RebuildEnemySkillEntries();
    }

    /// <summary>顶部锚点 + 中心 pivot 定位：anchoredPosition 直接表示矩形中心（旋转不会漂移）</summary>
    private static void PlaceByTopCenter(RectTransform rt, float centerX, float centerYFromTop)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(centerX, -centerYFromTop);
    }

    /// <summary>
    /// 重新收集左侧卡面的技能列表。
    /// 数据源只有每个敌人的 lastTurnUsedSkills（上回合"准备释放"并已执行的技能，保留重复）。
    /// 第 1 回合还没有"上回合"，列表就是空的（卡面显示"空"）——
    /// 不要再用本回合 PlannedSkills 兜底，那会把"本回合预告"提前泄露到左栏。
    /// </summary>
    private void RebuildEnemySkillEntries()
    {
        enemySkillEntries.Clear();

        EnemyUnit[] enemies = FindObjectsOfType<EnemyUnit>();
        foreach (EnemyUnit e in enemies)
        {
            if (e == null || e.IsDead) continue;
            foreach (CardData card in e.LastTurnUsedSkills)
                if (card != null)
                    enemySkillEntries.Add(new EnemySkillEntry { Owner = e, Card = card });
        }

        enemySkillScroll = Mathf.Clamp(enemySkillScroll, 0, Mathf.Max(0, enemySkillEntries.Count - 3));
        RefreshEnemySkillCards();
    }

    /// <summary>把滚动窗口起点起的 3 条记录写进卡面文字；箭头在列表尽头时禁用</summary>
    private void RefreshEnemySkillCards()
    {
        for (int i = 0; i < 3; i++)
        {
            int idx = enemySkillScroll + i;
            CardData card = idx < enemySkillEntries.Count ? enemySkillEntries[idx].Card : null;

            // 卡面随技能类型切换：攻击卡 → 攻击牌，防御卡 → 防御牌（无技能时保持空技能卡面）
            if (enemySkillFaces[i] != null && enemySkillFaces[i].image != null)
            {
                Sprite wantFace = FaceForCard(card);
                if (enemySkillFaces[i].image.sprite != wantFace)
                {
                    enemySkillFaces[i].image.sprite = wantFace;
                    enemySkillFaces[i].image.color = Color.white;
                }
            }

            if (enemySkillNameTexts[i] != null)
            {
                string wantName = card != null ? card.cardName : "空";
                if (enemySkillNameTexts[i].text != wantName) enemySkillNameTexts[i].text = wantName;
                // 卡面文字统一黑字（空位也是黑字，不再用灰色）
                if (enemySkillNameTexts[i].color != Color.black) enemySkillNameTexts[i].color = Color.black;
            }

            if (enemySkillCostTexts[i] != null)
            {
                string wantCost = card != null ? "思绪 " + card.thoughtCost : "";
                if (enemySkillCostTexts[i].text != wantCost) enemySkillCostTexts[i].text = wantCost;
            }

            if (enemySkillFaces[i] != null)
                enemySkillFaces[i].interactable = CardPoolRules.IsEnemySkillInfusionEligible(card);
        }

        if (enemySkillUpArrow != null)
            SetArrowInteractable(enemySkillUpArrow, enemySkillScroll > 0);
        if (enemySkillDownArrow != null)
            SetArrowInteractable(enemySkillDownArrow, enemySkillScroll < enemySkillEntries.Count - 3);
    }

    /// <summary>点箭头上下翻找：dir = -1 向上（看更前面的怪）/ +1 向下</summary>
    private void ScrollEnemySkills(int dir)
    {
        int max = Mathf.Max(0, enemySkillEntries.Count - 3);
        int want = Mathf.Clamp(enemySkillScroll + dir, 0, max);
        if (want == enemySkillScroll) return;
        enemySkillScroll = want;
        RefreshEnemySkillCards();
    }

    /// <summary>点左侧卡面：先查下方三个技能栏是否已满，未满才弹"是否花费思绪灌注"确认框</summary>
    private void OnEnemySkillCardClicked(int i)
    {
        if (!IsPlayerPhase()) return;
        if (cardSystem == null) cardSystem = FindCardSystem();
        if (cardSystem == null || cardSystem.IsChoosing) return;   // 选牌流程进行中不给灌注

        int idx = enemySkillScroll + i;
        if (idx < 0 || idx >= enemySkillEntries.Count) return;
        CardData card = enemySkillEntries[idx].Card;
        if (!CardPoolRules.IsEnemySkillInfusionEligible(card)) return;

        // 下方三张技能插槽已经全部有技能 → 直接提示，不再走灌注确认
        if (IsVisibleSlotFull())
        {
            ShowMessage("当前技能槽已满，无法进行灌注");
            return;
        }

        ShowInfuseConfirm(card);
    }

    /// <summary>
    /// 下方三张技能插槽（屏幕底部，UI 固定 3 格）是否已全部占用。
    /// 槽位数据可能配置了超过 3 个（PlayerCardSystem.slotCount 默认 5），
    /// 这里按"玩家能看到的三个技能栏"判断，取前 3 个槽位。
    /// </summary>
    private bool IsVisibleSlotFull()
    {
        if (cardSystem == null) cardSystem = FindCardSystem();
        if (cardSystem == null) return false;

        IReadOnlyList<CardSlot> slots = cardSystem.Slots;
        int visible = Mathf.Min(3, Mathf.Max(0, slots.Count - 2));
        if (visible <= 0) return false;

        for (int i = 0; i < visible; i++)
            if (slots[i + 2] == null || slots[i + 2].IsEmpty)
                return false;   // 还有空槽 → 可以灌注
        return true;            // 三格都有技能 → 已满
    }

    // ------------------------------------------------------------
    //               灌注确认弹窗 / 结果提示弹窗
    // ------------------------------------------------------------

    /// <summary>模态确认框：确认 = 灌注，取消 = 什么都不发生</summary>
    private void BuildInfuseConfirmPanel()
    {
        confirmPanel = new GameObject("InfuseConfirmPanel");
        confirmPanel.transform.SetParent(canvas.transform, false);
        Image bg = confirmPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);   // 遮罩挡住后面的点击
        Stretch(bg.rectTransform);

        GameObject box = new GameObject("DialogBox", typeof(RectTransform));
        box.transform.SetParent(confirmPanel.transform, false);
        Image boxImg = box.AddComponent<Image>();
        boxImg.color = new Color(0.13f, 0.17f, 0.24f, 0.97f);
        RectTransform boxRt = box.GetComponent<RectTransform>();
        SetCenterAnchor(boxRt);
        boxRt.sizeDelta = new Vector2(560f, 280f);

        confirmText = MakeText(box.transform, "", 26, TextAnchor.UpperCenter, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f),
            new Vector2(0.5f, 1f), new Vector2(490f, 140f));
        confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;

        MakeCenteredButton(box.transform, "确认灌注", new Vector2(-130f, -90f), new Vector2(220f, 60f),
            new Color(0.25f, 0.5f, 0.28f, 0.97f), OnConfirmInfuseClicked, 26);
        MakeCenteredButton(box.transform, "取消", new Vector2(130f, -90f), new Vector2(220f, 60f),
            new Color(0.32f, 0.32f, 0.34f, 0.97f), CloseInfuseConfirm, 26);

        confirmPanel.SetActive(false);
    }

    private void ShowInfuseConfirm(CardData card)
    {
        HideAllTooltips();
        pendingInfuseCard = card;
        if (confirmText != null)
            confirmText.text = "是否花费 " + card.thoughtCost + " 点思绪\n来灌注当前选中技能？\n\n【" + card.cardName + "】";
        confirmPanel.SetActive(true);
    }

    private void CloseInfuseConfirm()
    {
        pendingInfuseCard = null;
        if (confirmPanel != null) confirmPanel.SetActive(false);
    }

    /// <summary>确认灌注：成功后底部技能槽出现"复制的技能"（SlotChanged 自动刷新）</summary>
    private void OnConfirmInfuseClicked()
    {
        CardData card = pendingInfuseCard;
        CloseInfuseConfirm();
        if (card == null) return;
        if (cardSystem == null) cardSystem = FindCardSystem();
        if (cardSystem == null) return;

        InfuseResult result = cardSystem.TryInfuseCardDirectly(card);
        switch (result)
        {
            case InfuseResult.成功:
                break;   // 底部插槽由 SlotChanged 事件自动刷新
            case InfuseResult.槽位已满:
                ShowMessage("当前技能槽已满，灌注失败");
                break;
            case InfuseResult.思绪不足:
                ShowMessage("思绪不足，无法灌注当前技能");
                break;
            default:
                ShowMessage("灌注失败：" + result);
                break;
        }
    }

    /// <summary>纯提示弹窗：只有一个"确定"按钮</summary>
    private void BuildMessagePanel()
    {
        messagePanel = new GameObject("MessagePanel");
        messagePanel.transform.SetParent(canvas.transform, false);
        Image bg = messagePanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        Stretch(bg.rectTransform);

        GameObject box = new GameObject("DialogBox", typeof(RectTransform));
        box.transform.SetParent(messagePanel.transform, false);
        Image boxImg = box.AddComponent<Image>();
        boxImg.color = new Color(0.13f, 0.17f, 0.24f, 0.97f);
        RectTransform boxRt = box.GetComponent<RectTransform>();
        SetCenterAnchor(boxRt);
        boxRt.sizeDelta = new Vector2(560f, 220f);

        messageText = MakeText(box.transform, "", 26, TextAnchor.MiddleCenter, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -32f),
            new Vector2(0.5f, 1f), new Vector2(490f, 120f));
        messageText.horizontalOverflow = HorizontalWrapMode.Wrap;

        MakeCenteredButton(box.transform, "确定", new Vector2(0f, -72f), new Vector2(220f, 56f),
            new Color(0.32f, 0.32f, 0.34f, 0.97f), CloseMessagePanel, 26);

        messagePanel.SetActive(false);
    }

    private void ShowMessage(string msg)
    {
        HideAllTooltips();   // 弹模态框时先把悬停浮层收掉，避免两个框叠在屏幕上
        if (messageText != null) messageText.text = msg;
        if (messagePanel != null) messagePanel.SetActive(true);
    }

    private void CloseMessagePanel()
    {
        if (messagePanel != null) messagePanel.SetActive(false);
    }

    /// <summary>
    /// 执行一次技能动作，失败时弹窗说明**原因**。
    ///
    /// 原因由 PlayerCardSystem（阶段/槽位/次数/射程/视线/亲密）、
    /// PlayerUnit（耐力/行动次数）、DefenseSkillResolver（防御选格）写进 SkillFailure，
    /// 这里取一次即清空；万一某条分支没写原因，退回 fallback 那句通用提示。
    /// </summary>
    private bool RunCardAction(System.Func<bool> action, string fallback)
    {
        if (action == null) return false;

        SkillFailure.Clear();
        if (action()) return true;

        string reason = SkillFailure.Consume();
        ShowMessage(string.IsNullOrEmpty(reason) ? fallback : reason);
        return false;
    }

    /// <summary>插卡按钮：清掉画面上的弹窗，先弹牌堆选择（两个大型按钮）</summary>
    private void OnDrawButtonClicked(int _)
    {
        if (!IsPlayerPhase()) return;
        cardSystem = FindCardSystem();
        if (cardSystem == null) return;

        // 需求：点插卡按钮后，画面上的全部弹窗先暂时消失
        if (cardPanel != null) cardPanel.SetActive(false);

        if (cardSystem.IsChoosing) { ShowCardChoices(); return; }   // 已有待选卡 → 直接回到卡牌界面

        ExitMode();
        RefreshDeckCostTexts();
        deckPanel.SetActive(true);   // 先选攻击/防御牌堆
        DimEnemies(true);
    }

    /// <summary>标题与按钮上的费用小字跟随策划配置（drawThoughtCost）</summary>
    private void RefreshDeckCostTexts()
    {
        int cost = cardSystem != null ? cardSystem.drawThoughtCost : 5;
        if (deckTitleText != null)
            deckTitleText.text = "选择要抽取的技能牌堆（消耗 " + cost + " 点思绪）";
        foreach (Text t in deckCostTexts)
            if (t != null) t.text = "消耗思绪 " + cost;
    }

    private void CloseDeckPanel()
    {
        if (deckPanel != null) deckPanel.SetActive(false);
        if (mode == SelectMode.无 && (cardPanel == null || !cardPanel.activeSelf))
            DimEnemies(false);
    }

    /// <summary>牌堆按钮顶部图标：图标居中放在按钮最上方，文字块整体下移一行让位</summary>
    private void AddDeckIcon(Button btn, Sprite icon)
    {
        if (btn == null) return;

        // 文字块：从"撑满按钮"改为锚在图标下方（两行居中，缩小一号）
        Text label = btn.GetComponentInChildren<Text>();
        if (label != null)
        {
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0.5f, 1f);
            lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, -114f);
            lrt.sizeDelta = new Vector2(380f, 74f);
            label.fontSize = 30;
        }

        // 图标：按钮顶部居中，preserveAspect 防拉伸；纯装饰图不挡点击
        if (icon == null) return;
        Image img = MakeImage(btn.transform, "DeckIcon", icon, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -10f), new Vector2(110f, 84f));
        img.preserveAspect = true;
    }

    /// <summary>选了牌堆：抽对应牌库，成功后切到 3 张居中卡牌</summary>
    private void DrawFromDeck(CardPool pool)
    {
        if (cardSystem == null) cardSystem = FindCardSystem();
        if (cardSystem == null) return;

        // 思绪不足：直接弹窗拦下（BeginDraw 里扣费失败只在 Console 打日志，玩家看不到）
        PlayerUnit drawer = FindObjectOfType<PlayerUnit>();
        if (drawer != null && drawer.CurrentThought < cardSystem.drawThoughtCost)
        {
            CloseDeckPanel();
            ShowMessage("思绪不足，无法灌注当前技能");
            return;
        }

        if (!cardSystem.BeginDraw(pool))
        {
            ShowStatus("抽卡失败（思绪不足 / 槽位已满，详见 Console）");
            CloseDeckPanel();
            return;
        }
        deckPanel.SetActive(false);
        ShowCardChoices();
    }

    private void ShowCardChoices()
    {
        cardPanel.SetActive(true);
        DimEnemies(true);

        // 清掉上一轮的卡牌（子物体：0=底图，1=放弃按钮，2 起是卡牌）
        for (int i = cardPanel.transform.childCount - 1; i >= 2; i--)
            Destroy(cardPanel.transform.GetChild(i).gameObject);

        IReadOnlyList<CardData> choices = cardSystem.CurrentChoices;
        for (int i = 0; i < choices.Count; i++)
        {
            CardData card = choices[i];
            if (card == null) continue;
            MakeChoiceCard(cardPanel.transform, card, i, choices.Count);
        }
    }

    /// <summary>一张待选卡：按类型选攻击牌/防御牌卡面 + 卡名 + 消耗 + 技能介绍，居中排列，点击灌注</summary>
    private void MakeChoiceCard(Transform parent, CardData card, int index, int total)
    {
        const float w = 320f, gap = 40f;
        float h = w * 495f / 379f;                 // ≈418：与空技能卡面同比例，不拉伸
        float x = (index - (total - 1) / 2f) * (w + gap);
        CardData captured = card;

        Button b = MakeImageButton(parent, "ChoiceCard_" + index, FaceForCard(card),
            new Vector2(0.5f, 0.5f), new Vector2(x, 30f), new Vector2(w, h),
            _ => TryInfuse(captured), 0);

        // 鼠标悬停：卡面右侧浮层显示技能介绍（带 buff 时旁边加 buff 浮层）
        AddHoverTrigger(b.gameObject,
            () => ShowChoiceCardTooltip(captured, b.image.rectTransform), HideSkillTooltip);

        // 卡名：写进卡面下方浅灰文字区的上沿（图像 y251~470 → 归一化 0.49~0.05）
        Text nameT = MakeText(b.transform, "【" + card.cardName + "】", 28, TextAnchor.MiddleCenter,
            Color.black,
            new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f), Vector2.zero,
            new Vector2(0.5f, 0.45f), new Vector2(w * 0.86f, 34f));
        nameT.horizontalOverflow = HorizontalWrapMode.Wrap;

        // 消耗（卡面文字统一黑字）
        MakeText(b.transform, "思绪 " + card.thoughtCost + " · 耐力 " + card.actionCost,
            19, TextAnchor.MiddleCenter, Color.black,
            new Vector2(0.5f, 0.385f), new Vector2(0.5f, 0.385f), Vector2.zero,
            new Vector2(0.5f, 0.385f), new Vector2(w * 0.86f, 26f));

        // 技能介绍（CardData.description，自动换行，放不下就截断）
        Text desc = MakeText(b.transform, card.description, 16, TextAnchor.UpperCenter,
            Color.black,
            new Vector2(0.5f, 0.35f), new Vector2(0.5f, 0.35f), Vector2.zero,
            new Vector2(0.5f, 1f), new Vector2(w * 0.86f, h * 0.29f));
        desc.horizontalOverflow = HorizontalWrapMode.Wrap;
        desc.verticalOverflow = VerticalWrapMode.Truncate;
    }

    private void TryInfuse(CardData card)
    {
        if (cardSystem == null) return;

        // 选牌灌注同样要付卡的思绪：不足时弹窗拦下（ConfirmInfuse 失败只会留 Console 日志）
        PlayerUnit infuser = FindObjectOfType<PlayerUnit>();
        if (infuser != null && card != null && infuser.CurrentThought < card.thoughtCost)
        {
            ShowMessage("思绪不足，无法灌注当前技能");
            return;
        }

        IReadOnlyList<CardData> choices = cardSystem.CurrentChoices;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i] == card)
            {
                cardSystem.ConfirmInfuse(i);   // 成功会触发 CardOfferClosed；失败留在选牌界面改选
                return;
            }
        }
    }

    /// <summary>鼠标停在抽卡待选卡上：浮层显示在卡面右侧（卡面已印介绍，这里主要补 buff 说明）</summary>
    private void ShowChoiceCardTooltip(CardData card, RectTransform face)
    {
        if (card == null || face == null) { HideSkillTooltip(); return; }

        SetSkillTooltipText(card);

        // 卡面中心（世界坐标 = overlay 屏幕像素）换算成 Canvas 本地坐标再摆浮层
        RectTransform canvasRt = canvas.transform as RectTransform;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, face.position);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, screenPos, null, out Vector2 local))
        { HideSkillTooltip(); return; }

        float hw = face.rect.width / 2f;   // face.rect 已是 Canvas 本地单位
        skillTooltipRt.anchorMin = new Vector2(0.5f, 0.5f);
        skillTooltipRt.anchorMax = new Vector2(0.5f, 0.5f);
        skillTooltipRt.pivot = new Vector2(0f, 0.5f);
        skillTooltipRt.anchoredPosition = new Vector2(local.x + hw + 8f, local.y);

        // 右侧放不下 → 改挂卡面左侧
        if (local.x + hw + 8f + skillTooltipRt.sizeDelta.x > canvasRt.rect.width / 2f)
        {
            skillTooltipRt.pivot = new Vector2(1f, 0.5f);
            skillTooltipRt.anchoredPosition = new Vector2(local.x - hw - 8f, local.y);
        }
        skillTooltip.SetActive(true);
        PlaceBuffTooltipBesideSkill();
    }

    /// <summary>
    /// 这张牌"实际"要付多少耐力 —— 预检必须和 PlayerCardSystem 的扣费口径一致：
    ///   · 攻击 / 基础技能：CardData.GetPlayActionCost（"连续拳"这类按本回合已用次数打折）；
    ///   · 防御技能：DefenseCombatState.GetStaminaCost（"自动屏障"首用 -3）。
    /// 原来预检一律写 GetFinalActionCost，碰上上面两种折扣就会误报"耐力不足"，
    /// 明明付得起却连瞄准都进不去。
    /// </summary>
    private static int StaminaCostOf(CardData card, PlayerUnit player)
    {
        if (card == null) return 0;
        if (player == null) return card.GetFinalActionCost(null);
        return card.IsAttack ? card.GetPlayActionCost(player)
                             : player.DefenseState.GetStaminaCost(card, player);
    }

    private PlayerCardSystem FindCardSystem()
    {
        if (cardSystem == null)
        {
            PlayerUnit p = FindObjectOfType<PlayerUnit>();
            if (p != null) cardSystem = p.GetComponent<PlayerCardSystem>();
        }
        return cardSystem;
    }

    // ============================================================
    //          基础技能 推 / 跳：常驻按钮 + 瞄准模式
    // ============================================================

    private void EnterMode(SelectMode target)
    {
        if (!IsPlayerPhase()) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || player.IsDead) return;

        // 再点同一个按钮 = 取消
        if (mode == target) { ExitMode(); return; }

        // 推 / 跳 也要先看耐力：不足直接弹窗，不进入瞄准
        CardData basicCard = target == SelectMode.推 ? pushCard :
                             (target == SelectMode.跳 ? jumpCard : null);
        if (basicCard != null && player.CurrentStamina < StaminaCostOf(basicCard, player))
        {
            ShowMessage("耐力不足，无法释放技能");
            return;
        }

        ExitMode();
        mode = target;

        if (target == SelectMode.推) HighlightPushTargets(player);
        else if (target == SelectMode.跳) HighlightJumpCells(player);

        DimEnemies(true);   // 例图：选择技能时小怪半透明
    }

    /// <summary>推：按卡牌射程（minRange~maxRange）标出范围内的敌人（红圈加深）</summary>
    private void HighlightPushTargets(PlayerUnit player)
    {
        if (pushCard == null || HexGrid.Instance == null) return;
        int min = Mathf.Max(0, pushCard.minRange);
        int max = Mathf.Max(pushCard.maxRange, min);

        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(player.GetCoord(), max))
        {
            if (cell == null) continue;
            int dist = cell.coord.Distance(player.GetCoord());
            if (dist < min || dist > max) continue;

            Unit occupant = cell.occupyUnit;
            if (occupant is EnemyUnit && !occupant.IsDead)
                cell.SetTargetHighlight(true);   // 有敌人：红圈加深（可点）
        }
    }

    /// <summary>跳：按卡牌射程标出可以落脚的格子（绿圈，可跨越障碍）</summary>
    private void HighlightJumpCells(PlayerUnit player)
    {
        if (jumpCard == null || HexGrid.Instance == null) return;
        int min = Mathf.Max(1, jumpCard.minRange);
        int max = Mathf.Max(jumpCard.maxRange, min);

        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(player.GetCoord(), max))
        {
            if (cell == null) continue;
            int dist = cell.coord.Distance(player.GetCoord());
            if (dist < min || dist > max) continue;

            // CanStopOn = 可通行 + 无单位 + 无阻挡障碍；跳的落点只要求能站住
            if (cell.CanStopOn(player))
                cell.SetMoveRangeHighlight(true);   // 绿圈
        }
    }

    /// <summary>退出瞄准模式：清高亮与瞄准状态、恢复小怪透明度</summary>
    private void ExitMode()
    {
        if (mode != SelectMode.无)
        {
            mode = SelectMode.无;
            aimSlot = -1;
            pendingAimCells.Clear();
            HexGrid.Instance?.ClearAllHighlights();
        }
        else
        {
            aimSlot = -1;
            pendingAimCells.Clear();
        }
        DimEnemies(false);
    }

    /// <summary>下一回合按钮：选择中 = 取消，否则结束回合</summary>
    private void OnEndTurnClicked()
    {
        if (mode != SelectMode.无 || (deckPanel != null && deckPanel.activeSelf)) { CancelAll(); return; }
        if (cardSystem != null && cardSystem.IsChoosing) { CancelAll(); return; }
        EndTurn();
    }

    private void CancelAll()
    {
        ExitMode();
        CloseDeckPanel();
        if (cardSystem != null && cardSystem.IsChoosing)
            cardSystem.AbandonInfuse();   // 规则：开抽的 5 点思绪不退
    }

    private void EndTurn()
    {
        if (!IsPlayerPhase()) return;
        ExitMode();
        if (TurnManager.Instance != null)
            TurnManager.Instance.RequestEndPlayerTurn();
        else if (GameManager.Instance != null)
            GameManager.Instance.EndPlayerTurn();
    }

    // ============================================================
    //          技能插槽：点击 → 按技能规则瞄准 → 释放
    // ============================================================

    /// <summary>点击第 i 张技能插槽：进入该技能的瞄准模式；再点同一张 = 取消</summary>
    private void EnterSlotMode(int slotIndex)
    {
        slotIndex += 2;
        if (!IsPlayerPhase()) return;
        cardSystem = FindCardSystem();
        if (cardSystem == null || slotIndex >= cardSystem.Slots.Count) return;

        // 再点同一个插槽 = 取消
        if (mode == SelectMode.技能 && aimSlot == slotIndex) { ExitMode(); return; }

        CardSlot slot = cardSystem.Slots[slotIndex];
        if (slot.IsEmpty || !slot.CanUse)
        {
            ShowStatus("该插槽没有可用技能");
            return;
        }

        ExitMode();
        mode = SelectMode.技能;
        aimSlot = slotIndex;
        pendingAimCells.Clear();

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        CardData card = slot.card;
        if (player == null || card == null) { ExitMode(); return; }

        // 耐力不足：直接弹窗，不进入瞄准（否则要等选完格子才在 Console 里失败）
        if (player.CurrentStamina < StaminaCostOf(card, player))
        {
            ExitMode();
            ShowMessage("耐力不足，无法释放技能");
            return;
        }

        // 防御卡且"对自己用、且空选择就算合法"：点插槽直接释放，无需瞄准。
        // 这里不能只判断 defenseTargetRule == 自身 —— 还有例外：
        //   · 神圣祝福：自身带「神圣」时必须选一只小天使（IsValidSelection 要求恰好 1 个目标），
        //     直接拿空选择去放会 100% 失败，表现为"点了没反应"；
        //   · 错乱传送之类的自身卡，落点为空时同样要判一次。
        // 所以用结算器自己的 CanResolve(空选择) 当判据：它说行才走快捷释放。
        if (!card.IsAttack && card.defenseTargetRule == DefenseTargetRule.自身 &&
            DefenseSkillResolver.CanResolve(card, player, new HexCoord[0]))
        {
            bool ok = RunCardAction(() => cardSystem.UseSlotCard(slotIndex, player),
                $"【{card.cardName}】释放失败");
            if (ok) ShowStatus("对自身使用【" + card.cardName + "】");
            mode = SelectMode.无;
            aimSlot = -1;
            return;
        }

        RefreshSkillHighlights();
        DimEnemies(true);   // 例图：选择技能时小怪半透明
    }

    /// <summary>按当前瞄准技能的选格规则重画高亮（多目标每选一次都会重画）</summary>
    private void RefreshSkillHighlights()
    {
        HexGrid.Instance?.ClearAllHighlights();
        if (mode != SelectMode.技能 || cardSystem == null) return;
        if (aimSlot < 0 || aimSlot >= cardSystem.Slots.Count) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        CardData card = cardSystem.Slots[aimSlot].card;
        if (player == null || card == null || HexGrid.Instance == null) return;

        bool noCenter = card.IsAttack &&
            (card.attackTargetRule == AttackTargetRule.自身范围 ||
             card.attackTargetRule == AttackTargetRule.全场 ||
             card.attackTargetRule == AttackTargetRule.全场随机 ||
             card.attackTargetRule == AttackTargetRule.固定地图图案);

        foreach (HexCell cell in HexGrid.Instance.GetAllCells())
        {
            if (cell == null) continue;

            // 已选中的多目标格用深红圈区分
            if (pendingAimCells.Contains(cell.coord)) { cell.SetTargetHighlight(true); continue; }

            if (noCenter)
            {
                if (InClickToCastCells(card, cell.coord, player)) cell.SetAimHighlight(true);
            }
            else if (card.IsAttack)
            {
                if (card.CanSelectAttackCell(player, cell.coord)) cell.SetAimHighlight(true);
            }
            else
            {
                // 对敌/对格防御卡：合法性由防御结算器判定
                if (DefenseSkillResolver.CanResolve(card, player, new[] { cell.coord }))
                    cell.SetAimHighlight(true);
            }
        }
    }

    /// <summary>无需另选中心格的范围技能：这些高亮格都可以点击以释放</summary>
    private static bool InClickToCastCells(CardData card, HexCoord coord, PlayerUnit player)
    {
        if (card.attackTargetRule == AttackTargetRule.固定地图图案)
        {
            // 礼炮轰炸在释放时才随机选四图之一，选牌时显示四图可能覆盖的格子。
            for (int pattern = 0; pattern < 4; pattern++)
                foreach (HexCoord c in AttackAreaGeometry.GetFixedBombardmentPattern(pattern))
                    if (c == coord && HexGrid.Instance.GetCell(c) != null) return true;
            return false;
        }

        if (card.attackTargetRule == AttackTargetRule.全场随机)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            Unit occupant = cell != null ? cell.occupyUnit : null;
            return occupant != null && !occupant.IsDead && occupant is EnemyUnit;
        }

        foreach (AttackAreaCell entry in card.GetAttackArea(player, player.GetCoord()))
            if (entry.Coord == coord && HexGrid.Instance.GetCell(entry.Coord) != null) return true;
        return false;
    }

    /// <summary>技能瞄准中点击某格 → 按选格规则释放（两条输入通道共用）</summary>
    private void ExecuteSkillCell(HexCell cell)
    {
        if (cardSystem == null || aimSlot < 0 || aimSlot >= cardSystem.Slots.Count) { ExitMode(); return; }
        CardSlot slot = cardSystem.Slots[aimSlot];
        CardData card = slot.card;
        if (card == null) { ExitMode(); return; }
        if (!slot.CanUse)
        {
            ExitMode();
            ShowMessage($"【{card.cardName}】的使用次数已经用完");
            return;
        }

        // 兜底：瞄准期间耐力被扣（buff 等）导致不足时，弹窗而不是只打 Console
        PlayerUnit caster = FindObjectOfType<PlayerUnit>();
        if (caster != null && caster.CurrentStamina < StaminaCostOf(card, caster))
        {
            ExitMode();
            ShowMessage("耐力不足，无法释放技能");
            return;
        }

        // ---------- 防御/增益卡：点击合法目标格（自身型已在进槽时直接释放） ----------
        if (!card.IsAttack)
        {
            bool ok = RunCardAction(() => cardSystem.TryUseDefenseCardOnCells(aimSlot, new[] { cell.coord }),
                $"【{card.cardName}】释放失败");
            if (ok)
            {
                ShowStatus("【" + card.cardName + "】已释放");
                ExitMode();
            }
            return;
        }

        // ---------- 攻击卡：按选格规则分发 ----------
        bool done = false;
        switch (card.attackTargetRule)
        {
            case AttackTargetRule.单位:
            {
                Unit target = cell.occupyUnit;
                if (target is EnemyUnit && !target.IsDead)
                    done = RunCardAction(() => cardSystem.UseSlotCard(aimSlot, target),
                        $"【{card.cardName}】释放失败");
                else if (cell.obstacle != null && card.CanTargetObstacle)
                    done = RunCardAction(() => cardSystem.UseSlotCardOnObstacle(aimSlot, cell.obstacle),
                        $"【{card.cardName}】无法攻击这个障碍物");
                else
                {
                    ShowMessage("请点击高亮的敌方单位");
                    return;
                }
                if (done) ShowStatus("【" + card.cardName + "】已释放");
                break;
            }
            case AttackTargetRule.多个单位:
            {
                Unit target = cell.occupyUnit;
                if (!(target is EnemyUnit) || target.IsDead ||
                    !card.CanSelectAttackCell(FindObjectOfType<PlayerUnit>(), cell.coord))
                {
                    ShowMessage("请点击高亮的敌方单位");
                    return;
                }

                int maxTargets = Mathf.Max(1, card.maxSelectedTargets);
                if (pendingAimCells.Contains(cell.coord) || pendingAimCells.Count >= maxTargets)
                {
                    // 再点已选目标或选满 → 确认释放
                    done = RunCardAction(() => cardSystem.UseSlotCardOnCells(aimSlot, pendingAimCells),
                        $"【{card.cardName}】释放失败");
                    if (done)
                    {
                        pendingAimCells.Clear();
                        ShowStatus("【" + card.cardName + "】已释放");
                    }
                }
                else
                {
                    pendingAimCells.Add(cell.coord);
                    ShowStatus("已选 " + pendingAimCells.Count + "/" + maxTargets +
                               " 个目标；继续点选，再点已选目标释放");
                    RefreshSkillHighlights();
                    return;
                }
                break;
            }
            case AttackTargetRule.相邻地块定向扇形:
            case AttackTargetRule.地块:
            case AttackTargetRule.穿透单位:
            case AttackTargetRule.连锁单位:
            {
                done = RunCardAction(() => cardSystem.UseSlotCardOnCells(aimSlot, new[] { cell.coord }),
                    $"【{card.cardName}】无法对这一格释放");
                if (done) ShowStatus("【" + card.cardName + "】已释放");
                break;
            }
            default:   // 自身范围 / 全场 / 全场随机 / 固定地图图案
            {
                done = RunCardAction(() => cardSystem.UseSlotCardOnCells(aimSlot, new HexCoord[0]),
                    $"【{card.cardName}】释放失败");
                if (done) ShowStatus("【" + card.cardName + "】已释放");
                break;
            }
        }

        if (done) ExitMode();
    }

    /// <summary>刷新三张插槽的文字（卡名 + 剩余次数）</summary>
    private void RefreshSlotTexts()
    {
        if (cardSystem == null) cardSystem = FindCardSystem();
        for (int i = 0; i < slotTexts.Length; i++)
        {
            CardSlot slot = (cardSystem != null && i + 2 < cardSystem.Slots.Count) ? cardSystem.Slots[i + 2] : null;
            bool hasCard = slot != null && !slot.IsEmpty;

            // 卡面随技能类型切换：攻击卡 → 攻击牌，防御卡 → 防御牌（空槽保持空技能卡面）
            if (slotFaceButtons[i] != null && slotFaceButtons[i].image != null)
            {
                Sprite wantFace = FaceForCard(hasCard ? slot.card : null);
                if (slotFaceButtons[i].image.sprite != wantFace)
                {
                    slotFaceButtons[i].image.sprite = wantFace;
                    slotFaceButtons[i].image.color = Color.white;
                }
            }

            // 名称：写进卡面空白区（黑字）；空槽同样黑字
            if (slotNameTexts[i] != null)
            {
                string wantName = hasCard ? slot.card.cardName : "空";
                if (slotNameTexts[i].text != wantName) slotNameTexts[i].text = wantName;
                if (slotNameTexts[i].color != Color.black) slotNameTexts[i].color = Color.black;
            }

            // 剩余次数：只有限次卡才显示
            if (slotUseTexts[i] != null)
            {
                string wantUse = (hasCard && slot.card.maxUses > 0) ? "×" + slot.remainingUses : "";
                if (slotUseTexts[i].text != wantUse) slotUseTexts[i].text = wantUse;
            }

            // 底部插槽横条只留边框：名称已上移到卡面，避免重复且白字压在浅灰卡面上看不清
            if (slotTexts[i] != null && slotTexts[i].text != "")
                slotTexts[i].text = "";
        }
    }

    /// <summary>是否处于技能瞄准中（位移/攻击按钮进入的模式）；DebugDriver 据此让出鼠标</summary>
    public bool IsAiming => mode != SelectMode.无;

    private static bool IsPlayerPhase()
    {
        if (TurnManager.Instance != null)
            return TurnManager.Instance.Phase == TurnPhase.玩家行动;
        return GameManager.Instance != null && GameManager.Instance.IsInBattle && GameManager.Instance.isPlayerTurn;
    }

    // ============================================================
    //                点击格子执行 / 悬停小怪半透明
    // ============================================================

    private void Update()
    {
        // ESC 菜单打开时整体让位：暂停只是停时间、Update 照跑，
        // 不让位的话菜单期间里的鼠标点击会穿透到战斗操作
        if (EscMenuUI.IsOpen) return;

        GuardTooltips();     // 先兜底关掉该关的悬停浮层，再处理本帧输入
        RefreshHud();
        RefreshBuffBar();
        RefreshRelicIconsIfChanged();
        RefreshEnemyPlannedLabels();

        // 灌注确认 / 提示弹窗打开时：右键关闭（模态，其它输入不处理）
        // —— Esc 让给游戏内 ESC 菜单了，这里只留右键
        if (confirmPanel != null && confirmPanel.activeSelf)
        {
            if (Input.GetMouseButtonDown(1))
                CloseInfuseConfirm();
            return;
        }
        if (messagePanel != null && messagePanel.activeSelf)
        {
            if (Input.GetMouseButtonDown(1))
                CloseMessagePanel();
            return;
        }

        // 牌堆选择层打开时：右键、或点面板外的空白（非卡牌界面）取消（此时 mode 仍是"无"）
        if (mode == SelectMode.无 && deckPanel != null && deckPanel.activeSelf)
        {
            if (Input.GetMouseButtonDown(1)) { CloseDeckPanel(); return; }
            if (Input.GetMouseButtonDown(0) && !IsPointerOnSelectableUnder(deckPanel)) { CloseDeckPanel(); return; }
            return;
        }

        if (mode == SelectMode.无) return;

        // 右键取消瞄准（Esc 让给游戏内 ESC 菜单了）
        if (Input.GetMouseButtonDown(1))
        {
            ExitMode();
            return;
        }

        if (!Input.GetMouseButtonDown(0)) return;

        try
        {
            HandleAimClick();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[BattleHUD] 点击处理异常：" + e);
        }
    }

    /// <summary>瞄准状态下处理一次左键点击（拾取格子 → 移动 / 攻击）</summary>
    /// <summary>
    /// 指针是否落在"可交互 UI"上（按钮等 Selectable）。
    /// 不能用 EventSystem.IsPointerOverGameObject()：它只认 Graphic.raycastTarget，
    /// 任何铺满屏幕的装饰文字/半透明底图都会被算作 UI，从而吞掉世界里所有格子点击。
    /// 这里精确到 Selectable——只有真正点在按钮上，才把这次点击让给 UI。
    /// </summary>
    private static bool IsPointerOnInteractiveUI()
    {
        if (EventSystem.current == null) return false;

        PointerEventData ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);

        for (int i = 0; i < results.Count; i++)
        {
            GameObject go = results[i].gameObject;
            if (go != null && go.GetComponentInParent<Selectable>() != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 指针是否落在 root 这一组 UI 自己的按钮上。
    /// 用于"点这组 UI 以外的空白处即取消"：RaycastAll 会把被遮罩盖住的
    /// 底层按钮也返回来，所以这里限定必须是 root 的子物体，避免误判。
    /// </summary>
    private static bool IsPointerOnSelectableUnder(GameObject root)
    {
        if (root == null || EventSystem.current == null) return false;

        PointerEventData ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);

        for (int i = 0; i < results.Count; i++)
        {
            GameObject go = results[i].gameObject;
            if (go == null) continue;
            Selectable s = go.GetComponentInParent<Selectable>();
            if (s != null && s.transform.IsChildOf(root.transform)) return true;
        }
        return false;
    }

    private void HandleAimClick()
    {
        if (IsPointerOnInteractiveUI())
        {
            ShowStatus("点击落在按钮上，已交给 UI 处理");
            return;
        }

        HexCell cell = PickCell();
        if (cell == null)
        {
            // 兜底：坐标换算没命中时再试一次物理拾取（两者用的是不同坐标系路径）
            Camera cam2 = Camera.main;
            if (cam2 != null)
            {
                Vector3 w = cam2.ScreenToWorldPoint(Input.mousePosition);
                Collider2D hit = Physics2D.OverlapPoint(new Vector2(w.x, w.y));
                if (hit != null) cell = hit.GetComponentInParent<HexCell>();
            }
        }
        if (cell == null)
        {
            // 点击非卡牌界面（既不在按钮上、也没命中任何格子）：取消瞄准
            ExitMode();
            return;
        }

        lastClickFrame = Time.frameCount;
        ExecuteCell(cell);
    }

    /// <summary>对某格执行当前瞄准动作（推 / 跳 / 槽位技能）。两条输入通道共用。</summary>
    private void ExecuteCell(HexCell cell)
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || player.IsDead) { ExitMode(); return; }

        if (mode == SelectMode.推)
        {
            Unit target = cell.occupyUnit;
            if (!(target is EnemyUnit) || target.IsDead)
            {
                ShowMessage("推 只能对敌方单位使用：这一格没有敌人");
                return;
            }
            PlayerCardSystem cards = FindCardSystem();
            if (cards == null || pushCard == null)
            {
                ShowStatus("推 未配置（缺 PlayerCardSystem 或卡牌引用）");
                return;
            }
            // 耐力不足：弹窗（推的消耗）
            if (player.CurrentStamina < StaminaCostOf(pushCard, player))
            {
                ExitMode();
                ShowMessage("耐力不足，无法释放技能");
                return;
            }
            if (RunCardAction(() => cards.UseBasicAttack(pushCard, target), "推 释放失败"))
            {
                ShowStatus($"推：对 {target.data.unitName} 造成 {pushCard.damage} 伤害并击退");
                ExitMode();
            }
        }
        else if (mode == SelectMode.跳)
        {
            // 只允许点绿圈内的格子（CanStopOn = 可通行 + 无单位 + 无阻挡障碍）
            if (!cell.CanStopOn(player))
            {
                ShowMessage("跳 的落点必须是高亮的空格（不能有单位或阻挡物）");
                return;
            }
            PlayerCardSystem cards = FindCardSystem();
            if (cards == null || jumpCard == null)
            {
                ShowStatus("跳 未配置（缺 PlayerCardSystem 或卡牌引用）");
                return;
            }
            // 耐力不足：弹窗（跳的消耗）
            if (player.CurrentStamina < StaminaCostOf(jumpCard, player))
            {
                ExitMode();
                ShowMessage("耐力不足，无法释放技能");
                return;
            }
            if (RunCardAction(() => cards.UseBasicDefense(jumpCard, new[] { cell.coord }), "跳 释放失败"))
            {
                ShowStatus($"跳：移动到({cell.coord.q},{cell.coord.r})");
                ExitMode();
            }
        }
        else if (mode == SelectMode.技能)
        {
            ExecuteSkillCell(cell);
        }
    }

    /// <summary>碰撞体点击通道：HexCell.OnMouseDown 通过事件总线转发过来（与坐标拾取互为兜底）</summary>
    private void OnCellClickedEvent(HexCell cell)
    {
        if (cell == null) return;
        if (lastClickFrame == Time.frameCount) return;   // 本帧已被 Update 的点击通道处理过
        lastClickFrame = Time.frameCount;
        if (mode == SelectMode.无) return;               // 常态不响应（需求：必须选技能才能行动）
        if (IsPointerOnInteractiveUI()) return;
        ExecuteCell(cell);
    }

    /// <summary>操作结果反馈：只写 Console，界面上不再弹提示条</summary>
    private void ShowStatus(string text)
    {
        Debug.Log("[BattleHUD] " + text);
    }

    /// <summary>
    /// 鼠标位置 → 格子。用 HexGrid.WorldToHex 坐标换算（与调试驱动同一条已验证路径），
    /// 不走 Physics2D 射线——避免碰撞体缺失/物理层配置导致点击静默失败。
    /// </summary>
    private static HexCell PickCell()
    {
        if (HexGrid.Instance == null) return null;
        Camera cam = Camera.main;
        if (cam == null) return null;
        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
        return HexGrid.Instance.GetCell(HexGrid.Instance.WorldToHex(world));
    }

    /// <summary>小怪半透明化（例图：选择技能时隐藏多余信息）</summary>
    private void DimEnemies(bool dim)
    {
        // 用 SpriteRenderer.color（顶点色）而不是 material.color：
        // 小怪材质被 HitFlash 换成 DamageFlash 着色器后没有 _Color 属性，
        // 读 material.color 会报错；顶点色与材质无关，也不会产生材质实例
        if (dim)
        {
            foreach (EnemyUnit enemy in FindObjectsOfType<EnemyUnit>())
            {
                foreach (SpriteRenderer r in enemy.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!dimmedSprites.ContainsKey(r))
                    {
                        dimmedSprites[r] = r.color;
                        Color c = r.color;
                        c.a *= 0.5f;
                        r.color = c;
                    }
                }
            }
        }
        else
        {
            foreach (KeyValuePair<SpriteRenderer, Color> kv in dimmedSprites)
            {
                if (kv.Key != null)
                    kv.Key.color = kv.Value;
            }
            dimmedSprites.Clear();
        }
    }

    // ============================================================
    //   敌人悬停信息：棋子下方红色血条 + 上方"本回合要放的技能"
    // ============================================================

    /// <summary>
    /// 每帧刷新：只给"鼠标悬停到的那只敌人"显示
    /// ① 脚下的红色血条（当前/上限）② 头顶的技能标签（本回合 PlannedSkills）。
    /// 瞄准、抽卡、弹窗、敌方行动、指针压住 HUD 按钮时全部隐藏。
    /// 标签与血条都 raycastTarget=false，不会拦格子点击。
    /// </summary>
    private void RefreshEnemyPlannedLabels()
    {
        if (plannedLabelLayer == null) return;

        bool visible = showEnemyPlannedSkills && IsPlayerPhase() && mode == SelectMode.无
                       && !BattleEndPanel.IsOpen   // 战斗结束奖励界面打开时：悬停信息全部隐藏
                       && (deckPanel == null || !deckPanel.activeSelf)
                       && (cardSystem == null || !cardSystem.IsChoosing)
                       && (confirmPanel == null || !confirmPanel.activeSelf)
                       && (messagePanel == null || !messagePanel.activeSelf)
                       && !IsPointerOnInteractiveUI();

        if (!visible) { HideEnemyHoverInfo(); return; }

        SyncPlannedLabels();

        Camera cam = Camera.main;
        RectTransform canvasRt = canvas != null ? canvas.transform as RectTransform : null;
        if (cam == null || canvasRt == null) { HideEnemyHoverInfo(); return; }

        EnemyUnit hovered = FindHoveredEnemy(cam);
        if (hovered == null) { HideEnemyHoverInfo(); return; }

        // 非悬停目标的标签先关掉
        PlannedSkillLabel hoveredLabel = null;
        foreach (PlannedSkillLabel label in plannedLabels)
        {
            if (label == null || label.Root == null) continue;
            if (label.Enemy == hovered) { hoveredLabel = label; continue; }
            if (label.Root.gameObject.activeSelf) label.Root.gameObject.SetActive(false);
        }

        // 悬停的这只重新量一次包围盒（可能刚被击退/翻面，缓存值会滞后）
        Bounds bounds = ComputeSpriteBounds(hovered);
        if (hoveredLabel != null) hoveredLabel.SpriteBounds = bounds;

        float halfW = canvasRt.rect.width * 0.5f;
        float halfH = canvasRt.rect.height * 0.5f;

        // ---------- 头顶：本回合预告技能 ----------
        string text = BuildPlannedSkillText(hovered);
        if (hoveredLabel != null && hoveredLabel.Root != null)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (hoveredLabel.Root.gameObject.activeSelf) hoveredLabel.Root.gameObject.SetActive(false);
            }
            else
            {
                if (!hoveredLabel.Root.gameObject.activeSelf) hoveredLabel.Root.gameObject.SetActive(true);

                if (hoveredLabel.ShownText != text)
                {
                    hoveredLabel.ShownText = text;
                    hoveredLabel.Content.text = text;
                    float w = Mathf.Clamp(hoveredLabel.Content.preferredWidth + 18f * hoverInfoScale,
                                          54f * hoverInfoScale, 320f * hoverInfoScale);
                    // 带 buff 行会折成两行：高度按实际换行结果撑开
                    float h = Mathf.Max(26f * hoverInfoScale,
                                        hoveredLabel.Content.preferredHeight + 10f);
                    hoveredLabel.Root.sizeDelta = new Vector2(w, h);
                }

                Vector3 headWorld = hovered.transform.position + HeadOffsetFrom(bounds, hovered.transform.position);
                Vector3 headScreen = cam.WorldToScreenPoint(headWorld);
                if (headScreen.z > 0f)
                {
                    Vector2 local;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, headScreen, null, out local);
                    Vector2 size = hoveredLabel.Root.sizeDelta;
                    local.x = Mathf.Clamp(local.x, -halfW + size.x * 0.5f + 8f, halfW - size.x * 0.5f - 8f);
                    local.y = Mathf.Clamp(local.y, -halfH + 40f, halfH - 158f);   // 别钻进顶部状态栏
                    hoveredLabel.Root.anchoredPosition = local;
                }
            }
        }

        // ---------- 脚下：红色血条 ----------
        EnsureEnemyHpBar();
        if (enemyHpBar != null)
        {
            Vector3 feetWorld = new Vector3(bounds.center.x, bounds.min.y - 0.06f, bounds.center.z);
            Vector3 feetScreen = cam.WorldToScreenPoint(feetWorld);
            bool onScreen = feetScreen.z > 0f;
            if (enemyHpBar.gameObject.activeSelf != onScreen)
                enemyHpBar.gameObject.SetActive(onScreen);

            if (onScreen)
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, feetScreen, null, out local);
                enemyHpBar.anchoredPosition = local;

                int max = hovered.GetMaxHealth();
                float ratio = max > 0 ? Mathf.Clamp01((float)hovered.GetCurrentHealth() / max) : 0f;
                if (enemyHpBarFill != null)
                    enemyHpBarFill.rectTransform.sizeDelta =
                        new Vector2(hpBarInnerWidth * hoverInfoScale * ratio, 8f * hoverInfoScale);
                if (enemyHpBarText != null)
                    enemyHpBarText.text = hovered.GetCurrentHealth() + "/" + max;
            }
        }
    }

    /// <summary>关掉所有头顶标签与血条（非悬停 / 不可显示状态）</summary>
    private void HideEnemyHoverInfo()
    {
        foreach (PlannedSkillLabel label in plannedLabels)
            if (label != null && label.Root != null && label.Root.gameObject.activeSelf)
                label.Root.gameObject.SetActive(false);
        if (enemyHpBar != null && enemyHpBar.gameObject.activeSelf)
            enemyHpBar.gameObject.SetActive(false);
    }

    /// <summary>鼠标是否压在敌人立绘上：用精灵包围盒投影成屏幕矩形判断（不依赖碰撞体）</summary>
    private EnemyUnit FindHoveredEnemy(Camera cam)
    {
        Vector3 mouse = Input.mousePosition;
        EnemyUnit best = null;
        float bestDistance = float.MaxValue;

        foreach (PlannedSkillLabel label in plannedLabels)
        {
            if (label == null || label.Enemy == null || label.Enemy.IsDead) continue;

            Vector3 a = cam.WorldToScreenPoint(label.SpriteBounds.min);
            Vector3 b = cam.WorldToScreenPoint(label.SpriteBounds.max);
            Rect rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                                        Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            if (!rect.Contains(mouse)) continue;

            // 多只立绘重叠时取屏幕中心离鼠标最近的那只
            float d = Mathf.Abs(mouse.x - rect.center.x) + Mathf.Abs(mouse.y - rect.center.y);
            if (d < bestDistance) { bestDistance = d; best = label.Enemy; }
        }
        return best;
    }

    /// <summary>
    /// 头顶悬停标签内容：第一行 = 本回合预告的技能名（间隔点串起），
    /// 第二行 = 身上的 buff（名称×层数），有 buff 才有第二行。
    /// </summary>
    private static string BuildPlannedSkillText(EnemyUnit enemy)
    {
        if (enemy == null || enemy.IsDead) return "";
        string text = "";
        foreach (CardData card in enemy.PlannedSkills)
        {
            if (card == null) continue;
            if (text.Length > 0) text += " · ";
            text += card.cardName;
        }

        string buffs = "";
        List<BuffInstance> instances = enemy.GetActiveBuffs();
        if (instances != null)
        {
            foreach (BuffInstance b in instances)
            {
                if (b == null || b.data == null || string.IsNullOrEmpty(b.data.buffName)) continue;
                if (buffs.Length > 0) buffs += " · ";
                buffs += b.data.buffName;
                if (b.currentStacks > 1) buffs += "×" + b.currentStacks;
            }
        }
        if (buffs.Length > 0)
            text = string.IsNullOrEmpty(text) ? "状态：" + buffs : text + "\n状态：" + buffs;
        return text;
    }

    /// <summary>每 20 帧同步一次敌人列表（新建 / 回收标签、重量包围盒），避免每帧 FindObjectsOfType</summary>
    private void SyncPlannedLabels()
    {
        if (Time.frameCount - plannedLabelSyncFrame < 20) return;
        plannedLabelSyncFrame = Time.frameCount;

        for (int i = plannedLabels.Count - 1; i >= 0; i--)
        {
            PlannedSkillLabel label = plannedLabels[i];
            if (label == null || label.Enemy == null || label.Enemy.IsDead)
            {
                if (label != null && label.Root != null) Destroy(label.Root.gameObject);
                plannedLabels.RemoveAt(i);
            }
        }

        foreach (EnemyUnit enemy in FindObjectsOfType<EnemyUnit>())
        {
            if (enemy == null || enemy.IsDead) continue;

            bool exists = false;
            foreach (PlannedSkillLabel label in plannedLabels)
            {
                if (label.Enemy != enemy) continue;
                exists = true;
                label.SpriteBounds = ComputeSpriteBounds(enemy);   // 换立绘/翻转后重新量
                break;
            }
            if (!exists) plannedLabels.Add(CreatePlannedLabel(enemy));
        }
    }

    private PlannedSkillLabel CreatePlannedLabel(EnemyUnit enemy)
    {
        GameObject root = new GameObject("PlannedSkill_" + enemy.name, typeof(RectTransform));
        root.transform.SetParent(plannedLabelLayer.transform, false);
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(80f * hoverInfoScale, 26f * hoverInfoScale);

        // 半透明深色底条：白字在浅色地砖上也读得清；不参与射线检测
        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.13f, 0.82f);
        bg.raycastTarget = false;

        Text t = MakeText(root.transform, "", Mathf.RoundToInt(17f * hoverInfoScale), TextAnchor.MiddleCenter,
            new Color(0.93f, 0.97f, 1f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(0.5f, 0.5f), Vector2.zero);
        Stretch(t.rectTransform);

        root.SetActive(false);   // 只有悬停时才显示

        return new PlannedSkillLabel
        {
            Enemy = enemy,
            Root = rt,
            Content = t,
            SpriteBounds = ComputeSpriteBounds(enemy)
        };
    }

    /// <summary>脚下血条：深色底 + 从左往右收的红色填充（只建一次，之后复用）</summary>
    private void EnsureEnemyHpBar()
    {
        if (enemyHpBar != null) return;

        GameObject go = new GameObject("EnemyHpBar", typeof(RectTransform));
        go.transform.SetParent(plannedLabelLayer.transform, false);
        enemyHpBar = go.GetComponent<RectTransform>();
        enemyHpBar.anchorMin = new Vector2(0.5f, 0.5f);
        enemyHpBar.anchorMax = new Vector2(0.5f, 0.5f);
        enemyHpBar.pivot = new Vector2(0.5f, 0.5f);
        enemyHpBar.sizeDelta = new Vector2(hpBarWidth * hoverInfoScale, 12f * hoverInfoScale);

        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.05f, 0.06f, 0.88f);
        bg.raycastTarget = false;

        GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(go.transform, false);
        enemyHpBarFill = fillGo.AddComponent<Image>();
        enemyHpBarFill.color = new Color(0.86f, 0.18f, 0.18f);
        enemyHpBarFill.raycastTarget = false;

        RectTransform fr = enemyHpBarFill.rectTransform;
        fr.anchorMin = new Vector2(0f, 0.5f);
        fr.anchorMax = new Vector2(0f, 0.5f);
        fr.pivot = new Vector2(0f, 0.5f);
        fr.anchoredPosition = new Vector2(2f * hoverInfoScale, 0f);
        fr.sizeDelta = new Vector2(hpBarInnerWidth * hoverInfoScale, 8f * hoverInfoScale);

        // 血条数字（当前/上限）：白字黑描边，叠在血条正中，方便直接读数值
        GameObject txtGo = new GameObject("HpText", typeof(RectTransform));
        txtGo.transform.SetParent(go.transform, false);
        enemyHpBarText = txtGo.AddComponent<Text>();
        enemyHpBarText.font = uiFont;
        enemyHpBarText.fontSize = Mathf.RoundToInt(11f * hoverInfoScale);
        enemyHpBarText.alignment = TextAnchor.MiddleCenter;
        enemyHpBarText.color = Color.white;
        enemyHpBarText.horizontalOverflow = HorizontalWrapMode.Overflow;
        enemyHpBarText.verticalOverflow = VerticalWrapMode.Overflow;
        enemyHpBarText.supportRichText = false;
        enemyHpBarText.raycastTarget = false;
        Outline ol = txtGo.AddComponent<Outline>();
        ol.effectColor = new Color(0f, 0f, 0f, 0.9f);
        Stretch(enemyHpBarText.rectTransform);

        go.SetActive(false);
    }

    /// <summary>合并敌人身上全部 SpriteRenderer 的世界包围盒（没有精灵时给个占位盒）</summary>
    private static Bounds ComputeSpriteBounds(EnemyUnit enemy)
    {
        bool has = false;
        Bounds bounds = default;
        foreach (SpriteRenderer r in enemy.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r == null || r.sprite == null) continue;
            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        if (!has)
            bounds = new Bounds(enemy.transform.position + Vector3.up * 0.5f, new Vector3(1f, 1f, 0f));
        return bounds;
    }

    /// <summary>包围盒顶端（含立绘朝向偏移）相对单位 transform 的偏移，再往上留 0.12 世界单位</summary>
    private static Vector3 HeadOffsetFrom(Bounds bounds, Vector3 origin)
    {
        Vector3 top = bounds.center;
        top.y = bounds.max.y + 0.12f;
        top.z = origin.z;
        Vector3 offset = top - origin;
        offset.z = 0f;
        return offset;
    }

    // ============================================================
    //             左上角数值刷新（血量 / 耐力 / 思绪）
    // ============================================================

    private void RefreshHud()
    {
        // 下一回合 / 取消
        bool busy = mode != SelectMode.无
                    || (deckPanel != null && deckPanel.activeSelf)
                    || (cardSystem != null && cardSystem.IsChoosing);
        if (endTurnLabel != null)
        {
            string want = busy ? "取消" : "下一回合";
            if (endTurnLabel.text != want) endTurnLabel.text = want;
        }

        // 选技能/选牌期间：隐藏左下角按钮与插槽组、左侧敌方技能栏
        // （保留左上状态和右下取消），释放/取消后恢复
        // 注意：SetActive(false) 不会补发 PointerExit，悬停浮层必须在这里显式关掉，
        // 否则进入瞄准 / 打牌堆时会一直贴在屏幕上（必须重新悬停一次再移开才能消失）
        if (skillGroup != null && skillGroup.activeSelf == busy)
        {
            if (busy) HideAllTooltips();
            skillGroup.SetActive(!busy);
        }
        if (enemySkillGroup != null && enemySkillGroup.activeSelf == busy)
        {
            if (busy) HideAllTooltips();
            enemySkillGroup.SetActive(!busy);
        }

        // 插槽文字（卡名/次数）随槽位变化刷新；每帧兜底一次，事件丢失也能补上
        RefreshSlotTexts();

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null) return;

        string hp = player.GetCurrentHealth() + "/" + player.GetMaxHealth();
        if (hpText != null && hpText.text != hp) hpText.text = hp;

        string st = player.CurrentStamina + "/" + player.maxStamina;
        if (staminaText != null && staminaText.text != st) staminaText.text = st;

        string th = player.CurrentThought + "/" + player.maxThought;
        if (thoughtText != null && thoughtText.text != th) thoughtText.text = th;
    }
}
