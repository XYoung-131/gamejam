using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ============================================================
//  开始界面 —— 程序启动后的第一个界面（Start.unity）
//
//  布局：
//  · 全屏背景：开始界面背景.jpg（1920×1080，铺满整屏）
//  · 左下角一列四个菜单按钮（素材 开始界面UI_1~4）：
//      左缘距屏幕左边 50px，从上到下依次 UI_1 → UI_4，
//      纵向间隔 20px，最下面一个（UI_4）底边距屏幕下边 20px；
//      显示高 180px（宽按素材比例 ≈257，不拉伸）。
//  · UI_1（开始游戏）：鼠标悬停/按下换成 开始界面UI_5，点击跳转 Battle 场景。
//  · UI_2~4：暂未接功能，纯显示（后续接 继续/设置/退出 等）。
//
//  全部 UI 运行时生成（Canvas / EventSystem 自举），不依赖预制体；
//  挂载位置：Start.unity 的 StartMenu 物体上。
// ============================================================
public class StartMenuUI : MonoBehaviour
{
    public static StartMenuUI Instance { get; private set; }

    [Header("素材（编辑器下引用丢失时按 Art 路径自动加载）")]
    public Sprite backgroundSprite;                 // 开始界面背景.jpg：全屏背景
    public Sprite[] menuSprites = new Sprite[4];    // 开始界面UI_1~4：从上到下四个菜单按钮
    public Sprite menuHoverSprite;                  // 开始界面UI_5：UI_1 悬停/按下时的底图

    [Header("目标场景")]
    [Tooltip("点击第一个按钮（开始游戏）后加载的场景名")]
    public string battleSceneName = "Battle";

    // ---- 布局参数 ----
    // 左缘距屏幕左边：原 50 → 右移 100（150）→ 再右移 150（300）→ 左移 75（225）
    private const float leftGap = 225f;
    // 最下面一个按钮底边的 y：缩小纵向间距后整列抬高，让之前被屏幕下沿截掉的 30px 内容完整露出
    private const float bottomGap = 0f;
    private const float buttonH = 180f;     // 每个按钮的显示高
    private const float gap = 10f;          // 按钮纵向间隔（原 20px，缩小让整列上收）
    private const float buttonAspect = 2244f / 1570f;   // 裁剪后素材宽高比（约 1.43）

    private Font uiFont;

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

        BuildUI();
    }

    private void BuildUI()
    {
        // 编辑器双保险：场景引用丢失时按 Art 规范路径直接加载
#if UNITY_EDITOR
        if (backgroundSprite == null)
            backgroundSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Game/Art/Backgrounds/开始界面背景.jpg");
        for (int i = 0; i < menuSprites.Length; i++)
            if (menuSprites[i] == null)
                menuSprites[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Game/Art/UI/开始界面UI_" + (i + 1) + ".png");
        if (menuHoverSprite == null)
            menuHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Game/Art/UI/开始界面UI_5.png");
#endif

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

        // ---- 全屏背景：开始界面背景.jpg ----
        Image bg = new GameObject("Background").AddComponent<Image>();
        bg.transform.SetParent(canvas.transform, false);
        bg.sprite = backgroundSprite;
        bg.raycastTarget = false;   // 背景不挡按钮点击
        Stretch(bg.rectTransform);
        // 素材 1920×1080 与基准分辨率同比例，铺满即可（素材缺失时留相机底色）

        // ---- 左下角四个菜单按钮：从上到下 UI_1 → UI_4 ----
        float buttonW = buttonH * buttonAspect;
        for (int i = 0; i < menuSprites.Length; i++)
        {
            // i=0（UI_1）在最上面：底边 y = 20 + (3-i) × (高 + 间隔)
            float y = bottomGap + (menuSprites.Length - 1 - i) * (buttonH + gap);
            GameObject go = new GameObject("Menu_" + (i + 1), typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            Image img = go.AddComponent<Image>();
            img.sprite = menuSprites[i];
            img.color = menuSprites[i] != null ? Color.white : new Color(0.4f, 0.42f, 0.48f, 0.9f);
            img.preserveAspect = true;   // 立绘保持原始比例
            img.raycastTarget = i == 0;  // 只有"开始游戏"可点，其余按钮不参与射线

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);   // 左下角锚点
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(leftGap, y);
            rt.sizeDelta = new Vector2(buttonW, buttonH);

            if (i == 0)
            {
                // ---- UI_1：开始游戏 ----
                Button btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(OnStartGameClicked);

                // 悬停 / 按下换成 开始界面UI_5（SpriteSwap，不叠颜色）
                if (menuHoverSprite != null)
                {
                    btn.transition = Selectable.Transition.SpriteSwap;
                    SpriteState st = btn.spriteState;
                    st.highlightedSprite = menuHoverSprite;
                    st.pressedSprite = menuHoverSprite;
                    btn.spriteState = st;
                }
            }
        }
    }

    /// <summary>点击"开始游戏"：打开开场事件「实验开始」；事件里选"迎接第一场战斗"再进战斗场景</summary>
    private void OnStartGameClicked()
    {
        EventPanel_实验开始.Show();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
