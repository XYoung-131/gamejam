using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// ============================================================
//  设置面板（共用）
//
//  主界面「梳理」按钮与游戏内 ESC 菜单的「设置」按钮共用这一份：
//   · 音量大小 —— 实时改 AudioListener.volume，写 PlayerPrefs "masterVolume"
//   · 显示模式 —— 窗口 / 无边框窗口 / 全屏，点哪个立刻生效，写 PlayerPrefs "displayMode"
//
//  外观与主界面弹窗一致：半透明遮罩 + 黑框（淡蓝描边）。
//  Show(...) 在指定父物体下生成"遮罩 + 黑框面板"整块并返回根物体，
//  调用方销毁这个根物体就等于关掉面板
//  （StartMenuUI 记进 modalRoot，EscMenuUI 记进 settingsPanel）。
// ============================================================
public static class SettingsPanelUI
{
    /// <summary>三个显示档位的文案（下标 = 存档值：0 窗口、1 无边框窗口、2 全屏）</summary>
    public static readonly string[] DisplayModeNames = { "窗口模式", "无边框窗口", "全屏模式" };

    /// <summary>音量存档键（PlayerPrefs）</summary>
    public const string VolumeKey = "masterVolume";

    /// <summary>显示模式存档键（PlayerPrefs），存 DisplayModeNames 的下标</summary>
    public const string DisplayModeKey = "displayMode";

    // 切到"窗口模式"时用的窗口分辨率。这里是代码里的兜底默认值，
    // 真正的配置项在 StartMenuUI 的 Inspector 上（awake 时推进来），避免两处各写一份。
    public static int WindowedWidth = 1280;
    public static int WindowedHeight = 720;

    /// <summary>面板尺寸（与主界面弹窗同一套版式）</summary>
    public static readonly Vector2 PanelSize = new Vector2(880f, 680f);

    /// <summary>
    /// 在 parent 下生成"遮罩 + 黑框设置面板"，返回整块根物体（调用方负责销毁）。
    /// onClose：点"关闭"时的回调。
    /// </summary>
    public static GameObject Show(Transform parent, Font font, Sprite buttonSprite, Sprite buttonHoverSprite,
        UnityAction onClose)
    {
        GameObject root = new GameObject("SettingsPanelRoot", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        Stretch(root.GetComponent<RectTransform>());

        // 半透明遮罩：挡住下层界面，避免设置面板期间误点
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
        SetCentered(panel.GetComponent<RectTransform>(), Vector2.zero, PanelSize);

        NewText(font, panel.transform, "Title", "设置", 40, new Vector2(0f, 290f),
            new Vector2(600f, 80f), TextAnchor.MiddleCenter);

        // ---- 音量大小 ----
        NewText(font, panel.transform, "VolumeLabel", "音量", 32, new Vector2(-310f, 160f),
            new Vector2(200f, 60f), TextAnchor.MiddleLeft);
        float current = PlayerPrefs.GetFloat(VolumeKey, 1f);
        Text valueText = NewText(font, panel.transform, "VolumeValue", Mathf.RoundToInt(current * 100f) + "%",
            32, new Vector2(330f, 160f), new Vector2(160f, 60f), TextAnchor.MiddleRight);

        Slider slider = NewVolumeSlider(panel.transform, new Vector2(0f, 160f), new Vector2(460f, 30f), current);
        slider.onValueChanged.AddListener(v =>
        {
            AudioListener.volume = v;
            PlayerPrefs.SetFloat(VolumeKey, v);
            valueText.text = Mathf.RoundToInt(v * 100f) + "%";
        });

        // ---- 显示模式：窗口 / 无边框窗口 / 全屏，点哪个立刻生效 ----
        NewText(font, panel.transform, "DisplayLabel", "显示模式", 32, new Vector2(-310f, 30f),
            new Vector2(220f, 60f), TextAnchor.MiddleLeft);

        int mode = PlayerPrefs.GetInt(DisplayModeKey, CurrentDisplayModeIndex());
        Button[] modeButtons = new Button[DisplayModeNames.Length];
        for (int i = 0; i < DisplayModeNames.Length; i++)
        {
            int index = i;   // 闭包捕获必须拷一份，否则三个按钮都会读到循环结束时的值
            modeButtons[index] = NewDialogButton(font, buttonSprite, buttonHoverSprite, panel.transform,
                DisplayModeNames[index], new Vector2((i - 1) * 240f, -60f),
                () =>
                {
                    ApplyDisplayMode(index);
                    RefreshDisplayModeButtons(modeButtons, index);
                });
            modeButtons[index].GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 78f);
        }
        RefreshDisplayModeButtons(modeButtons, mode);

        NewDialogButton(font, buttonSprite, buttonHoverSprite, panel.transform, "关闭", new Vector2(0f, -250f), () =>
        {
            PlayerPrefs.Save();   // 关闭设置时把音量 / 显示模式落盘
            if (onClose != null) onClose();
        });

        return root;
    }

    // ============================================================
    //                          显示模式
    // ============================================================

    /// <summary>当前实际所处的显示档位（首次运行没有存档时用它当默认值，不强行改窗口）</summary>
    public static int CurrentDisplayModeIndex()
    {
        switch (Screen.fullScreenMode)
        {
            case FullScreenMode.Windowed: return 0;
            case FullScreenMode.FullScreenWindow: return 1;
            default: return 2;
        }
    }

    /// <summary>
    /// 切换显示模式并落盘。
    /// 0 = 窗口模式（按 Inspector 上配的窗口分辨率开窗；窗口可被鼠标拖动改大小，
    ///     依赖 Player Settings → Resolution and Presentation → Resizable Window）
    /// 1 = 无边框窗口（铺满屏幕但没有标题栏 / 边框）
    /// 2 = 独占全屏（真·全屏，独占显示输出）
    /// </summary>
    public static void ApplyDisplayMode(int mode)
    {
        // 已经在目标模式了就别再 SetResolution：
        // 每次进 Start 场景（含从战斗"返回主界面"重载场景）都会走 Awake，
        // 若无条件重设窗口分辨率，玩家用鼠标拖出来的窗口尺寸会被顶回默认值。
        if (mode == CurrentDisplayModeIndex())
        {
            PlayerPrefs.SetInt(DisplayModeKey, mode);
            PlayerPrefs.Save();
            return;
        }

        // 两种全屏都按显示器当前分辨率切，别拿窗口分辨率去放大（会发虚）
        Vector2 desktop = new Vector2(Screen.currentResolution.width, Screen.currentResolution.height);

        switch (mode)
        {
            case 1:
                Screen.SetResolution((int)desktop.x, (int)desktop.y, FullScreenMode.FullScreenWindow);
                break;
            case 2:
                Screen.SetResolution((int)desktop.x, (int)desktop.y, FullScreenMode.ExclusiveFullScreen);
                break;
            default:
                Screen.SetResolution(WindowedWidth, WindowedHeight, FullScreenMode.Windowed);
                break;
        }
        PlayerPrefs.SetInt(DisplayModeKey, mode);
        PlayerPrefs.Save();
    }

    /// <summary>把三个显示模式按钮的选中态刷出来：选中的亮，未选中的压灰</summary>
    private static void RefreshDisplayModeButtons(Button[] buttons, int selected)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            Image img = buttons[i].targetGraphic as Image;
            if (img != null)
                img.color = i == selected ? Color.white : new Color(0.55f, 0.58f, 0.65f, 1f);
        }
    }

    // ============================================================
    //                          小工具
    // ============================================================

    private static Text NewText(Font font, Transform parent, string name, string content, int fontSize,
        Vector2 pos, Vector2 size, TextAnchor anchor)
    {
        Text text = new GameObject(name, typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(parent, false);
        text.font = font;
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

    private static Button NewDialogButton(Font font, Sprite buttonSprite, Sprite buttonHoverSprite,
        Transform parent, string label, Vector2 pos, UnityAction onClick)
    {
        GameObject go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.sprite = buttonSprite;
        img.color = buttonSprite != null ? Color.white : new Color(0.56f, 0.76f, 0.90f, 1f);
        SetCentered(img.rectTransform, pos, new Vector2(300f, 92f));

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

        Text text = NewText(font, go.transform, "Text", label, 32, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter);
        text.color = new Color(0.07f, 0.12f, 0.22f, 1f);
        Stretch(text.rectTransform);
        return btn;
    }

    /// <summary>音量滑条（标准 UGUI Slider：背景槽 + 填充 + 手柄）</summary>
    private static Slider NewVolumeSlider(Transform parent, Vector2 pos, Vector2 size, float value)
    {
        GameObject go = new GameObject("VolumeSlider", typeof(Slider));
        go.transform.SetParent(parent, false);
        SetCentered(go.GetComponent<RectTransform>(), pos, size);

        // 背景槽
        Image bg = new GameObject("Background", typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(go.transform, false);
        bg.color = new Color(1f, 1f, 1f, 0.18f);
        RectTransform bgRt = bg.rectTransform;
        bgRt.anchorMin = new Vector2(0f, 0.25f);
        bgRt.anchorMax = new Vector2(1f, 0.75f);
        bgRt.sizeDelta = Vector2.zero;

        // 填充区（左右各留 10px 给手柄）
        RectTransform fillArea = new GameObject("Fill Area", typeof(RectTransform)).GetComponent<RectTransform>();
        fillArea.SetParent(go.transform, false);
        fillArea.anchorMin = new Vector2(0f, 0.25f);
        fillArea.anchorMax = new Vector2(1f, 0.75f);
        fillArea.pivot = new Vector2(0.5f, 0.5f);
        fillArea.sizeDelta = new Vector2(-20f, 0f);
        fillArea.anchoredPosition = Vector2.zero;

        Image fill = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(fillArea, false);
        fill.color = new Color(0.42f, 0.72f, 0.95f, 1f);
        RectTransform fillRt = fill.rectTransform;
        fillRt.pivot = new Vector2(0.5f, 0.5f);
        fillRt.sizeDelta = new Vector2(20f, 0f);

        // 手柄滑动区
        RectTransform handleArea = new GameObject("Handle Slide Area", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(go.transform, false);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.pivot = new Vector2(0.5f, 0.5f);
        handleArea.sizeDelta = new Vector2(-20f, 0f);
        handleArea.anchoredPosition = Vector2.zero;

        Image handle = new GameObject("Handle", typeof(Image)).GetComponent<Image>();
        handle.transform.SetParent(handleArea, false);
        handle.color = Color.white;
        RectTransform handleRt = handle.rectTransform;
        handleRt.pivot = new Vector2(0.5f, 0.5f);
        handleRt.sizeDelta = new Vector2(20f, 0f);

        Slider slider = go.GetComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = handleRt;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = value;
        return slider;
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
