using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
//  游戏内 ESC 菜单
//
//  按 ESC 弹出（菜单优先级最高，接管了原先散落的 ESC 用途）：
//   · 设置            —— 弹与主界面「梳理」完全相同的设置面板
//                        （SettingsPanelUI：音量大小 + 显示模式）
//   · 保存并返回主界面 —— 把当局进度写盘并保留存档，回到主界面 UI；
//                        之后在主界面点「回忆」可以接着打
//   · 保存并退出游戏   —— 把当局进度写盘（存档保留），退出程序
//
//  打开菜单 = 暂停：战斗场景里走 GameManager.Pause()（状态机与相机一起停），
//  其它相位直接 Time.timeScale = 0；关闭菜单时恢复。
//  ESC 再按一次 = 关闭：先关设置面板，再关菜单本身。
//
//  暂停只是停时间，各组件 Update 照跑，所以 BattleHUD 里加了
//  「菜单打开就整体让位」（见 EscMenuUI.IsOpen），避免鼠标点击穿透到战斗操作。
//
//  自举方式与 RunFlowController 一致：场景加载完成后创建一次，
//  之后 DontDestroyOnLoad 跨场景常驻；Canvas 等真的用到时才懒创建。
// ============================================================
public class EscMenuUI : MonoBehaviour
{
    public static EscMenuUI Instance { get; private set; }

    /// <summary>菜单（含其设置面板）是否正开着 —— BattleHUD 据此让位</summary>
    public static bool IsOpen { get { return Instance != null && Instance.menuRoot != null; } }

    [Header("素材（编辑器下引用丢失时按 Art 路径自动加载）")]
    public Sprite buttonSprite;        // 淡蓝色按键_1：按钮常态
    public Sprite buttonHoverSprite;   // 淡蓝色按键_2：按钮悬停 / 按下

    // ---- 布局 ----
    private static readonly Vector2 PanelSize = new Vector2(900f, 640f);
    private static readonly Vector2 ButtonSize = new Vector2(420f, 92f);

    private Font uiFont;
    private Canvas canvas;              // 懒创建：没按过 ESC 的场景不会多出一个 Canvas
    private RectTransform uiRoot;
    private GameObject menuRoot;        // 遮罩 + 黑框面板整块（关闭时销毁）
    private GameObject settingsPanel;   // SettingsPanelUI 生成的设置面板

    private bool pausedViaGameManager;  // 暂停是走 GameManager 还是直接停时间
    private float prevTimeScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("~EscMenuUI");
        DontDestroyOnLoad(go);
        go.AddComponent<EscMenuUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
        ResolveButtonSprites();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ============================================================
    //                          输入
    // ============================================================

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // ESC 逐层关闭：先关设置面板，再关菜单本身；都没开才考虑弹出
        if (settingsPanel != null) { CloseSettings(); return; }
        if (menuRoot != null) { CloseMenu(); return; }
        if (CanOpen()) OpenMenu();
    }

    /// <summary>只有"局内还在打"的时候才弹菜单：主界面、结算奖励、通关 / 失败都不弹
    /// （结算 / 胜负界面自己带"返回主界面"按钮）</summary>
    private static bool CanOpen()
    {
        RunFlowController flow = RunFlowController.Instance;
        if (flow != null && flow.Progression != null)
        {
            RunPhase phase = flow.Progression.Phase;
            if (phase == RunPhase.Event || phase == RunPhase.Battle || phase == RunPhase.Rest) return true;
            if (phase != RunPhase.Menu) return false;   // 奖励 / 胜负等相位不弹
        }

        // 走到这里说明"没有本局流程"（相位是 Menu）：
        // 直接从 Battle.unity 起播就是这种情况，此时战斗里也允许弹菜单
        return GameManager.Instance != null;
    }

    // ============================================================
    //                          打开 / 关闭
    // ============================================================

    private void OpenMenu()
    {
        PauseGame();
        BuildMenu();
    }

    private void CloseMenu()
    {
        CloseSettings();
        if (menuRoot != null) Destroy(menuRoot);
        menuRoot = null;
        ResumeGame();
    }

    private void BuildMenu()
    {
        EnsureCanvas();

        menuRoot = new GameObject("EscMenuRoot", typeof(RectTransform));
        menuRoot.transform.SetParent(uiRoot, false);
        Stretch(menuRoot.GetComponent<RectTransform>());

        // 半透明遮罩：压暗并挡住下层界面的点击
        Image dim = new GameObject("Dim", typeof(Image)).GetComponent<Image>();
        dim.transform.SetParent(menuRoot.transform, false);
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        Stretch(dim.rectTransform);

        // 黑框面板（与事件 / 主界面弹窗同一套风格）
        GameObject panel = new GameObject("Panel", typeof(Image), typeof(Outline));
        panel.transform.SetParent(menuRoot.transform, false);
        panel.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.97f);
        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(0.35f, 0.62f, 0.90f, 1f);
        outline.effectDistance = new Vector2(3f, 3f);
        SetCentered(panel.GetComponent<RectTransform>(), Vector2.zero, PanelSize);

        NewText(panel.transform, "Title", "菜单", 40, new Vector2(0f, 250f), new Vector2(600f, 80f),
            TextAnchor.MiddleCenter);

        NewButton(panel.transform, "设置", new Vector2(0f, 100f), OnSettingsClicked);
        NewButton(panel.transform, "保存并返回主界面", new Vector2(0f, -20f), OnSaveAndReturnToMenu);
        NewButton(panel.transform, "保存并退出游戏", new Vector2(0f, -140f), OnSaveAndQuit);
    }

    private void EnsureCanvas()
    {
        if (canvas != null) return;

        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        canvas = new GameObject("EscMenuCanvas").AddComponent<Canvas>();
        canvas.transform.SetParent(transform, false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;   // 压在战斗 HUD(500) / 事件(550) / 结算(600) / 流程(1000) 之上
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        uiRoot = canvas.GetComponent<RectTransform>();
    }

    // ============================================================
    //                          暂停 / 恢复
    // ============================================================

    /// <summary>打开菜单时暂停：战斗里走 GameManager（状态机 + 相机一起停），其它相位直接停时间</summary>
    private void PauseGame()
    {
        prevTimeScale = Time.timeScale;

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.IsInBattle)
        {
            gm.Pause();
            pausedViaGameManager = true;
        }
        else
        {
            Time.timeScale = 0f;
            pausedViaGameManager = false;
        }
    }

    private void ResumeGame()
    {
        if (pausedViaGameManager && GameManager.Instance != null)
            GameManager.Instance.Resume();
        else
            Time.timeScale = prevTimeScale;

        pausedViaGameManager = false;
        prevTimeScale = 1f;
    }

    // ============================================================
    //                          按钮行为
    // ============================================================

    /// <summary>点「设置」：弹与主界面「梳理」同一份设置面板，盖在菜单之上</summary>
    private void OnSettingsClicked()
    {
        if (menuRoot == null || settingsPanel != null) return;
        settingsPanel = SettingsPanelUI.Show(menuRoot.transform, uiFont, buttonSprite, buttonHoverSprite, CloseSettings);
    }

    private void CloseSettings()
    {
        if (settingsPanel != null) Destroy(settingsPanel);
        settingsPanel = null;
    }

    /// <summary>点「保存并返回主界面」：存档 + 回主界面（保留存档，可"回忆"继续）</summary>
    private void OnSaveAndReturnToMenu()
    {
        Debug.Log("[EscMenu] 保存并返回主界面");
        CloseMenu();          // 先恢复时间与状态，再离开场景
        Time.timeScale = 1f;  // 兜底：别把冻结的时间带进主界面

        RunFlowController flow = RunFlowController.Instance;
        if (flow != null)
        {
            flow.SaveAndReturnToMenu();
            return;
        }

        // 兜底：直接从 Battle.unity 起播时没有 RunFlowController，没有"当局"可存，只回主界面
        Debug.LogWarning("[EscMenu] 没有 RunFlowController，本局无法存档，仅返回主界面");
        SceneManager.LoadScene("Start");
    }

    /// <summary>点「保存并退出游戏」：存档 + 退出程序</summary>
    private void OnSaveAndQuit()
    {
        Debug.Log("[EscMenu] 保存并退出游戏");
        RunSaveSystem.Save();   // 写盘；存档保留，下次启动可"回忆"继续
        CloseMenu();
        Time.timeScale = 1f;
        PlayerPrefs.Save();     // 音量 / 显示模式等设置一并落盘
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;   // 编辑器下结束运行（Application.Quit 在编辑器里无效）
#else
        Application.Quit();
#endif
    }

    // ============================================================
    //                          小工具
    // ============================================================

    /// <summary>按钮素材兜底：编辑器按 Art 路径读，正式包从 Resources 配置资产取</summary>
    private void ResolveButtonSprites()
    {
#if UNITY_EDITOR
        const string uiDir = "Assets/Game/Art/UI/";
        if (buttonSprite == null)
            buttonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_1.png");
        if (buttonHoverSprite == null)
            buttonHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_2.png");
#endif
        if (buttonSprite == null || buttonHoverSprite == null)
        {
            BattleUIAssetCatalog catalog = Resources.Load<BattleUIAssetCatalog>("BattleUIAssetCatalog");
            if (catalog != null)
            {
                if (buttonSprite == null) buttonSprite = catalog.buttonSprite;
                if (buttonHoverSprite == null) buttonHoverSprite = catalog.buttonHoverSprite;
            }
        }
    }

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

    private Button NewButton(Transform parent, string label, Vector2 pos, UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = buttonSprite;
        img.color = buttonSprite != null ? Color.white : new Color(0.56f, 0.76f, 0.90f, 1f);
        SetCentered(img.rectTransform, pos, ButtonSize);

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        if (buttonHoverSprite != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            SpriteState st = btn.spriteState;
            st.highlightedSprite = buttonHoverSprite;
            st.pressedSprite = buttonHoverSprite;
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
