using UnityEngine;
using UnityEngine.UI;

// ═══════════════════════════════════════════════════════════════
//  事件界面模版（EventPanel）
//  游戏内多种事件共用同一份版式，内容取自飞书「游戏事件」表：
//    1. 顶部：与战斗界面相同的深蓝色长条（105 高），上面保留
//       三个空饮料槽位 + 左上角血量槽红圆；耐力/思绪槽不再显示
//       （被下方铺满的一个UI框_1 盖住的区域语义上等价于不画）
//    2. 深蓝长条以下的整个屏幕：一个UI框_1 拉伸铺满（1920×975）；
//       屏幕正中再生成一个纯黑长方形（1140×825）作为事件内容区
//    3. 黑色长方形最左侧、紧贴框上侧：淡蓝色按键_1（黑字写事件名称，
//       底边贴框顶、整体悬在框上方）
//    4. 黑色长方形内右下角：三个 耐力槽_2，距右 50px / 距下 10px /
//       间隔 5px；每个槽宽度按选项文本自适应，选项不满三个不建空槽
//    5. 描述文本写在黑色长方形内：左缘对齐三槽位的最左侧（固定
//       x=180），顶部距框上侧约 20px（黑底白字）
//  具体事件调用 Show(名称, 描述, 选项数组) 打开；Close() 关闭。
// ═══════════════════════════════════════════════════════════════
public class EventPanel : MonoBehaviour
{
    // ---- 素材引用（场景无需预配，编辑器下按 Art/UI 路径兜底加载） ----
    public Sprite statusBarSprite;    // 深蓝色长条_2：顶部状态栏底板
    public Sprite frameBackSprite;    // 一个UI框_1：顶部以下铺满整屏的底框
    public Sprite buttonSprite;       // 淡蓝色按键_1：名称按钮常态
    public Sprite buttonHoverSprite;  // 淡蓝色按键_2：按钮悬停/按下
    public Sprite slotSprite;         // 耐力槽_2：选项槽位
    public Sprite hpSlotSprite;       // 血量槽_1：左上角红色圆
    public Sprite potionSlotSprite;   // 空饮料槽位：长条上的三个空药水槽

    // ---- 布局常量 ----
    private const float statusBarH = 105f;   // 顶部深蓝长条高（与战斗界面 ×0.7 后一致）
    private const float frameW = 1140f;      // 中央黑框宽（沿用旧版：旧框转置 ×1.5）
    private const float frameH = 495f;       // 中央黑框高 = 825×0.6
    private const float btnH = 50f;          // 名称按钮显示高（100×0.5）
    private const float btnW = 607.5f;       // 名称按钮显示宽（405×1.5，素材横向拉伸）
    private const float slotH = 62.4f;       // 选项槽显示高（52×1.2）
    private const float slotGap = 5f;        // 槽位间隔
    private const float slotRightGap = 20f;  // 槽右缘距黑框右
    private const float slotBottomGap = 10f; // 最下槽底缘距黑框下
    private const float slotMaxW = 1260f;    // 选项槽宽度上限（840×1.5）
    private const float slotMinW = 540f;     // 选项槽宽度下限（360×1.5）
    private const float descTopGap = 20f;    // 描述文本顶部距框上侧

    /// <summary>描述文本左缘（框左起）＝ 选项槽最左侧，由 BuildOptions/占位槽写入</summary>
    private float optionLeftEdge;

    private static EventPanel instance;
    public static bool IsOpen => instance != null;

    /// <summary>事件内容挂点：黑框内的空白区域，具体事件界面往这里填内容</summary>
    private RectTransform eventContent;
    public RectTransform EventContent => eventContent;

    // Show() 传入的事件数据（Awake 时消费）
    private static string pendingName;
    private static string pendingDesc;
    private static string[] pendingOptions;
    private static UnityEngine.Events.UnityAction<int>[] pendingActions;

    // 运行时引用
    private Text nameText;              // 名称按钮上的黑字
    private readonly System.Collections.Generic.List<RectTransform> optionSlots
        = new System.Collections.Generic.List<RectTransform>();

    // ══════════════════════════════════════════════════════════
    //  对外接口
    // ══════════════════════════════════════════════════════════

    /// <summary>打开事件模版。传事件数据则填充名称/描述/选项；不传则只出空模版。</summary>
    /// <param name="eventName">事件名称（写在淡蓝色按钮里，黑字）</param>
    /// <param name="description">事件描述（黑框内白字文本）</param>
    /// <param name="options">选项 1~3（放入耐力槽，不满三个不建空槽）</param>
    /// <param name="optionActions">选项点击回调（下标对应选项；null = 暂无行为 TODO）</param>
    public static EventPanel Show(string eventName = null, string description = null,
        string[] options = null, UnityEngine.Events.UnityAction<int>[] optionActions = null)
    {
        CloseCurrent();
        pendingName = eventName;
        pendingDesc = description;
        pendingOptions = options;
        pendingActions = optionActions;
        GameObject go = new GameObject("EventPanel", typeof(EventPanel));
        return go.GetComponent<EventPanel>();
    }

    /// <summary>事件选项完成后关闭界面，并通知本局流程进入下一关。</summary>
    public static void Close()
    {
        if (instance == null) return;
        CloseCurrent();
        if (RunFlowController.Instance != null)
            RunFlowController.Instance.CompleteEvent();
    }

    private static void CloseCurrent()
    {
        if (instance == null) return;
        EventPanel panel = instance;
        instance = null;
        Destroy(panel.gameObject);
    }

    // ══════════════════════════════════════════════════════════
    //  构建
    // ══════════════════════════════════════════════════════════

    private void Awake()
    {
        instance = this;

#if UNITY_EDITOR
        const string uiDir = "Assets/Game/Art/UI/";
        if (statusBarSprite == null)
            statusBarSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "深蓝色长条_2.png");
        if (frameBackSprite == null)
            frameBackSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "一个UI框_1.png");
        if (buttonSprite == null)
            buttonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_1.png");
        if (buttonHoverSprite == null)
            buttonHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_2.png");
        if (slotSprite == null)
            slotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "耐力槽_2.png");
        if (hpSlotSprite == null)
            hpSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "血量槽_1.png");
        if (potionSlotSprite == null)
            potionSlotSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "空饮料槽位.png");
#endif

        // ---- 独立 Canvas：盖住战斗 HUD（500），让位给战斗结束界面（600） ----
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 550;
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0f;   // 显式匹配宽度：水平基准恒 1920
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        // ============================================================
        // 1. 顶部深蓝色长条 + 药水槽 + 血量槽
        //    （以下三段为 BattleHUD 战斗界面顶部状态栏的拷贝，视觉完全一致）
        // ============================================================
        // 兜底色块：顶部整行 105 高（与长条同区域同色）——长条素材万一加载
        // 失败/尺寸异常时露出的也是均匀深蓝，不会出现"两条拼接"或场景内容
        RectTransform backfillRt = NewRect(transform, "StatusBar_Backfill",
            new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, statusBarH));
        Image backfill = backfillRt.gameObject.AddComponent<Image>();
        backfill.sprite = null;
        backfill.color = new Color(0.192f, 0.518f, 0.812f, 1f);   // 深蓝色长条_2 内芯主色
        backfill.raycastTarget = true;

        // ---- 深蓝色长条_2 铺满屏幕顶部 ----
        // pivot=(0.5,1)：pos(0,0) 表示"顶边中点贴 canvas 顶"，105 高全部落在屏内
        RectTransform barRt = NewRect(transform, "StatusBar_Bar",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1920f, statusBarH));
        barRt.pivot = new Vector2(0.5f, 1f);
        Image barImg = barRt.gameObject.AddComponent<Image>();
        barImg.sprite = statusBarSprite;
        barImg.color = statusBarSprite == null ? new Color(0.192f, 0.518f, 0.812f, 1f) : Color.white;

        // ---- 长条偏右：三个饮料槽位，起点在中轴右侧 420px，间隔 5px ----
        // （同 BattleHUD.PotionSlot_*：77 高、垂直居中于 105 高的长条 y=-52.5）
        if (potionSlotSprite != null)
        {
            float potionSlotH = 77f;
            float potionSlotW = potionSlotH * 815f / 931f;
            for (int i = 0; i < 3; i++)
            {
                float x = 420f + potionSlotW / 2f + i * (potionSlotW + 5f);   // 第 i 只瓶子中心的 x 偏移
                Image slot = NewImage(transform, "PotionSlot_" + i, potionSlotSprite, Color.white,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(x, -52.5f), new Vector2(potionSlotW, potionSlotH));
                slot.preserveAspect = true;
                slot.raycastTarget = false;   // 事件界面里药水槽纯展示
            }
        }

        // ============================================================
        // 2. 一个UI框_1 铺满长条以下的整屏 + 屏幕正中的黑色长方形
        // ============================================================
        RectTransform back = NewRect(transform, "FrameBack",
            new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        back.offsetMin = Vector2.zero;              // 底边贴屏幕底
        back.offsetMax = new Vector2(0f, -statusBarH);   // 顶边贴深蓝长条下沿 → 1920×975
        Image backImg = back.gameObject.AddComponent<Image>();
        backImg.sprite = frameBackSprite;
        backImg.color = frameBackSprite == null ? new Color(0.81f, 0.81f, 0.81f, 1f) : Color.white;
        backImg.raycastTarget = true;               // 挡住下层 HUD 的耐力/思绪槽等点击

        // ---- 左上角：血量槽红圆（创建于 FrameBack 之后 → 处于一个UI框_1 图层之上） ----
        // pivot=(0,1)：pos(24,-24) 表示"圆的左上角距屏左/上各 24"，132×132 完整可见
        Image hpSlot = NewImage(transform, "StatusBar_HP", hpSlotSprite,
            hpSlotSprite == null ? new Color(0.89f, 0.23f, 0.23f, 0.95f) : Color.white,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(132f, 132f));
        hpSlot.rectTransform.pivot = new Vector2(0f, 1f);
        hpSlot.raycastTarget = false;
        Text hpText = MakeChildText(hpSlot.rectTransform, "12/12", 30, Color.white);

        // 屏幕正中的纯黑长方形（事件内容区）
        RectTransform frame = NewRect(transform, "EventFrame",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(frameW, frameH));
        Image blackRect = frame.gameObject.AddComponent<Image>();
        blackRect.sprite = null;
        blackRect.color = Color.black;

        // 事件内容挂点：黑框内部（留边距），供具体事件填充
        eventContent = NewRect(frame, "EventContent",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
            new Vector2(frameW - 80f, frameH - 80f));

        // ============================================================
        // 3. 名称按钮：黑框最左侧、底边贴框顶（整体悬在框上方）
        // ============================================================
        Button topLeft = NewImageButton(frame, "NameButton", buttonSprite,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(btnW, btnH), null, buttonHoverSprite);
        topLeft.image.rectTransform.pivot = new Vector2(0f, 0f);
        nameText = MakeChildText(topLeft.image.rectTransform, "", 30, Color.black);

        // ============================================================
        // 4. 选项槽（无事件数据时画三个固定占位槽）
        // ============================================================
        // 无事件数据时的占位：三个固定槽，右缘距框右 20、竖直排列从上到下、最下槽距框下 10
        if (pendingOptions == null)
        {
            for (int i = 0; i < 3; i++)
            {
                // 选项 1 在最上：槽 i 的底边 = 10 + (2-i)×(槽高+间隔)
                float bottom = slotBottomGap + (2 - i) * (slotH + slotGap);
                Image slot = NewImage(frame, "EventSlot_" + i, slotSprite,
                    slotSprite == null ? new Color(0.85f, 0.85f, 0.87f, 0.9f) : Color.white,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(frameW / 2f - slotRightGap - slotMinW / 2f, -(bottom + slotH / 2f)),
                    new Vector2(slotMinW, slotH));
                slot.raycastTarget = false;
            }
            optionLeftEdge = frameW - slotRightGap - slotMinW;
        }

        // ---- 填充事件数据（Show() 传入的内容） ----
        if (!string.IsNullOrEmpty(pendingName)) nameText.text = pendingName;
        if (pendingOptions != null) BuildOptions(frame, pendingOptions, pendingActions);
        if (!string.IsNullOrEmpty(pendingDesc)) BuildDescription(frame, pendingDesc);
    }

    /// <summary>
    /// 描述文本：黑底白字自动换行，顶部距框上侧约 20px；
    /// 左缘与选项槽（耐力槽）的最左侧对齐（optionLeftEdge，框左起）。
    /// </summary>
    private void BuildDescription(RectTransform frame, string description)
    {
        float x = Mathf.Min(optionLeftEdge, frameW - slotRightGap - 120f);  // 防文本区过窄兜底
        float width = frameW - slotRightGap - x;
        RectTransform rt = NewRect(frame, "Description",
            new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(x, -descTopGap), new Vector2(width, 200f));
        rt.pivot = new Vector2(0f, 1f);
        Text t = MakeChildText(rt, description, 30, Color.white);
        t.alignment = TextAnchor.UpperLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    /// <summary>
    /// 选项槽：最多三个，右缘距框右 20、竖直排列从上到下（选项 1 在最上）、
    /// 最下槽底缘距框下 10、间隔 5；每个槽宽度按选项文本自适应
    /// （不足三个时不创建空槽，等价于从下往上删除）。
    /// </summary>
    private void BuildOptions(RectTransform frame, string[] options,
        UnityEngine.Events.UnityAction<int>[] actions)
    {
        int n = Mathf.Min(options.Length, 3);
        float[] ws = new float[n];
        for (int i = 0; i < n; i++)
        {
            // 先量文本：按字号 26 + 左右留白估槽宽
            string s = options[i] ?? "";
            float textW = EstimateTextWidth(s, 26);
            ws[i] = Mathf.Clamp(textW + 48f, slotMinW, slotMaxW);
        }

        // 竖排：选项 n-1 在最下（底缘距框下 10），选项 0 在最上；所有槽右缘对齐
        float rightEdge = frameW / 2f - slotRightGap;   // 槽右缘（框中心坐标系）
        float maxW = 0f;
        for (int i = 0; i < n; i++) maxW = Mathf.Max(maxW, ws[i]);
        optionLeftEdge = frameW / 2f + (rightEdge - maxW);   // 最左槽左缘（框左起）
        for (int i = 0; i < n; i++)
        {
            float bottom = slotBottomGap + (n - 1 - i) * (slotH + slotGap);
            int captured = i;
            Button slot = NewImageButton(frame, "OptionSlot_" + i, slotSprite,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(rightEdge - ws[i] / 2f, -(bottom + slotH / 2f)),
                new Vector2(ws[i], slotH),
                actions != null && actions[i] != null ? () => actions[i](captured) :
                    (UnityEngine.Events.UnityAction)(() => { /* TODO: 接选项效果 */ }));
            Text t = MakeChildText(slot.image.rectTransform, options[i], 26, Color.black);
            t.alignment = TextAnchor.MiddleCenter;
            optionSlots.Add(slot.image.rectTransform);
        }
    }

    /// <summary>估算一行文本宽度（中文按字号 1:1、ASCII 按 0.55 近似）</summary>
    private static float EstimateTextWidth(string s, int fontSize)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        float w = 0f;
        foreach (char c in s)
            w += c > 0x2E80 ? fontSize : fontSize * 0.55f;
        return w;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    // ══════════════════════════════════════════════════════════
    //  UI 工具（与 BattleEndPanel 同款轻量实现）
    // ══════════════════════════════════════════════════════════

    /// <summary>创建一个纯 RectTransform 容器</summary>
    private static RectTransform NewRect(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        RectTransform rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>创建一张图片（sprite 为 null 时退化为纯色块）</summary>
    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        RectTransform rt = NewRect(parent, name, anchorMin, anchorMax, pos, size);
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        return img;
    }

    /// <summary>创建一个图片按钮，支持悬停/按下换图</summary>
    private static Button NewImageButton(Transform parent, string name, Sprite sprite,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size,
        UnityEngine.Events.UnityAction onClick, Sprite hoverSprite = null)
    {
        Image img = NewImage(parent, name, sprite, sprite == null ? new Color(0.15f, 0.19f, 0.27f, 0.98f) : Color.white,
            anchorMin, anchorMax, pos, size);
        Button btn = img.gameObject.AddComponent<Button>();
        if (onClick != null) btn.onClick.AddListener(onClick);
        if (hoverSprite != null)
        {
            btn.transition = Selectable.Transition.SpriteSwap;
            SpriteState st = btn.spriteState;
            st.highlightedSprite = hoverSprite;
            st.pressedSprite = hoverSprite;
            btn.spriteState = st;
        }
        return btn;
    }

    /// <summary>在容器内铺满一张文本</summary>
    private static Text MakeChildText(RectTransform parent, string content, int size, Color color)
    {
        Text t = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        t.rectTransform.SetParent(parent, false);
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = Vector2.zero;
        t.rectTransform.offsetMax = Vector2.zero;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null)
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.supportRichText = false;
        t.raycastTarget = false;   // 不挡按钮点击
        return t;
    }
}
