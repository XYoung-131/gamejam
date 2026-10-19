using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// ============================================================
//  战斗结束奖励界面——胜利后接管画面
//
//  布局（全部运行时生成，Canvas sortingOrder=600 盖在 BattleHUD 之上）：
//  - 顶部深蓝长条（前 150px，含血量槽与饮料槽）保持原样；
//    长条以下的全部内容（含长条下方的耐力槽 / 思绪槽）被半透明遮罩盖住变灰，
//    遮罩 raycastTarget=true，挡住下层 HUD 按钮与世界点击；
//  - 整个屏幕正中一个【一个UI框_2】素材框，框顶放【深蓝色长条_1】标题条，
//    上写"战斗所得"；
//  - 框内高一层按上下 5px 间距纵排若干【淡蓝色按键_1】奖励按钮：
//      ① 进行 1 次技能选择 —— 免费三选一，选中的技能加入牌库；
//      ② 药水 —— 本场战斗掉落了药水才显示，点击领取进背包；
//      ③ 捡起思维碎片 —— 本场击败精英怪 / BOSS 才显示，
//         点击弹出 3 件遗物（图标 + 名称 + 效果），三选一；
//      ④ 增加 N 点耐力和思绪上限与每回合回复量（精英 +2 / BOSS +5）；
//  - 屏幕右下角【淡蓝色箭头_1】写黑色"跳过奖励"（位置与 BattleHUD 的"下一回合"
//    按钮完全一致：右缘距屏幕右边 10px、底边 86px、251×62），悬停换箭头_2，
//    点击进入下一关（LevelData.nextLevel，没配则重打本关）。
//
//  奖励的持久化：领取的东西同时记进 PlayerRunState（最小 RunData），
//  下一关生成玩家时由 LevelManager 回放，跨关不丢。
//
//  挂载位置：不需要手工挂场景——BattleHUD.Awake 运行时创建。
// ============================================================
public class BattleEndPanel : MonoBehaviour
{
    public static BattleEndPanel Instance { get; private set; }

    /// <summary>结束界面是否打开（BattleHUD 靠它隐藏悬停信息等）</summary>
    public static bool IsOpen => Instance != null && Instance.panelRoot != null && Instance.panelRoot.activeSelf;

    [Header("素材（运行时创建没有场景引用，编辑器下按 Art/UI 路径自动加载）")]
    public Sprite frameSprite;        // 一个UI框_2：中央大框
    public Sprite titleBarSprite;     // 深蓝色长条_1：标题条
    public Sprite buttonSprite;       // 淡蓝色按键_1：奖励按钮常态
    public Sprite buttonHoverSprite;  // 淡蓝色按键_2：奖励按钮悬停/按下
    public Sprite arrowSprite;        // 淡蓝色箭头_1：跳过按钮常态
    public Sprite arrowHoverSprite;   // 淡蓝色箭头_2：跳过按钮悬停/按下
    public Sprite cardFaceSprite;     // 空技能卡面：技能选择按钮左侧的小图标

    // ---------------- 布局常量（素材裁剪后的比例） ----------------
    private const float frameH = 760f;                 // 中央框显示高
    private const float frameAspect = 1182f / 1632f;   // 一个UI框_2 宽高比
    private const float titleBarAspect = 1331f / 217f; // 深蓝色长条_1 宽高比
    private const float buttonAspect = 1247f / 308f;   // 淡蓝色按键 宽高比
    private const float arrowAspect = 587f / 447f;     // 淡蓝色箭头 宽高比
    private const float statusBarH = 150f;             // 顶部深蓝长条高度：遮罩从长条下沿开始盖
    private const float optionGap = 5f;                // 奖励按钮上下间距

    // ---------------- 本场战斗的奖励状态 ----------------
    private PotionData pendingPotion;   // 本场掉落的药水（LootSystem 掷出、面板领取）
    private readonly List<RelicData> pendingRelicChoices = new List<RelicData>();   // 思维碎片的 3 件候选（打开面板时抽好）
    private bool defeatedElite;         // 本场是否击杀了精英怪
    private bool defeatedBoss;          // 本场是否击杀了 BOSS
    private bool skillTaken;
    private bool potionTaken;
    private bool relicTaken;
    private bool statTaken;

    // ---------------- UI 引用 ----------------
    private Canvas canvas;
    private GameObject panelRoot;      // 整个结束界面（遮罩 + 中央框）
    private GameObject subPanel;       // 子选择层（技能三选一 / 遗物三选一）
    private Text skillOptionLabel;
    private Text potionOptionLabel;
    private Text relicOptionLabel;
    private Text statOptionLabel;
    private Button skillOptionButton;
    private Button potionOptionButton;
    private Button relicOptionButton;
    private Button statOptionButton;

    private Font font;
    private LootSystem loot;
    private PlayerUnit Player => FindObjectOfType<PlayerUnit>();

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

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");

#if UNITY_EDITOR
        const string uiDir = "Assets/Game/Art/UI/";
        if (frameSprite == null) frameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "一个UI框_2.png");
        if (titleBarSprite == null) titleBarSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "深蓝色长条_1.png");
        if (buttonSprite == null) buttonSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_1.png");
        if (buttonHoverSprite == null) buttonHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色按键_2.png");
        if (arrowSprite == null) arrowSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色箭头_1.png");
        if (arrowHoverSprite == null) arrowHoverSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "淡蓝色箭头_2.png");
        if (cardFaceSprite == null) cardFaceSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(uiDir + "空技能卡面.png");
#endif

        // 本场的精英 / BOSS 击杀记录（UnitDied 在敌人死亡瞬间广播）
        EventManager.AddListener<Unit>("UnitDied", HandleUnitDied);
        // 战斗结束：胜利弹奖励界面，失败清空跨关缓存
        EventManager.AddListener<GameState>("GameStateChanged", HandleGameStateChanged);

        // 药水 / 遗物改由本界面领取：关掉 LootSystem 的两条自动发放
        // （运行时改字段不影响场景序列化的默认值）
        loot = FindObjectOfType<LootSystem>();
        if (loot != null)
        {
            loot.autoAddPotionToBag = false;      // 药水：改为面板"药水"按钮领取
            loot.autoGrantRelicOnDeath = false;   // 遗物：改为"捡起思维碎片"三选一
        }

        BuildCanvas();
    }

    private void OnDestroy()
    {
        EventManager.RemoveListener<Unit>("UnitDied", HandleUnitDied);
        EventManager.RemoveListener<GameState>("GameStateChanged", HandleGameStateChanged);
        if (Instance == this) Instance = null;
    }

    private void BuildCanvas()
    {
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        canvas = new GameObject("BattleEndCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;   // 盖在 BattleHUD(500) 之上
        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        // 面板整体先建好、默认隐藏，胜利时再打开
        BuildPanel();
        panelRoot.SetActive(false);
    }

    // ============================================================
    //                  战斗结束事件 / 击杀记录
    // ============================================================

    private void HandleUnitDied(Unit dead)
    {
        EnemyUnit enemy = dead as EnemyUnit;
        if (enemy == null || enemy.data == null) return;
        if (enemy.data.grade == UnitGrade.精英怪) defeatedElite = true;
        if (enemy.data.grade == UnitGrade.BOSS) defeatedBoss = true;
    }

    private void HandleGameStateChanged(GameState state)
    {
        if (state == GameState.Victory)
        {
            // LootSystem 也监听同一事件，事件顺序不保证——
            // 延迟一帧再打开，确保 LastBattlePotionDrop 已经掷完
            StartCoroutine(OpenNextFrame());
        }
        else if (state == GameState.Defeat)
        {
            PlayerRunState.Reset();   // 打输了，奖励清零重来
        }
    }

    private IEnumerator OpenNextFrame()
    {
        yield return null;
        Open();
    }

    /// <summary>打开战斗结束奖励界面：收集本场奖励 → 生成按钮列表</summary>
    private void Open()
    {
        pendingPotion = loot != null ? loot.LastBattlePotionDrop : null;

        // 思维碎片的三件候选提前抽好：按钮左侧要显示图标，点开时也复用同一批，避免两次抽到不同遗物
        pendingRelicChoices.Clear();
        if (defeatedElite || defeatedBoss)
        {
            RelicPool pool = defeatedBoss ? RelicPool.BOSS : RelicPool.精英;
            if (loot != null) pendingRelicChoices.AddRange(loot.RollRelicCandidates(pool, 3));
        }

        skillTaken = potionTaken = relicTaken = statTaken = false;

        BuildOptions();
        panelRoot.SetActive(true);
    }

    // ============================================================
    //                       界面构建
    // ============================================================

    private void BuildPanel()
    {
        panelRoot = new GameObject("BattleEndRoot", typeof(RectTransform));
        panelRoot.transform.SetParent(canvas.transform, false);
        Stretch(panelRoot.GetComponent<RectTransform>());

        // ---- 遮罩：只留顶部深蓝长条（150 高），长条以下全部变灰且不可交互 ----
        // 耐力槽 / 思绪槽挂在长条下方（顶边 y=-165），所以也会被这层遮罩盖住
        Image dim = panelRoot.AddComponent<Image>();
        dim.color = new Color(0.02f, 0.03f, 0.05f, 0.9f);   // 压得很暗：战场细节基本看不清
        dim.raycastTarget = true;   // 挡住下层 HUD 与世界点击
        RectTransform dimRt = dim.rectTransform;
        dimRt.anchorMin = new Vector2(0f, 0f);
        dimRt.anchorMax = new Vector2(1f, 1f);
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = new Vector2(0f, -statusBarH);

        // ---- 中央大框：一个UI框_2（整个屏幕正中） ----
        float frameW = frameH * frameAspect;
        Image frame = NewImage(panelRoot.transform, "Frame", frameSprite, Color.white);
        SetCenterAnchor(frame.rectTransform);
        frame.rectTransform.anchoredPosition = Vector2.zero;
        frame.rectTransform.sizeDelta = new Vector2(frameW, frameH);

        // ---- 标题条：深蓝色长条_1 + "战斗所得" ----
        const float titleW = 430f;
        float titleH = titleW / titleBarAspect;
        Image bar = NewImage(frame.transform, "TitleBar", titleBarSprite, Color.white);
        SetTopCenter(bar.rectTransform);
        bar.rectTransform.anchoredPosition = new Vector2(0f, -58f);
        bar.rectTransform.sizeDelta = new Vector2(titleW, titleH);
        Text title = NewText(bar.transform, "战斗所得", 34, Color.white);
        Stretch(title.rectTransform);

        // ---- 屏幕右下角：跳过奖励箭头（右缘距屏幕右边界 10px、底边 86px，与"下一回合"按钮同一角落） ----
        // 素材尖头朝上：本地 y 轴（长度方向）旋转后映射到屏幕 x 轴，所以 sizeDelta=(厚, 长)，
        // 顺时针转 90° 后尖头朝右。厚度比"下一回合"按钮加粗一倍（62→124），底边仍对齐 86px。
        // 绕中心旋转避免 pivot 在角上时漂移。
        const float skipLen = 251f;                    // 箭头长度（旋转后为屏幕宽度）
        const float skipThick = 124f;                  // 箭头厚度（旋转后为屏幕高度，已加倍）
        const float skipRight = 10f;                   // 右距：与"下一回合"按钮一致
        const float skipBottom = 86f;                  // 底距：与"下一回合"按钮一致（箭头向上加宽）
        Button skip = NewImageButton(panelRoot.transform, "SkipButton", arrowSprite,
            new Vector2(1f, 0f), Vector2.zero, new Vector2(skipThick, skipLen), _ => NextLevel(), 0);
        RectTransform skipRt = skip.GetComponent<RectTransform>();
        skipRt.pivot = new Vector2(0.5f, 0.5f);
        Vector2 skipCenter = new Vector2(-(skipRight + skipLen / 2f), skipBottom + skipThick / 2f);
        skipRt.anchoredPosition = skipCenter;
        skipRt.localRotation = Quaternion.Euler(0f, 0f, -90f);   // 顺时针 90°：尖头朝右
        ApplyHoverSpriteSwap(skip, arrowHoverSprite);

        // "跳过奖励"黑字：独立于箭头（不跟着转），压在箭头实体上（右侧是尖端，文字略偏左）
        Text skipText = NewText(panelRoot.transform, "跳过奖励", 19, Color.black);
        RectTransform textRt = skipText.rectTransform;
        textRt.anchorMin = new Vector2(1f, 0f);
        textRt.anchorMax = new Vector2(1f, 0f);
        textRt.pivot = new Vector2(0.5f, 0.5f);
        textRt.anchoredPosition = skipCenter + new Vector2(-6f, 0f);
        textRt.sizeDelta = new Vector2(skipLen - 20f, 30f);
    }

    /// <summary>按奖励可用性生成选项按钮（每次打开重建，保证状态干净）</summary>
    private void BuildOptions()
    {
        // 清掉上一次的选项（Frame 是 panelRoot 唯一的子物体；跳过箭头是 Frame 的子物体，保留）
        Transform frame = panelRoot.transform.GetChild(0);
        for (int i = frame.childCount - 1; i >= 0; i--)
        {
            Transform child = frame.GetChild(i);
            if (child.name.StartsWith("Option_")) Destroy(child.gameObject);
        }

        skillOptionLabel = potionOptionLabel = relicOptionLabel = statOptionLabel = null;
        skillOptionButton = potionOptionButton = relicOptionButton = statOptionButton = null;

        const float optionW = 440f;
        float optionH = optionW / buttonAspect;

        // 第一个按钮上沿：标题条下沿再留 24px
        float titleBottom = 58f + (430f / titleBarAspect) / 2f;   // 标题条下沿到框顶
        float y = titleBottom + 24f + optionH / 2f;
        int index = 0;

        // 技能选择按钮不放立绘：文字居中（其余选项仍可带左侧图标）
        MakeOption(frame, ref index, ref y, optionW, optionH, OptionSkillLabel(), null,
            OnSkillOptionClicked, out skillOptionButton, out skillOptionLabel);

        if (pendingPotion != null)
            MakeOption(frame, ref index, ref y, optionW, optionH, OptionPotionLabel(), pendingPotion.icon,
                OnPotionOptionClicked, out potionOptionButton, out potionOptionLabel);

        if (defeatedElite || defeatedBoss)
        {
            // 思维碎片按钮左侧放一件候选遗物的图标（候选在 Open 时已抽好，点开还是同样三件）
            Sprite relicIcon = pendingRelicChoices.Count > 0 ? pendingRelicChoices[0].icon : null;
            MakeOption(frame, ref index, ref y, optionW, optionH, "捡起思维碎片", relicIcon,
                OnRelicOptionClicked, out relicOptionButton, out relicOptionLabel);
            MakeOption(frame, ref index, ref y, optionW, optionH, OptionStatLabel(), null,
                OnStatOptionClicked, out statOptionButton, out statOptionLabel);
        }
    }

    private string OptionSkillLabel()
    {
        PlayerCardSystem cards = FindObjectOfType<PlayerCardSystem>();
        int poolCount = cards != null ? cards.attackPool.Count + cards.defensePool.Count : 0;
        return poolCount > 0 ? "进行 1 次技能选择" : "进行 1 次技能选择（牌库为空）";
    }

    private string OptionPotionLabel() =>
        potionTaken ? "药水 已领取" : "药水：【" + pendingPotion.potionName + "】";

    private string OptionStatLabel()
    {
        int bonus = defeatedBoss ? 5 : 2;
        return statTaken ? "耐力/思绪上限 +" + bonus + " 已获得" : "增加 " + bonus + " 点耐力和思绪上限与每回合回复量";
    }

    /// <summary>生成一个奖励按钮：淡蓝色按键底图 + 左侧美术图标 + 深蓝文字 + 悬停换图</summary>
    private void MakeOption(Transform frame, ref int index, ref float y,
        float w, float h, string label, Sprite icon, UnityAction action,
        out Button button, out Text labelText)
    {
        Button btn = NewImageButton(frame, "Option_" + index, buttonSprite,
            new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(w, h), _ => action(), 0);
        ApplyHoverSpriteSwap(btn, buttonHoverSprite);

        // 左侧图标（药水 / 遗物 / 技能卡面）：等比缩放不拉伸；文字整体右移让位
        if (icon != null)
        {
            Image ic = NewImage(btn.transform, "Icon", icon, Color.white);
            RectTransform ir = ic.rectTransform;
            ir.anchorMin = new Vector2(0f, 0.5f);
            ir.anchorMax = new Vector2(0f, 0.5f);
            ir.pivot = new Vector2(0f, 0.5f);
            ir.anchoredPosition = new Vector2(16f, 0f);
            ir.sizeDelta = new Vector2(h - 24f, h - 24f);
            ic.preserveAspect = true;
        }

        labelText = NewText(btn.transform, label, 26, new Color(0.14f, 0.22f, 0.36f));
        RectTransform lr = labelText.rectTransform;
        Stretch(lr);
        lr.offsetMin = new Vector2(icon != null ? h - 4f : 12f, 6f);   // 有图标时让开图标宽度
        lr.offsetMax = new Vector2(-12f, -6f);
        // 长文案（"增加 N 点耐力和思绪上限与每回合回复量"）自动换行成两行
        labelText.horizontalOverflow = HorizontalWrapMode.Wrap;

        button = btn;
        index++;
        y += h + optionGap;
    }

    // ============================================================
    //                  选项 ①：进行 1 次技能选择
    // ============================================================

    private void OnSkillOptionClicked()
    {
        if (skillTaken) return;
        if (subPanel != null) return;   // 子选择层已打开

        PlayerCardSystem cards = FindObjectOfType<PlayerCardSystem>();
        if (cards == null) return;

        // 攻击 + 防御牌库合并，按抽卡同一套资格规则过滤，随机取 3 张
        List<CardData> pool = new List<CardData>();
        foreach (CardData c in cards.attackPool)
            if (CardPoolRules.IsOfferEligible(c, CardPool.攻击)) pool.Add(c);
        foreach (CardData c in cards.defensePool)
            if (CardPoolRules.IsOfferEligible(c, CardPool.防御)) pool.Add(c);
        if (pool.Count == 0)
        {
            Debug.LogWarning("[BattleEndPanel] 攻击/防御牌库为空，技能选择不可用");
            return;
        }
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        int count = Mathf.Min(3, pool.Count);
        List<CardData> choices = pool.GetRange(0, count);

        ShowCardChoice("选择 1 个技能加入卡池", choices, card =>
        {
            skillTaken = true;
            PlayerRunState.ExtraCards.Add(card);
            PlayerCardSystem sys = FindObjectOfType<PlayerCardSystem>();
            // 加入对应抽卡池（攻击 / 防御）：以后抽卡时就有机会抽到它
            bool added = AddCardToPool(sys, card);
            CloseSubPanel();
            if (skillOptionLabel != null)
                skillOptionLabel.text = added ? "技能已加入卡池" : "技能已加入卡池（原本已有）";
            if (skillOptionButton != null) skillOptionButton.interactable = false;
            Debug.Log($"[战斗奖励] 技能选择：获得【{card.cardName}】，已加入{card.library}卡池");
        });
    }

    /// <summary>把奖励技能塞进对应的抽卡池；池里已有同一张卡就不重复加。返回是否真的新增。</summary>
    private static bool AddCardToPool(PlayerCardSystem sys, CardData card)
    {
        if (sys == null || card == null) return false;
        List<CardData> pool = card.library == CardPool.防御 ? sys.defensePool : sys.attackPool;
        if (pool == null) return false;
        if (pool.Contains(card)) return false;
        pool.Add(card);
        return true;
    }

    // ============================================================
    //                  选项 ②：药水
    // ============================================================

    private void OnPotionOptionClicked()
    {
        if (potionTaken || pendingPotion == null) return;

        PlayerUnit player = Player;
        if (player == null) return;

        if (player.AddPotion(pendingPotion))
        {
            potionTaken = true;
            PlayerRunState.Potions.Add(pendingPotion);
            Debug.Log($"[战斗奖励] 领取药水【{pendingPotion.potionName}】");
            if (potionOptionLabel != null) potionOptionLabel.text = "药水 已领取";
            if (potionOptionButton != null) potionOptionButton.interactable = false;
        }
        else
        {
            if (potionOptionLabel != null) potionOptionLabel.text = "药水 背包已满";
        }
    }

    // ============================================================
    //            选项 ③：捡起思维碎片（遗物三选一）
    // ============================================================

    private void OnRelicOptionClicked()
    {
        if (relicTaken) return;
        if (subPanel != null) return;

        List<RelicData> candidates = pendingRelicChoices;
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[BattleEndPanel] 遗物池为空（或都已持有），思维碎片不可用");
            return;
        }

        ShowRelicChoice("捡起思维碎片：选择 1 件遗物", candidates, relic =>
        {
            relicTaken = true;
            PlayerUnit player = Player;
            if (player != null) player.AddRelic(relic);
            PlayerRunState.Relics.Add(relic);
            CloseSubPanel();
            if (relicOptionLabel != null) relicOptionLabel.text = "思维碎片 已领取";
            if (relicOptionButton != null) relicOptionButton.interactable = false;
            Debug.Log($"[战斗奖励] 思维碎片：获得遗物【{relic.relicName}】");
        });
    }

    // ============================================================
    //      选项 ④：耐力/思绪上限与每回合回复量（精英+2 / BOSS+5）
    // ============================================================

    private void OnStatOptionClicked()
    {
        if (statTaken) return;

        PlayerUnit player = Player;
        if (player == null) return;

        int bonus = defeatedBoss ? 5 : 2;
        player.maxStamina += bonus;
        player.maxThought += bonus;
        player.staminaRegenPerTurnEnd += bonus;
        player.thoughtRegenPerTurnEnd += bonus;
        // 当前值也补上增量（Initialize 重置过）
        player.RestoreStamina(bonus);
        player.RestoreThought(bonus);

        statTaken = true;
        PlayerRunState.BonusStamina += bonus;
        PlayerRunState.BonusThought += bonus;

        if (statOptionLabel != null) statOptionLabel.text = OptionStatLabel();
        if (statOptionButton != null) statOptionButton.interactable = false;
        Debug.Log($"[战斗奖励] 耐力/思绪上限与每回合回复量 +{bonus}");
    }

    // ============================================================
    //                  子选择层：技能 / 遗物 三选一
    // ============================================================

    private void ShowCardChoice(string title, List<CardData> choices, UnityAction<CardData> onPick)
    {
        subPanel = new GameObject("SubPanel", typeof(RectTransform));
        subPanel.transform.SetParent(panelRoot.transform, false);
        Stretch(subPanel.GetComponent<RectTransform>());
        Image dim = subPanel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);
        dim.raycastTarget = true;

        Text titleT = NewText(subPanel.transform, title, 32, Color.white);
        SetTopCenter(titleT.rectTransform);
        titleT.rectTransform.anchoredPosition = new Vector2(0f, -80f);
        titleT.rectTransform.sizeDelta = new Vector2(900f, 60f);

        // 卡牌显示与抽卡界面（BattleHUD.MakeChoiceCard）完全一致：
        // 空技能卡面 320 宽、按素材比例定高（不拉伸），黑字写进卡面下方浅灰文字区。
        const float w = 320f, gap = 40f;
        float h = w * 495f / 379f;                 // ≈418
        for (int i = 0; i < choices.Count; i++)
        {
            CardData card = choices[i];
            if (card == null) continue;
            CardData captured = card;
            float x = (i - (choices.Count - 1) / 2f) * (w + gap);

            Button b = NewImageButton(subPanel.transform, "Card_" + i, cardFaceSprite,
                new Vector2(0.5f, 0.5f), new Vector2(x, 20f), new Vector2(w, h),
                _ => onPick(captured), 0);
            if (cardFaceSprite == null)
                b.image.color = new Color(0.15f, 0.19f, 0.27f, 0.98f);   // 素材缺失时兜底成深色块

            // 卡名：卡面浅灰文字区上沿
            Text nameT = NewFaceText(b.transform, "【" + card.cardName + "】", 28, Color.black,
                new Vector2(0.5f, 0.45f), new Vector2(0.5f, 0.45f),
                new Vector2(w * 0.86f, 34f), TextAnchor.MiddleCenter);
            nameT.horizontalOverflow = HorizontalWrapMode.Wrap;

            // 消耗
            NewFaceText(b.transform, "思绪 " + card.thoughtCost + " · 耐力 " + card.actionCost,
                19, Color.black, new Vector2(0.5f, 0.385f), new Vector2(0.5f, 0.385f),
                new Vector2(w * 0.86f, 26f), TextAnchor.MiddleCenter);

            // 技能介绍：从 35% 高处向下排，超出部分截断
            Text desc = NewFaceText(b.transform, card.description, 16, Color.black,
                new Vector2(0.5f, 0.35f), new Vector2(0.5f, 1f),
                new Vector2(w * 0.86f, h * 0.29f), TextAnchor.UpperCenter);
            desc.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }

    /// <summary>卡面上的文字：按卡面归一化锚点定位（anchor.y 越大越靠上），全部黑字</summary>
    private Text NewFaceText(Transform parent, string content, int size, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 sizeDelta, TextAnchor align)
    {
        Text t = NewText(parent, content, size, color);
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = sizeDelta;
        return t;
    }

    private void ShowRelicChoice(string title, List<RelicData> candidates, UnityAction<RelicData> onPick)
    {
        subPanel = new GameObject("SubPanel", typeof(RectTransform));
        subPanel.transform.SetParent(panelRoot.transform, false);
        Stretch(subPanel.GetComponent<RectTransform>());
        Image dim = subPanel.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        Text titleT = NewText(subPanel.transform, title, 32, Color.white);
        SetTopCenter(titleT.rectTransform);
        titleT.rectTransform.anchoredPosition = new Vector2(0f, -80f);
        titleT.rectTransform.sizeDelta = new Vector2(900f, 60f);

        const float w = 300f, h = 400f, gap = 36f;
        for (int i = 0; i < candidates.Count; i++)
        {
            RelicData relic = candidates[i];
            RelicData captured = relic;
            float x = (i - (candidates.Count - 1) / 2f) * (w + gap);

            Button b = NewImageButton(subPanel.transform, "Relic_" + i, null,
                new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(w, h),
                _ => onPick(captured), 0);
            b.image.color = new Color(0.16f, 0.2f, 0.28f, 0.98f);

            // 遗物图标（图像下方的名称 / 效果说明）
            if (relic.icon != null)
            {
                Image icon = NewImage(b.transform, "Icon", relic.icon, Color.white);
                SetTopCenter(icon.rectTransform);
                icon.rectTransform.anchoredPosition = new Vector2(0f, -22f);
                icon.rectTransform.sizeDelta = new Vector2(160f, 160f);
            }

            Text nameT = NewText(b.transform, relic.relicName, 26, Color.white);
            SetTopCenter(nameT.rectTransform);
            nameT.rectTransform.anchoredPosition = new Vector2(0f, -196f);
            nameT.rectTransform.sizeDelta = new Vector2(w - 24f, 40f);

            Text descT = NewText(b.transform, relic.description, 17, new Color(0.88f, 0.9f, 0.95f));
            SetTopCenter(descT.rectTransform);
            descT.rectTransform.anchoredPosition = new Vector2(0f, -240f);
            descT.rectTransform.sizeDelta = new Vector2(w - 30f, h - 260f);
            descT.horizontalOverflow = HorizontalWrapMode.Wrap;
            descT.verticalOverflow = VerticalWrapMode.Truncate;
        }
    }

    private void CloseSubPanel()
    {
        if (subPanel != null) Destroy(subPanel);
        subPanel = null;
    }

    // ============================================================
    //                  跳过奖励 → 下一关卡
    // ============================================================

    private void NextLevel()
    {
        LevelManager lm = FindObjectOfType<LevelManager>();
        LevelData next = lm != null && lm.level != null ? lm.level.nextLevel : null;

        // LevelManager.Awake 会取走这个静态引用；没配下一关时保持 null = 重打本关
        LevelManager.pendingNextLevel = next;

        Debug.Log($"[战斗奖励] 跳过奖励，进入下一关：{(next != null ? next.levelName : "（未配置，重打本关）")}");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ============================================================
    //                  UGUI 小工具（与 BattleHUD 同款）
    // ============================================================

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetCenterAnchor(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void SetTopCenter(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private Image NewImage(Transform parent, string name, Sprite sprite, Color color)
    {
        Image img = new GameObject(name).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;   // 装饰图默认不挡点击；Button 的底图由调用方再打开
        return img;
    }

    private Text NewText(Transform parent, string content, int size, Color color)
    {
        Text t = new GameObject("Text").AddComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return t;
    }

    private Button NewImageButton(Transform parent, string name, Sprite sprite,
        Vector2 anchor, Vector2 anchoredPosition, Vector2 size,
        UnityAction<int> onClick, int arg)
    {
        Image img = new GameObject(name).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.sprite = sprite;
        img.color = sprite != null ? Color.white : new Color(0.75f, 0.8f, 0.9f, 0.95f);
        img.raycastTarget = true;   // 按钮底图必须接收射线

        RectTransform rt = img.rectTransform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        Button btn = img.gameObject.AddComponent<Button>();
        if (onClick != null)
            btn.onClick.AddListener(() => onClick(arg));
        return btn;
    }

    /// <summary>SpriteSwap 过渡：鼠标悬停 / 按下时整张换成 hover 图</summary>
    private static void ApplyHoverSpriteSwap(Button btn, Sprite hover)
    {
        if (btn == null || hover == null) return;
        btn.transition = Selectable.Transition.SpriteSwap;
        SpriteState st = btn.spriteState;
        st.highlightedSprite = hover;
        st.pressedSprite = hover;
        btn.spriteState = st;
    }
}
