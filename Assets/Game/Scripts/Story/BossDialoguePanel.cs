using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 仿明日方舟的剧情对话界面（运行时生成，不需要改场景、不依赖 Prefab）。
///
/// 布局：
///   · 全屏半透明黑底：压暗战场，同时接住点击（点画面任意处推进台词）；
///   · 底部大框【一个UI框_1】，不透明度 71%；
///   · 框左上角名牌【淡蓝色按键_1】，不透明度 82%，写当前说话人；
///   · 框内正文打字机逐字显示：点一下先显示整句，再点下一句；
///   · 右上角“跳过 ▶”：直接结束整段对话。
///
/// 触发点：
///   · TryPlayBeforeBattle —— LevelManager 布阵完成后、正式开战前调用：
///     关卡里有配置过对话的单位（如林德沃姆）就播“第一次 / 第二次及以后遇见”，
///     播完再调 onFinished 开战；
///   · TryPlayVictory —— BattleEndPanel 收到胜利时调用：该单位被击败就播胜利对话，
///     播完再打开战斗结算界面。
///
/// 素材取自 Resources/BattleUIAssetCatalog（一个UI框_1 = eventFrameSprite、
/// 淡蓝色按键_1 = buttonSprite），正式包与编辑器表现一致。
/// 对话内容全部来自 Resources/BossDialogues/ 下的 BossDialogueData 资产。
/// </summary>
public class BossDialoguePanel : MonoBehaviour
{
    // ---------------- 布局（对齐 PSD「对话界面_1」） ----------------
    // 底板：纯白满屏底 + 底部一个UI框_1 的灰板，灰板满宽、上沿在屏幕 63% 高度处
    private const float frameHeight = 400f;              // 灰板可见高度（1080 的 63% 到屏底）
    private const float frameAlpha = 0.71f;              // 需求：一个UI框_1 不透明度 71%
    private const float plateAlpha = 0.82f;              // 需求：淡蓝色按键_1 不透明度 82%
    private const float plateAspect = 1247f / 308f;      // 淡蓝色按键_1 素材宽高比
    private const float plateWidth = 542f;               // 名牌显示宽（PSD 里 533/1890 满宽换算）
    private const float portraitLeftX = 241f;            // 左立绘本体左边距（PSD 85 + 图层内留白 193×0.765）
    private const float portraitRightMargin = 142f;      // 右立绘右边距（PSD 1890-1778）
    private const float portraitBottom = 0f;             // 立绘底边贴屏幕底边（PSD 里脚被裁掉）
    private const float barHeight = 105f;                // 顶部蓝条高：与战斗 HUD / 事件界面同一尺寸
    private const float hpBallSize = 132f;               // 红球边长：与战斗 HUD 同一尺寸
    private const float hpBallX = 24f;                   // 红球距屏幕左 24（同战斗 HUD）
    private const float hpBallTop = 24f;                 // 红球距屏幕顶 24（同战斗 HUD）

    // ---------------- 运行时状态 ----------------
    private Canvas canvas;
    private Text nameText;
    private Text bodyText;
    private Image leftPortrait;        // 左：主角
    private Image rightPortrait;       // 右：对手
    private string leftName = "";
    private string rightName = "";

    private readonly List<DialogueLine> lines = new List<DialogueLine>();
    private int index;
    private float charactersPerSecond = 45f;
    private bool typing;               // 正在逐字显示（点击先补全当前句）
    private float shownChars;          // 已显示的字数（浮点累加）

    private BossDialogueData data;
    private bool markSeen;             // 本段是“第一次遇见”，结束时记入 PlayerPrefs
    private UnityAction onFinished;

    private Font font;
    private Sprite frameSprite;
    private Sprite plateSprite;
    private Sprite barSprite;          // 深蓝色长条_2：顶部蓝条（PSD 顶层血条区）
    private Sprite hpBallSprite;       // 血量槽_1：左上角红球
    private Text hpText;               // 红球里的“当前/上限”
    private static BossDialogueData[] cachedConfigs;

    // 玩家单位配置（Resources/玩家）缓存：只用来兜底算“开局满血”
    private const string PlayerDataResourceName = "玩家";
    private static bool playerDataLoaded;
    private static UnitData cachedPlayerData;

    // ============================================================
    //                       对外入口
    // ============================================================

    /// <summary>
    /// 开战前播对话。关卡里含配置过的单位时返回 true（对话结束后调用 onFinished 开战），
    /// 没有对应配置返回 false，调用方立刻开战。
    /// </summary>
    public static bool TryPlayBeforeBattle(LevelData level, UnityAction onFinished)
    {
        BossDialogueData config = FindForLevel(level);
        if (config == null) return false;

        bool seen = HasSeenFirst(config);
        List<DialogueLine> script = seen ? config.repeatEncounter : config.firstEncounter;
        if (!HasLines(script)) script = seen ? config.firstEncounter : config.repeatEncounter;
        if (!HasLines(script)) return false;

        Play(config, script, !seen, onFinished);
        return true;
    }

    /// <summary>
    /// 该单位被击败后播胜利对话。没有配置 / 没有台词返回 false。
    /// </summary>
    public static bool TryPlayVictory(string unitName, UnityAction onFinished)
    {
        BossDialogueData config = FindByUnitName(unitName);
        if (config == null || !HasLines(config.victory)) return false;

        Play(config, config.victory, false, onFinished);
        return true;
    }

    /// <summary>这个名字的敌人是否有对话配置（结算界面据此决定要不要先播胜利对话）</summary>
    public static bool HasConfigFor(string unitName)
    {
        return FindByUnitName(unitName) != null;
    }

    // ============================================================
    //                       配置查找
    // ============================================================

    private static BossDialogueData[] Configs()
    {
        if (cachedConfigs == null)
        {
            cachedConfigs = Resources.LoadAll<BossDialogueData>("BossDialogues");
            if (cachedConfigs == null || cachedConfigs.Length == 0)
                Debug.LogWarning("[BossDialogue] Resources/BossDialogues 下没有对话配置");
        }
        return cachedConfigs;
    }

    private static BossDialogueData FindByUnitName(string unitName)
    {
        if (string.IsNullOrEmpty(unitName)) return null;
        foreach (BossDialogueData config in Configs())
            if (config != null && config.unitName == unitName) return config;
        return null;
    }

    /// <summary>在关卡的所有变种里找配置过的单位（单位名与 UnitData.unitName 完全一致）</summary>
    private static BossDialogueData FindForLevel(LevelData level)
    {
        if (level == null || level.variants == null) return null;
        foreach (BossDialogueData config in Configs())
        {
            if (config == null || string.IsNullOrEmpty(config.unitName)) continue;
            foreach (LevelData.Variant variant in level.variants)
            {
                if (variant == null || variant.spawns == null) continue;
                foreach (LevelData.Spawn spawn in variant.spawns)
                    if (spawn != null && spawn.unit != null && spawn.unit.unitName == config.unitName)
                        return config;
            }
        }
        return null;
    }

    private static bool HasLines(List<DialogueLine> script)
    {
        if (script == null) return false;
        foreach (DialogueLine line in script)
            if (line != null && !string.IsNullOrEmpty(line.text)) return true;
        return false;
    }

    // ---------------- “第一次遇见”的记录 ----------------

    private static string SeenKey(BossDialogueData config)
    {
        string suffix = string.IsNullOrEmpty(config.seenKey) ? config.unitName : config.seenKey;
        return "BossDialogue_" + suffix;
    }

    private static bool HasSeenFirst(BossDialogueData config)
    {
        return PlayerPrefs.GetInt(SeenKey(config), 0) == 1;
    }

    private static void MarkSeen(BossDialogueData config)
    {
        PlayerPrefs.SetInt(SeenKey(config), 1);
        PlayerPrefs.Save();
    }

    // ============================================================
    //                       创建 / 构建
    // ============================================================

    private static void Play(BossDialogueData config, List<DialogueLine> script,
        bool firstTime, UnityAction onFinished)
    {
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        GameObject go = new GameObject("BossDialogueCanvas");
        BossDialoguePanel panel = go.AddComponent<BossDialoguePanel>();
        panel.Build(config, script, firstTime, onFinished);
    }

    private void Build(BossDialogueData config, List<DialogueLine> script,
        bool firstTime, UnityAction finished)
    {
        data = config;
        markSeen = firstTime;
        onFinished = finished;
        charactersPerSecond = Mathf.Max(1f, config.charactersPerSecond);
        leftName = config.leftName;
        rightName = config.rightName;

        lines.Clear();
        foreach (DialogueLine line in script)
            if (line != null && !string.IsNullOrEmpty(line.text)) lines.Add(line);

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // 素材：优先取 Resources 里的 UI 资产目录（正式包也能读到）
        BattleUIAssetCatalog assets = Resources.Load<BattleUIAssetCatalog>("BattleUIAssetCatalog");
        if (assets != null)
        {
            frameSprite = assets.eventFrameSprite;   // 一个UI框_1
            plateSprite = assets.buttonSprite;       // 淡蓝色按键_1
            barSprite = assets.statusBarSprite;      // 深蓝色长条_2
            hpBallSprite = assets.hpSlotSprite;      // 血量槽_1
        }
#if UNITY_EDITOR
        const string uiDir = "Assets/Game/Art/UI/";
        if (frameSprite == null)
            frameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "一个UI框_1.png");
        if (plateSprite == null)
            plateSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_1.png");
        if (barSprite == null)
            barSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "深蓝色长条_2.png");
        if (hpBallSprite == null)
            hpBallSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "血量槽_1.png");
#endif

        BuildCanvas();
        BuildView();

        index = 0;
        ShowLine();
    }

    private void BuildCanvas()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 700;   // 盖在战斗结算(600) 与战斗 HUD(500) 之上

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        gameObject.AddComponent<GraphicRaycaster>();
    }

    private void BuildView()
    {
        // ---- 全屏白底（同 PSD 的「背景」图层）：遮住战场，点击任意处推进 ----
        GameObject dimGo = new GameObject("Backdrop", typeof(RectTransform));
        dimGo.transform.SetParent(transform, false);
        Stretch(dimGo.GetComponent<RectTransform>());

        Image dim = dimGo.AddComponent<Image>();
        dim.color = Color.white;

        Button dimButton = dimGo.AddComponent<Button>();
        dimButton.transition = Selectable.Transition.None;
        dimButton.targetGraphic = dim;
        dimButton.onClick.AddListener(Advance);

        // ---- 立绘：左主角、右对手，站在灰板后面（先建，灰板会盖住下半身）----
        // 两侧高度取同一个值（PSD 里 1255 vs 1249），脚底贴屏幕底边；
        // 显式夹住：立绘顶端不能高过顶部蓝条的下沿（1080 - 105）
        float maxPortraitHeight = 1080f - barHeight;
        float portraitHeight = Mathf.Clamp(data != null ? data.leftPortraitHeight : 960f, 1f, maxPortraitHeight);
        float rightHeight = Mathf.Clamp(data != null ? data.rightPortraitHeight : portraitHeight, 1f, maxPortraitHeight);
        leftPortrait = NewPortrait(dimGo.transform, "LeftPortrait", data != null ? data.leftPortrait : null,
            portraitHeight,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(portraitLeftX, portraitBottom));
        rightPortrait = NewPortrait(dimGo.transform, "RightPortrait", data != null ? data.rightPortrait : null,
            rightHeight,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-portraitRightMargin, portraitBottom));

        // ---- 顶部蓝条【深蓝色长条_2】：与战斗 HUD 顶部状态栏同尺寸（满宽 × 105），顶边贴屏幕顶 ----
        Image topBar = NewImage(dimGo.transform, "TopBar", barSprite,
            barSprite == null ? new Color(0.192f, 0.518f, 0.812f, 1f) : Color.white,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        topBar.rectTransform.anchoredPosition = Vector2.zero;
        topBar.rectTransform.sizeDelta = new Vector2(0f, barHeight);

        // ---- 底部灰板：一个UI框_1（不透明度 71%），满屏宽、上沿在 63% 高度 ----
        Image frame = NewImage(dimGo.transform, "Frame", frameSprite,
            new Color(1f, 1f, 1f, frameAlpha),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
        frame.rectTransform.anchoredPosition = Vector2.zero;
        frame.rectTransform.sizeDelta = new Vector2(0f, frameHeight);

        // ---- 名牌：淡蓝色按键_1（不透明度 82%），坐在灰板上沿、贴屏幕左边 ----
        // PSD：left=-1、top=762、533x132，下沿正好压着灰板上沿（894）
        Image plate = NewImage(frame.transform, "NamePlate", plateSprite,
            new Color(1f, 1f, 1f, plateAlpha),
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f));
        plate.rectTransform.anchoredPosition = Vector2.zero;
        plate.rectTransform.sizeDelta = new Vector2(plateWidth, plateWidth / plateAspect);

        nameText = NewText(plate.transform, "", 30, new Color(0.14f, 0.22f, 0.36f),
            TextAnchor.MiddleCenter);
        // 左侧让开素材上的白色竖条
        nameText.rectTransform.offsetMin = new Vector2(46f, 6f);
        nameText.rectTransform.offsetMax = new Vector2(-16f, -6f);

        // ---- 正文：灰板内左对齐，深色字 ----
        // 名牌已经整个坐在灰板上沿以上，正文不用再避让名牌，直接从灰板内上沿开始
        bodyText = NewText(frame.transform, "", 32, new Color(0.10f, 0.12f, 0.16f),
            TextAnchor.UpperLeft);
        bodyText.lineSpacing = 1.25f;
        bodyText.verticalOverflow = VerticalWrapMode.Truncate;
        bodyText.rectTransform.offsetMin = new Vector2(64f, 40f);
        bodyText.rectTransform.offsetMax = new Vector2(-64f, -44f);

        // ---- 右上角“跳过”：直接结束整段对话（白底上用深色字） ----
        GameObject skipGo = new GameObject("Skip", typeof(RectTransform));
        skipGo.transform.SetParent(transform, false);
        RectTransform skipRt = skipGo.GetComponent<RectTransform>();
        skipRt.anchorMin = new Vector2(1f, 1f);
        skipRt.anchorMax = new Vector2(1f, 1f);
        skipRt.pivot = new Vector2(1f, 1f);
        skipRt.anchoredPosition = new Vector2(-40f, -28f);
        skipRt.sizeDelta = new Vector2(150f, 48f);

        Image skipBg = skipGo.AddComponent<Image>();
        skipBg.color = new Color(0f, 0f, 0f, 0.08f);

        Button skipButton = skipGo.AddComponent<Button>();
        skipButton.transition = Selectable.Transition.None;
        skipButton.targetGraphic = skipBg;
        skipButton.onClick.AddListener(Close);

        Text skipText = NewText(skipGo.transform, "跳过 ▶", 26,
            new Color(0.22f, 0.25f, 0.31f), TextAnchor.MiddleCenter);
        Stretch(skipText.rectTransform);

        // ---- 左上角红球【血量槽_1】+ “血量”小字 + 当前/上限（与战斗 HUD 同尺寸同位置） ----
        Image hpBall = NewImage(dimGo.transform, "HPBall", hpBallSprite,
            hpBallSprite == null ? new Color(0.89f, 0.23f, 0.23f, 0.95f) : Color.white,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        hpBall.rectTransform.anchoredPosition = new Vector2(hpBallX, -hpBallTop);
        hpBall.rectTransform.sizeDelta = new Vector2(hpBallSize, hpBallSize);

        // “血量”：字号 20、距圆顶 8px、132×26 居中（BattleHUD 红圆同款）
        Text hpLabel = NewText(hpBall.transform, "血量", 20, new Color(1f, 1f, 1f, 0.85f),
            TextAnchor.MiddleCenter);
        hpLabel.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        hpLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        hpLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        hpLabel.rectTransform.anchoredPosition = new Vector2(0f, -8f);
        hpLabel.rectTransform.sizeDelta = new Vector2(hpBallSize, 26f);

        // 数字：字号 30、白字、占圆内“血量”以下的剩余区域
        hpText = NewText(hpBall.transform, "", 30, Color.white, TextAnchor.MiddleCenter);
        hpText.rectTransform.offsetMin = Vector2.zero;
        hpText.rectTransform.offsetMax = new Vector2(0f, -34f);
    }

    // ============================================================
    //                       播放
    // ============================================================

    private string CurrentText
    {
        get { return index >= 0 && index < lines.Count ? lines[index].text : ""; }
    }

    private void ShowLine()
    {
        if (index < 0 || index >= lines.Count) return;
        nameText.text = lines[index].speaker;
        ApplySpeakerHighlight(lines[index].speaker);
        bodyText.text = "";
        shownChars = 0f;
        typing = true;
    }

    /// <summary>
    /// 谁说话谁亮：说话人的立绘保持原色，没说话的压暗（偏冷灰）；
    /// 名字两边都对不上（旁白）时两边都保持原色。
    /// </summary>
    private void ApplySpeakerHighlight(string speaker)
    {
        bool leftSpeaking = speaker == leftName;
        bool rightSpeaking = speaker == rightName;
        if (!leftSpeaking && !rightSpeaking)
        {
            leftSpeaking = true;
            rightSpeaking = true;
        }
        SetPortraitListening(leftPortrait, leftSpeaking);
        SetPortraitListening(rightPortrait, rightSpeaking);
    }

    private static void SetPortraitListening(Image portrait, bool speaking)
    {
        if (portrait == null) return;
        // 不说话的立绘变暗：RGB 压到 55% 左右，稍微偏冷（保留亮度，别黑成剪影）
        portrait.color = speaking ? Color.white : new Color(0.55f, 0.57f, 0.62f, 1f);
    }

    private void Update()
    {
        RefreshHealth();

        // 空格翻页：需求是"点击屏幕或按空格"。
        // 全屏底图的 Button 只响应鼠标点击；EventSystem 的 Submit 必须先用 SetSelectedGameObject
        // 选中某个对象才会派发（本面板没选中任何按钮），所以这里直接读键盘，
        // 编辑器与打包版（activeInputHandler = 旧版 Input Manager）表现一致。
        if (Input.GetKeyDown(KeyCode.Space)) Advance();

        if (!typing || bodyText == null) return;

        // 用 unscaledDeltaTime：万一有界面把 timeScale 归零，打字机也要能走
        shownChars += Time.unscaledDeltaTime * charactersPerSecond;
        int count = Mathf.Min(CurrentText.Length, Mathf.FloorToInt(shownChars));
        bodyText.text = CurrentText.Substring(0, count);
        if (count >= CurrentText.Length) typing = false;
    }

    // ---------------- 左上角血量（与战斗 HUD / 事件界面同一口径） ----------------

    /// <summary>每帧把红球里的数字刷成“当前/上限”，取不到数据时留空</summary>
    private void RefreshHealth()
    {
        if (hpText == null) return;

        int current, max;
        if (!TryGetRunHealth(out current, out max)) return;

        string want = current + "/" + max;
        if (hpText.text != want) hpText.text = want;
    }

    /// <summary>
    /// 本局血量（当前 / 上限），取值顺序与战斗 HUD 对齐：
    ///  · 场上有玩家 → 玩家实时血量
    ///  · 没有玩家（开战前的对话）→ 本局快照 PlayerRunState
    ///  · 本局还没开始 → 玩家配置的生命上限（开局满血）
    /// </summary>
    private static bool TryGetRunHealth(out int current, out int max)
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player != null)
        {
            current = player.GetCurrentHealth();
            max = player.GetMaxHealth();
            return true;
        }

        if (PlayerRunState.HasSnapshot)
        {
            current = PlayerRunState.Health;
            max = PlayerRunState.MaxHealth;
            return true;
        }

        UnitData playerConfig = GetPlayerConfig();
        if (playerConfig != null)
        {
            max = Mathf.Max(1, playerConfig.maxHealth);
            current = max;
            return true;
        }

        current = 0;
        max = 0;
        return false;
    }

    /// <summary>读玩家单位配置（Resources/玩家），只加载一次后复用；数值由 Inspector 决定</summary>
    private static UnitData GetPlayerConfig()
    {
        if (playerDataLoaded) return cachedPlayerData;
        playerDataLoaded = true;
        cachedPlayerData = Resources.Load<UnitData>(PlayerDataResourceName);
        if (cachedPlayerData == null)
            Debug.LogWarning("[BossDialogue] Resources/" + PlayerDataResourceName +
                " 缺失，对话界面取不到玩家生命上限，血量数字会留空");
        return cachedPlayerData;
    }

    /// <summary>推进：正在打字 → 立刻显示整句；已显示完 → 下一句；最后一句 → 结束</summary>
    private void Advance()
    {
        if (typing)
        {
            typing = false;
            bodyText.text = CurrentText;
            return;
        }
        if (index + 1 < lines.Count)
        {
            index++;
            ShowLine();
            return;
        }
        Close();
    }

    private void Close()
    {
        if (markSeen && data != null) MarkSeen(data);
        markSeen = false;

        UnityAction callback = onFinished;
        onFinished = null;

        Destroy(gameObject);
        if (callback != null) callback();
    }

    // ============================================================
    //                       小工具
    // ============================================================

    /// <summary>按素材原始宽高比摆一张立绘：只给高度，宽度自动算，避免拉变形</summary>
    private Image NewPortrait(Transform parent, string name, Sprite sprite, float height,
        Vector2 anchor, Vector2 pivot, Vector2 position)
    {
        if (sprite == null) return null;

        float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
        Image img = NewImage(parent, name, sprite, Color.white, anchor, anchor, pivot);
        img.rectTransform.sizeDelta = new Vector2(height * aspect, height);
        img.rectTransform.anchoredPosition = position;
        return img;
    }

    private static Image NewImage(Transform parent, string name, Sprite sprite, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.color = sprite == null ? new Color(0.85f, 0.85f, 0.88f, color.a) : color;
        img.raycastTarget = false;   // 点击交给全屏底图，装饰图不参与射线检测

        RectTransform rt = img.rectTransform;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        return img;
    }

    private Text NewText(Transform parent, string content, int size, Color color,
        TextAnchor alignment)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.text = content;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Stretch(text.rectTransform);
        return text;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
