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
//  · 左下角：攻击 / 位移 / 技能 / 休息 四个灰色按钮
//  · 右下角：下一回合按钮（进入选择状态时变成"取消"）
//  · 技能：点小人头像处按钮 → 弹牌堆选择（攻击/防御）→ 3 张待选卡 → 点击灌注
//  · 攻击 / 位移：做成两个常驻可选技能——进入瞄准模式后，可交互格显示
//    攻击选取格（红圈）/ 位移选取格（绿圈）贴图，点击格子执行
//  · 选择技能 / 卡牌期间：所有小怪图像半透明化（例图要求），"下一回合"变"取消"
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
        位移,    // 移动技能瞄准中：绿圈高亮可到达格，点击落点
        攻击     // 普通攻击瞄准中：红圈高亮相邻格，点击敌人
    }

    private SelectMode mode = SelectMode.无;

    // -------------------- UI 引用 --------------------

    private Canvas canvas;
    private Text hpText;             // 血量槽圆心的 "当前/上限" 数字
    private Text staminaText;        // 耐力槽上的数字
    private Text thoughtText;        // 思绪槽上的数字
    private Text endTurnLabel;       // 下一回合 / 取消
    private Button endTurnButton;
    private GameObject deckPanel;    // 牌堆选择弹窗
    private GameObject cardPanel;    // 3 张待选卡弹窗
    private GameObject skillGroup;   // 左下角四个技能按钮的父物体（选技能时整组隐藏）

    [Header("状态栏素材（引用已在 Battle.unity 里配好）")]
    public Sprite statusBarSprite;    // 深蓝色长条_2：顶部状态栏底板
    public Sprite hpSlotSprite;       // 血量槽_1：左上角红色圆
    public Sprite staminaSlotSprite;  // 耐力槽_2：长条下方左侧浅灰条
    public Sprite thoughtSlotSprite;  // 思绪槽_1：长条下方右侧浅蓝条

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
        // 碰撞体点击通道（HexCell.OnMouseDown 广播），与坐标拾取互为兜底
        EventManager.AddListener<HexCell>("CellClicked", OnCellClickedEvent);

        uiFont = LoadBuiltinFont();
        BuildUI();
    }

    private void OnDestroy()
    {
        EventManager.RemoveListener("CardOfferClosed", OnCardOfferClosed);
        EventManager.RemoveListener<HexCell>("CellClicked", OnCellClickedEvent);
    }

    /// <summary>选牌结束（成功/放弃）：关掉牌堆与待选卡弹窗，恢复小怪透明度</summary>
    private void OnCardOfferClosed()
    {
        if (cardPanel != null) cardPanel.SetActive(false);
        if (deckPanel != null) deckPanel.SetActive(false);
        if (mode == SelectMode.无)
            DimEnemies(false);
    }

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
#endif
        if (statusBarSprite == null || hpSlotSprite == null ||
            staminaSlotSprite == null || thoughtSlotSprite == null)
            Debug.LogWarning("[BattleHUD] 状态栏有素材未加载（打包版需保证场景引用有效），缺失项将以纯色块兜底显示");

        canvas = new GameObject("BattleHUDCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // 盖在所有世界物体上
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        // ============================================================
        // 顶部状态栏（自底向上：深蓝长条 → 耐力/思绪槽 → 血量槽）
        // 素材若丢失引用则退化为半透明纯色块，布局不受影响
        // ============================================================

        // ---- 最底层：深蓝色长条_2 铺满屏幕顶部（Canvas 在两张战斗背景之上） ----
        MakeImage(canvas.transform, "StatusBar_Bar", statusBarSprite,
            statusBarSprite == null ? new Color(0.16f, 0.27f, 0.44f, 0.9f) : Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1920f, 150f));

        // ---- 长条下方：耐力槽（左） / 思绪槽（右），关于画面中轴 x=960 对称 ----
        // pivot=(0.5,1)（顶边锚点）；y=-165 = 长条底边(-150) 再下移 15px
        // 尺寸：横向长度 448×1.2≈538，竖向宽度 110×0.7=77（正好贴合素材 400×69 原始比例）
        Image staminaSlot = MakeImage(canvas.transform, "StatusBar_Stamina", staminaSlotSprite,
            staminaSlotSprite == null ? new Color(0.85f, 0.85f, 0.87f, 0.9f) : Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-340f, -165f), new Vector2(538f, 77f));
        staminaText = MakeText(staminaSlot.transform, "20/20", 30, TextAnchor.MiddleCenter,
            new Color(0.15f, 0.15f, 0.18f));
        Stretch(staminaText.rectTransform);

        Image thoughtSlot = MakeImage(canvas.transform, "StatusBar_Thought", thoughtSlotSprite,
            thoughtSlotSprite == null ? new Color(0.65f, 0.8f, 0.97f, 0.9f) : Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(340f, -165f), new Vector2(538f, 77f));
        thoughtText = MakeText(thoughtSlot.transform, "20/20", 30, TextAnchor.MiddleCenter,
            new Color(0.15f, 0.15f, 0.18f));
        Stretch(thoughtText.rectTransform);

        // ---- 左上角：血量槽红圆，压在深蓝长条之上，数字写圆心 ----
        Image hpSlot = MakeImage(canvas.transform, "StatusBar_HP", hpSlotSprite,
            hpSlotSprite == null ? new Color(0.89f, 0.23f, 0.23f, 0.95f) : Color.white,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(132f, 132f));
        hpText = MakeText(hpSlot.transform, "12/12", 30, TextAnchor.MiddleCenter, Color.white);
        Stretch(hpText.rectTransform);

        // ---- 左下角：攻击 / 位移 / 技能 / 休息（选技能时整组隐藏，只留状态和取消） ----
        // 注意：必须显式带 RectTransform——纯 Transform 的物体 GetComponent<RectTransform>() 为 null
        skillGroup = new GameObject("SkillButtons", typeof(RectTransform));
        skillGroup.transform.SetParent(canvas.transform, false);
        Stretch(skillGroup.GetComponent<RectTransform>());   // 铺满画布，子按钮的左下角锚点才正确

        string[] labels = { "攻击", "位移", "技能", "休息" };
        UnityAction[] actions =
        {
            () => EnterMode(SelectMode.攻击),
            () => EnterMode(SelectMode.位移),
            OpenDeckPanel,
            RestAction
        };
        for (int i = 0; i < labels.Length; i++)
        {
            MakeButton(skillGroup.transform, labels[i],
                new Vector2(30f + i * 150f, 28f), new Vector2(132f, 56f),
                new Color(0.32f, 0.32f, 0.34f), actions[i]);
        }

        // ---- 右下角：下一回合 / 取消 ----
        endTurnButton = MakeButton(canvas.transform, "下一回合",
            new Vector2(-226f, 28f), new Vector2(196f, 62f),
            new Color(0.25f, 0.5f, 0.28f), OnEndTurnClicked);
        endTurnLabel = endTurnButton.GetComponentInChildren<Text>();

        // ---- 弹窗（先建好，默认隐藏） ----
        BuildDeckPanel();
        BuildCardPanel();
    }

    /// <summary>休息：跳过行动并结束回合（例图按钮之一；具体加成待策划确认，先按"喘口气"处理）</summary>
    private void RestAction()
    {
        Debug.Log("（占位）休息：跳过本回合行动。具体效果待策划确认。");
        EndTurn();
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

    /// <summary>按屏幕角锚点生成一个灰底白字按钮（例图：左下四个灰块 / 右下下一回合）</summary>
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

    // ============================================================
    //            技能按钮 → 牌堆选择 → 3 张待选卡
    // ============================================================

    private void BuildDeckPanel()
    {
        deckPanel = new GameObject("DeckPanel");
        deckPanel.transform.SetParent(canvas.transform, false);
        Image bg = deckPanel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        Stretch(bg.rectTransform);

        MakeText(deckPanel.transform, "选择牌堆（消耗 5 点思绪）", 30, TextAnchor.MiddleCenter, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 140f),
            new Vector2(0.5f, 0.5f), new Vector2(600f, 50f));

        Button atk = MakeButton(deckPanel.transform, "攻击牌库", new Vector2(-260f, 20f), new Vector2(220f, 70f),
            new Color(0.62f, 0.22f, 0.22f), () => DrawAndShowCards(CardPool.攻击));
        SetCenterAnchor(atk.GetComponent<RectTransform>());
        Button def = MakeButton(deckPanel.transform, "防御牌库", new Vector2(40f, 20f), new Vector2(220f, 70f),
            new Color(0.22f, 0.4f, 0.62f), () => DrawAndShowCards(CardPool.防御));
        SetCenterAnchor(def.GetComponent<RectTransform>());
        Button cancel = MakeButton(deckPanel.transform, "取消", new Vector2(-110f, -90f), new Vector2(220f, 56f),
            new Color(0.32f, 0.32f, 0.34f), CloseDeckPanel);
        SetCenterAnchor(cancel.GetComponent<RectTransform>());

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
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        Stretch(bg.rectTransform);

        Button cancel = MakeButton(cardPanel.transform, "放弃（开抽的 5 点思绪不退）", new Vector2(-200f, -60f),
            new Vector2(400f, 56f), new Color(0.32f, 0.32f, 0.34f), () => cardSystem?.AbandonInfuse());
        SetCenterAnchor(cancel.GetComponent<RectTransform>());

        cardPanel.SetActive(false);
    }

    private void OpenDeckPanel()
    {
        if (!IsPlayerPhase()) return;
        cardSystem = FindCardSystem();
        if (cardSystem == null) return;
        if (cardSystem.IsChoosing) { ShowCardChoices(); return; }

        CloseDeckPanel();
        deckPanel.SetActive(true);
        DimEnemies(true);
    }

    private void CloseDeckPanel()
    {
        if (deckPanel != null) deckPanel.SetActive(false);
        if (mode == SelectMode.无 && (cardPanel == null || !cardPanel.activeSelf))
            DimEnemies(false);
    }

    private void DrawAndShowCards(CardPool pool)
    {
        if (cardSystem == null) return;
        if (cardSystem.BeginDraw(pool))
            ShowCardChoices();
    }

    private void ShowCardChoices()
    {
        deckPanel?.SetActive(false);
        cardPanel.SetActive(true);
        DimEnemies(true);

        // 清掉上一轮的卡牌按钮（子物体：0=底图，1=放弃按钮，2 起是卡牌）
        for (int i = cardPanel.transform.childCount - 1; i >= 2; i--)
            Destroy(cardPanel.transform.GetChild(i).gameObject);

        IReadOnlyList<CardData> choices = cardSystem.CurrentChoices;
        float y = 120f;
        for (int i = 0; i < choices.Count; i++)
        {
            CardData card = choices[i];
            if (card == null) continue;
            string label = $"【{card.cardName}】 思绪{card.thoughtCost} 耐力{card.actionCost}";
            CardData captured = card;
            Button b = MakeButton(cardPanel.transform, label, new Vector2(-200f, y), new Vector2(400f, 80f),
                new Color(0.2f, 0.25f, 0.32f), () => TryInfuse(captured));
            SetCenterAnchor(b.GetComponent<RectTransform>());
            y -= 100f;
        }
    }

    private void TryInfuse(CardData card)
    {
        if (cardSystem == null) return;
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
    //              攻击 / 位移：可选技能 + 瞄准模式
    // ============================================================

    private void EnterMode(SelectMode target)
    {
        if (!IsPlayerPhase()) return;

        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || player.IsDead) return;

        // 再点同一个按钮 = 取消
        if (mode == target) { ExitMode(); return; }

        ExitMode();
        mode = target;

        if (target == SelectMode.位移)
        {
            // 可到达格 = 耐力允许走到的格子（含敏捷/残废修正）
            int range = player.GetMoveRangeForUI();
            foreach (HexCoord coord in HexPathfinding.GetReachableCells(player.GetCoord(), range))
            {
                HexCell cell = HexGrid.Instance != null ? HexGrid.Instance.GetCell(coord) : null;
                if (cell != null && cell.CanStopOn(player))
                    cell.SetMoveRangeHighlight(true);   // 绿圈
            }
        }
        else // 攻击：普通攻击 = 相邻 1 格
        {
            foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(player.GetCoord(), 1))
            {
                if (cell == null || cell.coord.Equals(player.GetCoord())) continue;
                Unit occupant = cell.occupyUnit;
                bool hasEnemy = occupant is EnemyUnit && !occupant.IsDead;
                if (hasEnemy)
                    cell.SetTargetHighlight(true);      // 有敌人：红圈加深
                else
                    cell.SetAttackRangeHighlight(true); // 红圈
            }
        }

        DimEnemies(true);   // 例图：选择技能时小怪半透明
    }

    /// <summary>退出瞄准模式：清高亮、恢复小怪透明度</summary>
    private void ExitMode()
    {
        if (mode != SelectMode.无)
        {
            mode = SelectMode.无;
            HexGrid.Instance?.ClearAllHighlights();
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
        RefreshHud();

        if (mode == SelectMode.无) return;

        // 右键 / Esc 取消瞄准
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
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
            Camera cam = Camera.main;
            Vector3 world = cam != null ? cam.ScreenToWorldPoint(Input.mousePosition) : Vector3.zero;
            ShowStatus($"未命中格子（世界坐标 {world.x:F2},{world.y:F2}）");
            return;
        }

        lastClickFrame = Time.frameCount;
        ExecuteCell(cell);
    }

    /// <summary>对某格执行当前瞄准动作（位移 / 攻击）。两条输入通道共用。</summary>
    private void ExecuteCell(HexCell cell)
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null || player.IsDead) { ExitMode(); return; }

        if (mode == SelectMode.位移)
        {
            // 只允许点绿圈内的格子（CanStopOn = 可通行 + 无单位 + 无阻挡障碍）
            if (!cell.CanStopOn(player))
            {
                ShowStatus($"格({cell.coord.q},{cell.coord.r})不可停留");
                return;
            }
            if (player.TryMoveTo(cell.coord))
            {
                ShowStatus($"移动到({cell.coord.q},{cell.coord.r})");
                ExitMode();
            }
            else ShowStatus("移动失败（详见 Console）");
        }
        else if (mode == SelectMode.攻击)
        {
            Unit target = cell.occupyUnit;
            if (!(target is EnemyUnit) || target.IsDead)
            {
                ShowStatus($"格({cell.coord.q},{cell.coord.r})没有敌人");
                return;
            }
            if (player.TryAttack(target))
            {
                ShowStatus("普通攻击命中");
                ExitMode();
            }
            else ShowStatus("攻击失败（详见 Console）");
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

        // 选技能/选牌期间：隐藏左下角技能按钮组（保留左上状态和右下取消），释放/取消后恢复
        if (skillGroup != null && skillGroup.activeSelf == busy)
            skillGroup.SetActive(!busy);

        // 操作状态提示条已移除（结果只写 Console）

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
