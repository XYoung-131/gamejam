using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

// ============================================================
//  开始界面 —— 程序启动后的第一个界面（Start.unity）
//
//  布局：
//  · 全屏背景：开始界面背景.jpg（1920×1080，铺满整屏）
//  · 左下角一列四个菜单按钮（从上到下）：
//      唤醒 → 回忆 → 梳理 → 沉睡
//      左缘距屏幕左边 225px，纵向间隔 10px，显示高 180px（宽按素材比例 ≈257，不拉伸）。
//      素材文字与序号：UI_1=回忆、UI_2=梳理、UI_3=唤醒、UI_4=沉睡，
//      所以显示顺序按素材下标排成 { 唤醒, 回忆, 梳理, 沉睡 } = { 2, 0, 1, 3 }。
//
//  按钮功能：
//  · 唤醒：开始新游戏。若已存在存档 → 先弹"已存在游戏存档，要重新开始吗？"，
//          选"是"删掉旧存档再重开，选"否"退回主界面。
//  · 回忆：继续旧存档。没有存档 → 提示"无正在进行的存档，请先唤醒"。
//  · 梳理：打开设置面板（SettingsPanelUI —— 游戏内 ESC 菜单的「设置」用的是同一份）：
//          音量大小 + 显示模式（窗口 / 无边框窗口 / 全屏）。
//          结果写进 PlayerPrefs，下次启动自动恢复。
//  · 沉睡：退出游戏（编辑器下结束播放，打包后调用 Application.Quit）。
//
//  全部 UI 运行时生成（Canvas / EventSystem 自举），不依赖预制体；
//  挂载位置：Start.unity 的 StartMenu 物体上。
// ============================================================
public class StartMenuUI : MonoBehaviour
{
    public static StartMenuUI Instance { get; private set; }

    [Header("素材（编辑器下引用丢失时按 Art 路径自动加载）")]
    public Sprite backgroundSprite;                 // 开始界面背景.jpg：全屏背景
    public Sprite[] menuSprites = new Sprite[4];    // 开始界面UI_1~4：回忆 / 梳理 / 唤醒 / 沉睡
    public Sprite menuHoverSprite;                  // 开始界面UI_5：回忆按钮悬停/按下时的底图
    public Sprite dialogButtonSprite;               // 淡蓝色按键_1：弹窗按钮底图
    public Sprite dialogButtonHoverSprite;          // 淡蓝色按键_2：弹窗按钮悬停底图

    [Header("目标场景")]
    [Tooltip("点击第一个按钮（开始游戏）后加载的场景名")]
    public string battleSceneName = "Battle";

    [Header("显示模式（设置面板里可切换）")]
    [Tooltip("切到“窗口模式”时使用的窗口分辨率（宽）")]
    public int windowedWidth = 1280;
    [Tooltip("切到“窗口模式”时使用的窗口分辨率（高）")]
    public int windowedHeight = 720;

    // ---- 布局参数 ----
    private const float leftGap = 225f;     // 按钮左缘距屏幕左边
    private const float bottomGap = 0f;     // 最下面一个按钮底边的 y
    private const float buttonH = 180f;     // 每个按钮的显示高
    private const float gap = 10f;          // 按钮纵向间隔
    private const float buttonAspect = 2244f / 1570f;   // 裁剪后素材宽高比（约 1.43）

    // ---- menuSprites 下标与按钮语义的对应（按素材上的文字） ----
    private const int SpriteRecall = 0;     // 开始界面UI_1 —— 回忆
    private const int SpriteTidy = 1;       // 开始界面UI_2 —— 梳理
    private const int SpriteWake = 2;       // 开始界面UI_3 —— 唤醒
    private const int SpriteSleep = 3;      // 开始界面UI_4 —— 沉睡

    /// <summary>显示顺序（从上到下）→ menuSprites 下标：唤醒、回忆、梳理、沉睡</summary>
    private static readonly int[] displayOrder = { SpriteWake, SpriteRecall, SpriteTidy, SpriteSleep };

    private Font uiFont;
    private RectTransform uiRoot;
    private GameObject modalRoot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null)
            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 把 Inspector 上配的窗口分辨率推给共用设置面板（避免两处各写一份配置）
        SettingsPanelUI.WindowedWidth = windowedWidth;
        SettingsPanelUI.WindowedHeight = windowedHeight;

        // 恢复上次调好的音量（没有记录时按满音量）
        AudioListener.volume = PlayerPrefs.GetFloat(SettingsPanelUI.VolumeKey, 1f);

        // 恢复上次选好的显示模式；首次运行没有记录时沿用当前实际状态，不强行改窗口
        SettingsPanelUI.ApplyDisplayMode(
            PlayerPrefs.GetInt(SettingsPanelUI.DisplayModeKey, SettingsPanelUI.CurrentDisplayModeIndex()));

        BuildUI();
    }

    private void BuildUI()
    {
        // 编辑器双保险：场景引用丢失时按 Art 规范路径直接加载
#if UNITY_EDITOR
        const string uiDir = "Assets/Game/Art/UI/";
        if (backgroundSprite == null)
            backgroundSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Game/Art/Backgrounds/开始界面背景.jpg");
        for (int i = 0; i < menuSprites.Length; i++)
            if (menuSprites[i] == null)
                menuSprites[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                    uiDir + "开始界面UI_" + (i + 1) + ".png");
        if (menuHoverSprite == null)
            menuHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "开始界面UI_5.png");
        if (dialogButtonSprite == null)
            dialogButtonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_1.png");
        if (dialogButtonHoverSprite == null)
            dialogButtonHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_2.png");
#endif

        // 正式包里 AssetDatabase 不可用：弹窗按钮素材从 Resources 配置资产兜底取
        if (dialogButtonSprite == null || dialogButtonHoverSprite == null)
        {
            BattleUIAssetCatalog catalog = Resources.Load<BattleUIAssetCatalog>("BattleUIAssetCatalog");
            if (catalog != null)
            {
                if (dialogButtonSprite == null) dialogButtonSprite = catalog.buttonSprite;
                if (dialogButtonHoverSprite == null) dialogButtonHoverSprite = catalog.buttonHoverSprite;
            }
        }

        // ---- Canvas：全屏覆盖，1920×1080 基准等比缩放（与战斗 HUD 同一套约定） ----
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        Canvas canvas = new GameObject("StartMenuCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;   // 背景铺满，宽度优先对齐
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        uiRoot = canvas.GetComponent<RectTransform>();

        // ---- 全屏背景：开始界面背景.jpg ----
        Image bg = new GameObject("Background").AddComponent<Image>();
        bg.transform.SetParent(canvas.transform, false);
        bg.sprite = backgroundSprite;
        bg.raycastTarget = false;   // 背景不挡按钮点击
        Stretch(bg.rectTransform);

        // ---- 左下角四个菜单按钮，从上到下：唤醒 → 回忆 → 梳理 → 沉睡 ----
        float buttonW = buttonH * buttonAspect;
        for (int row = 0; row < displayOrder.Length; row++)
        {
            int spriteIndex = displayOrder[row];
            Sprite sprite = spriteIndex < menuSprites.Length ? menuSprites[spriteIndex] : null;

            // row=0（唤醒）在最上面：底边 y = bottomGap + (3 - row) × (高 + 间隔)
            float y = bottomGap + (displayOrder.Length - 1 - row) * (buttonH + gap);

            GameObject go = new GameObject("Menu_" + spriteIndex, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : new Color(0.4f, 0.42f, 0.48f, 0.9f);
            img.preserveAspect = true;   // 立绘保持原始比例
            img.raycastTarget = true;    // 四个按钮都可点

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);   // 左下角锚点
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(leftGap, y);
            rt.sizeDelta = new Vector2(buttonW, buttonH);

            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            ApplyButtonFeedback(btn, spriteIndex);

            switch (spriteIndex)
            {
                case SpriteWake: btn.onClick.AddListener(OnWakeClicked); break;
                case SpriteRecall: btn.onClick.AddListener(OnRecallClicked); break;
                case SpriteTidy: btn.onClick.AddListener(OnTidyClicked); break;
                case SpriteSleep: btn.onClick.AddListener(OnQuitClicked); break;
            }
        }
    }

    /// <summary>
    /// 悬停 / 按下反馈：只有"回忆"有专属高亮素材（开始界面UI_5），
    /// 其余按钮没有对应素材，改用轻微颜色变化，保证点了有反馈。
    /// </summary>
    private void ApplyButtonFeedback(Button btn, int spriteIndex)
    {
        if (spriteIndex == SpriteRecall && menuHoverSprite != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            SpriteState st = btn.spriteState;
            st.highlightedSprite = menuHoverSprite;
            st.pressedSprite = menuHoverSprite;
            btn.spriteState = st;
            return;
        }

        btn.transition = Selectable.Transition.ColorTint;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(0.82f, 0.86f, 0.95f, 1f);
        cb.pressedColor = new Color(0.70f, 0.75f, 0.85f, 1f);
        cb.selectedColor = Color.white;
        cb.colorMultiplier = 1f;
        btn.colors = cb;
    }

    // ============================================================
    //                          按钮行为
    // ============================================================

    /// <summary>点击"唤醒"：有存档先问要不要重开，没有就直接开新档</summary>
    private void OnWakeClicked()
    {
        if (RunSaveSystem.HasSave)
        {
            ShowMessage("已存在游戏存档，要重新开始吗？", true, () =>
            {
                RunSaveSystem.Delete();   // 选"是"：删档重开
                CloseModal();
                StartNewGame();
            });
            return;
        }
        StartNewGame();
    }

    /// <summary>点击"回忆"：没有存档就提示，有存档就读档继续</summary>
    private void OnRecallClicked()
    {
        if (!RunSaveSystem.HasSave)
        {
            ShowMessage("无正在进行的存档，请先唤醒。", false, null);
            return;
        }
        if (RunFlowController.Instance == null)
        {
            Debug.LogError("[StartMenu] RunFlowController is missing.");
            return;
        }
        if (!RunFlowController.Instance.ContinueRun())
            ShowMessage("存档读取失败，请重新唤醒。", false, null);
    }

    /// <summary>点击"梳理"：打开设置面板（音量大小）</summary>
    private void OnTidyClicked()
    {
        ShowSettings();
    }

    /// <summary>点击"沉睡"：退出游戏。编辑器里结束播放模式，打包后真正退出程序。</summary>
    private void OnQuitClicked()
    {
        Debug.Log("[StartMenu] 退出游戏");
        PlayerPrefs.Save();   // 把音量等设置落盘
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;   // 编辑器下结束运行（Application.Quit 在编辑器里无效）
#else
        Application.Quit();
#endif
    }

    private void StartNewGame()
    {
        if (RunFlowController.Instance == null)
        {
            Debug.LogError("[StartMenu] RunFlowController is missing.");
            return;
        }
        RunFlowController.Instance.StartNewGame();
    }

    // ============================================================
    //                          弹窗 / 设置
    // ============================================================

    /// <summary>通用提示弹窗：confirm=true 显示"是/否"，否则只显示"确定"</summary>
    private void ShowMessage(string message, bool confirm, UnityAction onYes)
    {
        GameObject panel = BeginModal(new Vector2(900f, 460f));
        NewText(panel.transform, "Message", message, 34, new Vector2(0f, 50f), new Vector2(780f, 220f), TextAnchor.MiddleCenter);

        if (confirm)
        {
            NewDialogButton(panel.transform, "是", new Vector2(-190f, -140f),
                () => { if (onYes != null) onYes(); });
            NewDialogButton(panel.transform, "否", new Vector2(190f, -140f), CloseModal);
        }
        else
        {
            NewDialogButton(panel.transform, "确定", new Vector2(0f, -140f), CloseModal);
        }
    }

    /// <summary>设置面板：委托给共用的 SettingsPanelUI，保证与游戏内 ESC 菜单的「设置」完全一致</summary>
    private void ShowSettings()
    {
        CloseModal();
        modalRoot = SettingsPanelUI.Show(uiRoot, uiFont, dialogButtonSprite, dialogButtonHoverSprite, CloseModal);
    }

    /// <summary>建一个"遮罩 + 黑框面板"，返回面板本身（弹窗内容往里加）</summary>
    private GameObject BeginModal(Vector2 size)
    {
        CloseModal();

        GameObject root = new GameObject("StartMenuModal", typeof(RectTransform));
        root.transform.SetParent(uiRoot, false);
        Stretch(root.GetComponent<RectTransform>());
        modalRoot = root;

        // 半透明遮罩：挡住主界面按钮，避免弹窗期间误点
        Image dim = new GameObject("Dim", typeof(Image)).GetComponent<Image>();
        dim.transform.SetParent(root.transform, false);
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        Stretch(dim.rectTransform);

        // 黑框面板（与事件界面的"黑框"风格一致）
        GameObject panel = new GameObject("Panel", typeof(Image), typeof(Outline));
        panel.transform.SetParent(root.transform, false);
        Image panelImg = panel.GetComponent<Image>();
        panelImg.color = new Color(0.06f, 0.07f, 0.10f, 0.96f);
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.62f, 0.90f, 1f);
        outline.effectDistance = new Vector2(3f, 3f);
        SetCentered(panel.GetComponent<RectTransform>(), Vector2.zero, size);
        return panel;
    }

    private void CloseModal()
    {
        if (modalRoot != null) Destroy(modalRoot);
        modalRoot = null;
    }

    // ============================================================
    //                          小工具
    // ============================================================

    private Text NewText(Transform parent, string name, string content, int fontSize, Vector2 pos, Vector2 size,
        TextAnchor anchor)
    {
        Text text = new GameObject(name, typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = uiFont;
        text.text = content;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        SetCentered(text.rectTransform, pos, size);
        return text;
    }

    private Button NewDialogButton(Transform parent, string label, Vector2 pos, UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = dialogButtonSprite;
        img.color = dialogButtonSprite != null ? Color.white : new Color(0.56f, 0.76f, 0.90f, 1f);
        SetCentered(img.rectTransform, pos, new Vector2(300f, 92f));

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        if (dialogButtonHoverSprite != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            SpriteState st = btn.spriteState;
            st.highlightedSprite = dialogButtonHoverSprite;
            st.pressedSprite = dialogButtonHoverSprite;
            btn.spriteState = st;
        }
        btn.onClick.AddListener(onClick);

        Text text = NewText(go.transform, "Text", label, 32, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        text.color = new Color(0.07f, 0.12f, 0.22f, 1f);
        Stretch(text.rectTransform);
        return btn;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetCentered(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
