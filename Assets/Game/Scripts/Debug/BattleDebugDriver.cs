using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// ═══════════════════════════════════════════════════════════════
//  没有 UI 时的"最小可玩驱动"
//
//  项目里回合循环、伤害/buff、卡牌结算、寻路、胜负判定都是完整的，
//  但没有任何代码调用玩家侧入口（BeginDraw / ConfirmInfuse /
//  UseSlotCard / TryMoveTo / TryAttack / RequestEndPlayerTurn），
//  所以开战后会永久停在"玩家行动"阶段。
//
//  这个脚本补的就是这一层：把键盘和鼠标翻译成已有的公开方法，
//  再叠一层 IMGUI 状态面板（血量/耐力/思绪/槽位/敌方预告/胜负），
//  让人能真的把第 1 关打完。
//
//  为什么用 IMGUI 而不是 Canvas：
//    项目里没有 Canvas / EventSystem / TextMeshPro 场景对象，
//    用 OnGUI 不需要往场景里加任何东西，脚本自己就能起来。
//
//  为什么不改场景 YAML：
//    用 [RuntimeInitializeOnLoadMethod] 自举，场景里存在 LevelManager
//    时才创建这个物体——不改 Battle.unity，不影响队友的编辑器操作。
//
//  操作（游戏里按 F1 随时查看）：
//    左键点空格子   → 移动（自动寻路，按路径扣耐力）
//    左键点敌人     → 选中槽位则对该敌人出牌，没选中则普通攻击（需相邻）
//    左键点自己     → 对自身使用当前槽位的防御/增益牌
//    左键点障碍物   → 对障碍物使用当前槽位的攻击牌
//    1 / 2          → 从攻击 / 防御牌库抽卡（出现 3 张待选）
//    1 / 2 / 3      → 选牌阶段：灌注第 1/2/3 张（0 或退格放弃）
//    Q W E R T      → 选中第 1~5 号槽位（再按一次取消）
//    空格           → 结束回合
//    3              → 普通攻击最近的敌人
//    G              → 调试：补满耐力/思绪
//    F1 / F2        → 开关帮助 / 开关整个面板
//    R              → 战斗结束后重开本场景
//
//  挂载位置：不需要手动挂，运行时自动创建（名字 "~BattleDebugDriver"）。
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// 战斗调试驱动：键鼠操作 + IMGUI 状态面板。
/// 只在"没有 UI 的 demo 阶段"使用，正式 UI 做好后可以直接删掉这个文件。
/// </summary>
public class BattleDebugDriver : MonoBehaviour
{
    [Header("显示")]
    [Tooltip("关闭后只剩键鼠操作，不画任何界面（游戏中按 F2 也能切）")]
    public bool showOverlay = true;

    [Tooltip("关闭后不显示左下角的操作说明（游戏中按 F1 也能切）")]
    public bool showControlHelp = true;

    // ============================================================
    //                       运行时引用
    // ============================================================

    private LevelManager level;
    private PlayerUnit player;
    private PlayerCardSystem cardSystem;
    private TurnManager turnManager;
    private GameManager game;
    private Camera cam;

    private const int MaxSlots = 5;
    private static readonly KeyCode[] SlotKeys =
    {
        KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T
    };

    // ============================================================
    //                       交互状态
    // ============================================================

    private int selectedSlot = -1;     // 当前选中的槽位，-1 = 没选（此时点敌人走普通攻击）
    private string tip = "";           // 底部提示文字
    private float tipUntil;            // 提示显示到什么时候（Time.unscaledTime）
    private float highlightAge;        // 高亮还有多久刷新
    private float refRefreshTimer;     // 引用重查的节流计时
    private bool highlightDirty = true;

    // 缓存下来的敌人列表，OnGUI 每帧都要用，避免每帧两次 FindObjectsOfType
    private readonly List<EnemyUnit> enemyBuffer = new List<EnemyUnit>();

    // IMGUI 样式（第一次 OnGUI 时建）
    private Font uiFont;
    private GUIStyle labelStyle;
    private GUIStyle smallStyle;
    private GUIStyle centerStyle;
    private GUIStyle titleStyle;
    private GUIStyle bigStyle;

    // ============================================================
    //                       自举入口
    // ============================================================

    /// <summary>
    /// 场景加载完成后自动创建。
    /// 只有场景里存在 LevelManager（也就是 Battle.unity）时才创建，
    /// 这样在 SampleScene / ConceptMap 里不会冒出一个空面板。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindObjectOfType<LevelManager>() == null) return;
        if (FindObjectOfType<BattleDebugDriver>() != null) return;

        GameObject go = new GameObject("~BattleDebugDriver");
        // 跨场景保留：按 R 重开时物体会被销毁，只有 DontDestroyOnLoad 才活得下来，
        // 重开后靠 Update 里的 RefreshRefs 重新抓到新的玩家/关卡引用。
        DontDestroyOnLoad(go);
        go.AddComponent<BattleDebugDriver>();
    }

    private void Start()
    {
        RefreshRefs();
        Say("点格子移动 · 点敌人出牌 · 1抽攻击 2抽防御 · QWERT选槽位 · 空格结束回合 · F1帮助");
    }

    private void OnDisable()
    {
        // 面板销毁时把格子颜色还原，免得留一地高亮
        if (HexGrid.Instance != null)
            HexGrid.Instance.ClearAllHighlights();
    }

    // ============================================================
    //                     每帧：输入
    // ============================================================

    private void Update()
    {
        // 玩家是 LevelManager 在 Start 里生成的，重开场景后旧引用也会失效，
        // 所以没引用时节流重查（每 0.3 秒一次，不要每帧 FindObjectOfType）
        if (player == null || cardSystem == null || level == null)
        {
            refRefreshTimer -= Time.deltaTime;
            if (refRefreshTimer <= 0f)
            {
                refRefreshTimer = 0.3f;
                RefreshRefs();
            }
        }

        // 不在战斗场景（比如切到 SampleScene）时什么都不做
        if (level == null) return;

        // ---------- 战斗结束：只留"重开" ----------
        if (IsBattleOver)
        {
            if (Input.GetKeyDown(KeyCode.R))
                RestartBattle();
            UpdateHighlights();
            return;
        }

        if (Input.GetKeyDown(KeyCode.F1)) showControlHelp = !showControlHelp;
        if (Input.GetKeyDown(KeyCode.F2)) showOverlay = !showOverlay;

        if (player == null || cardSystem == null) return;

        // ---------- 正在选牌：只接受 1/2/3 和 0 ----------
        if (cardSystem.IsChoosing)
        {
            HandleChooseKeys();
            UpdateHighlights();
            return;
        }

        // ---------- 常规操作 ----------
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            bool ok = cardSystem.BeginDraw(CardPool.攻击);
            Say(ok ? "抽攻击牌：用 1 / 2 / 3 选一张，0 放弃" : "抽攻击牌失败（阶段不对 / 槽位满 / 思绪不足，详见 Console）");
            MarkDirty();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            bool ok = cardSystem.BeginDraw(CardPool.防御);
            Say(ok ? "抽防御牌：用 1 / 2 / 3 选一张，0 放弃" : "抽防御牌失败（阶段不对 / 槽位满 / 思绪不足，详见 Console）");
            MarkDirty();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            DoBasicAttackNearest();
        }
        else if (Input.GetKeyDown(KeyCode.G))
        {
            player.RestoreAllResources();
            Say("调试：耐力 / 思绪已补满");
            MarkDirty();
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            DoEndTurn();
        }

        // 槽位选择：Q W E R T
        for (int i = 0; i < SlotKeys.Length && i < MaxSlots; i++)
        {
            if (Input.GetKeyDown(SlotKeys[i]))
                SelectSlot(i);
        }

        HandleMouse();
        UpdateHighlights();
    }

    /// <summary>选牌阶段的按键：1/2/3 灌注，0 或退格放弃。</summary>
    private void HandleChooseKeys()
    {
        int count = cardSystem.CurrentChoices.Count;

        for (int i = 0; i < 3; i++)
        {
            KeyCode key = i == 0 ? KeyCode.Alpha1 : (i == 1 ? KeyCode.Alpha2 : KeyCode.Alpha3);
            if (!Input.GetKeyDown(key)) continue;

            if (i >= count)
            {
                Say("没有第 " + (i + 1) + " 张待选牌");
                return;
            }

            // 先记下卡名：ConfirmInfuse 成功后 currentChoices 会被清空
            CardData picked = cardSystem.CurrentChoices[i];
            string pickedName = picked != null ? picked.cardName : "?";

            bool ok = cardSystem.ConfirmInfuse(i);
            Say(ok ? "灌注成功：【" + pickedName + "】" : "灌注失败（思绪不足，换一张便宜的试试）");
            MarkDirty();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Backspace))
        {
            cardSystem.AbandonInfuse();
            Say("放弃本次灌注（开抽的思绪不退）");
            MarkDirty();
        }
    }

    /// <summary>鼠标左键：点格子 → 移动 / 出牌 / 打障碍物。</summary>
    private void HandleMouse()
    {
        if (!InPlayerAction) return;
        if (cam == null) cam = Camera.main;
        if (cam == null || HexGrid.Instance == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
        HexCell cell = HexGrid.Instance.GetCell(HexGrid.Instance.WorldToHex(world));
        if (cell == null) return;   // 点到地图外面，忽略

        // ① 敌人 → 出牌 / 普通攻击
        EnemyUnit enemy = cell.occupyUnit as EnemyUnit;
        if (enemy != null && !enemy.IsDead)
        {
            ActOnEnemy(enemy);
            return;
        }

        // ② 自己 → 对自身使用当前槽位（防御/治疗/增益）
        if (cell.occupyUnit == (Unit)player)
        {
            if (selectedSlot < 0)
            {
                Say("先按 Q/W/E/R/T 选一个槽位，再点自己使用防御牌");
                return;
            }
            bool ok = cardSystem.UseSlotCard(selectedSlot, player);
            Say(ok ? "对自己使用【" + SlotName(selectedSlot) + "】" : "出牌失败（这张牌不能对自己用 / 资源不足）");
            AfterPlay();
            return;
        }

        // ③ 障碍物 → 用攻击牌打它
        if (cell.obstacle != null)
        {
            if (selectedSlot < 0)
            {
                Say("先按 Q/W/E/R/T 选一个槽位，再点障碍物");
                return;
            }
            bool ok = cardSystem.UseSlotCardOnObstacle(selectedSlot, cell.obstacle);
            Say(ok ? "对障碍物使用【" + SlotName(selectedSlot) + "】" : "出牌失败（这张牌不能打障碍物 / 射程不够）");
            AfterPlay();
            return;
        }

        // ④ 空格子 → 移动
        if (player.TryMoveTo(cell.coord))
        {
            Say("移动到 (" + cell.coord.q + "," + cell.coord.r + ")");
            MarkDirty();
        }
        else
        {
            Say("走不过去：耐力不足 / 被挡住 / 不在玩家行动阶段（原因见 Console）");
        }
    }

    /// <summary>点敌人：有选中槽位就出牌，否则普通攻击。</summary>
    private void ActOnEnemy(EnemyUnit enemy)
    {
        if (selectedSlot >= 0)
        {
            string name = SlotName(selectedSlot);
            bool ok = cardSystem.UseSlotCard(selectedSlot, enemy);
            Say(ok ? "对 " + enemy.data.unitName + " 使用【" + name + "】"
                   : "出牌失败：射程 / 视线 / 目标类型 / 资源不满足（详见 Console）");
            AfterPlay();
            return;
        }

        bool hit = player.TryAttack(enemy);
        Say(hit ? "普通攻击 " + enemy.data.unitName
                : "普通攻击失败：只能打相邻格，或耐力不足（也可以先按 QWERT 选一张牌）");
        MarkDirty();
    }

    /// <summary>普通攻击离自己最近的敌人（省得手动找相邻格）。</summary>
    private void DoBasicAttackNearest()
    {
        EnemyUnit nearest = null;
        int best = int.MaxValue;
        foreach (EnemyUnit e in FindObjectsOfType<EnemyUnit>())
        {
            if (e == null || e.IsDead) continue;
            int d = player.GetCoord().Distance(e.GetCoord());
            if (d < best) { best = d; nearest = e; }
        }

        if (nearest == null)
        {
            Say("场上没有敌人");
            return;
        }

        bool ok = player.TryAttack(nearest);
        Say(ok ? "普通攻击 " + nearest.data.unitName + "（距离 " + best + "）"
               : "普通攻击失败：要相邻才能打，当前距离 " + best);
        MarkDirty();
    }

    /// <summary>结束回合：交给 TurnManager 走"结算 buff → 敌人行动"的完整流程。</summary>
    private void DoEndTurn()
    {
        if (turnManager == null)
        {
            Say("场景里没有 TurnManager，无法切换回合");
            return;
        }

        if (!InPlayerAction)
        {
            Say("现在不是玩家行动阶段（" + turnManager.Phase + "）");
            return;
        }

        selectedSlot = -1;
        turnManager.RequestEndPlayerTurn();
        Say("结束回合，敌人开始行动");
        MarkDirty();
    }

    /// <summary>选中/取消选中一个槽位。</summary>
    private void SelectSlot(int index)
    {
        if (cardSystem == null || index < 0 || index >= cardSystem.Slots.Count)
            return;

        if (selectedSlot == index)
        {
            selectedSlot = -1;
            Say("取消选中槽位");
        }
        else if (cardSystem.Slots[index].IsEmpty)
        {
            selectedSlot = -1;
            Say("槽位 " + (index + 1) + " 是空的（按 1 或 2 抽卡灌注）");
        }
        else
        {
            selectedSlot = index;
            CardData card = cardSystem.Slots[index].card;
            Say("选中【" + card.cardName + "】 思绪 " + card.thoughtCost +
                " / 耐力 " + card.GetFinalActionCost(player) +
                " / 射程 " + card.minRange + "~" + card.GetEffectiveMaxRange(player));
        }

        MarkDirty();
    }

    /// <summary>出牌后收尾：牌可能因次数耗尽腾空槽位，选中状态要跟着失效。</summary>
    private void AfterPlay()
    {
        if (selectedSlot >= 0)
        {
            if (selectedSlot >= cardSystem.Slots.Count || cardSystem.Slots[selectedSlot].IsEmpty)
                selectedSlot = -1;
        }
        MarkDirty();
    }

    private void RestartBattle()
    {
        Scene active = SceneManager.GetActiveScene();
        if (active.buildIndex >= 0)
            SceneManager.LoadScene(active.buildIndex);
        else
            Debug.LogWarning("[BattleDebugDriver] 当前场景不在 Build Settings 里，无法重开");
    }

    // ============================================================
    //                     引用 / 状态查询
    // ============================================================

    private void RefreshRefs()
    {
        if (game == null) game = GameManager.Instance != null ? GameManager.Instance : FindObjectOfType<GameManager>();
        if (turnManager == null) turnManager = TurnManager.Instance != null ? TurnManager.Instance : FindObjectOfType<TurnManager>();
        if (level == null) level = FindObjectOfType<LevelManager>();
        if (cam == null) cam = Camera.main;

        if (level != null && level.Player != null)
        {
            player = level.Player;
            cardSystem = player.GetComponent<PlayerCardSystem>();
        }

        if (player == null) player = FindObjectOfType<PlayerUnit>();
        if (cardSystem == null && player != null) cardSystem = player.GetComponent<PlayerCardSystem>();
    }

    private bool InPlayerAction => turnManager != null && turnManager.Phase == TurnPhase.玩家行动;

    private bool IsBattleOver =>
        game != null && (game.CurrentState == GameState.Victory || game.CurrentState == GameState.Defeat);

    private string SlotName(int index)
    {
        if (cardSystem == null || index < 0 || index >= cardSystem.Slots.Count) return "?";
        CardData card = cardSystem.Slots[index].card;
        return card != null ? card.cardName : "空";
    }

    private void Say(string text)
    {
        tip = text;
        tipUntil = Time.unscaledTime + 4f;
    }

    private void MarkDirty() => highlightDirty = true;

    // ============================================================
    //                        格子高亮
    // ============================================================

    /// <summary>
    /// 选中槽位时高亮该牌的射程（粉红），没选槽位时高亮可移动范围（绿色）。
    /// 每 0.35 秒最多重算一次，避免每帧跑 BFS。
    /// </summary>
    private void UpdateHighlights()
    {
        if (HexGrid.Instance == null) return;

        highlightAge -= Time.deltaTime;
        if (!highlightDirty && highlightAge > 0f) return;

        highlightDirty = false;
        highlightAge = 0.35f;

        HexGrid.Instance.ClearAllHighlights();

        if (player == null || player.IsDead || !InPlayerAction) return;

        if (selectedSlot >= 0 && selectedSlot < cardSystem.Slots.Count && !cardSystem.Slots[selectedSlot].IsEmpty)
        {
            CardData card = cardSystem.Slots[selectedSlot].card;
            int maxRange = Mathf.Clamp(card.GetEffectiveMaxRange(player), 0, 12);
            HexCoord from = player.GetCoord();

            foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(from, maxRange))
            {
                if (cell.coord.Distance(from) < card.minRange) continue;
                cell.SetAttackRangeHighlight(true);
            }
            return;
        }

        // 没选牌：把能走到的格子涂绿（按当前耐力算，走不动的不涂）
        foreach (HexCoord coord in HexPathfinding.GetReachableCells(player.GetCoord(), player.CurrentStamina))
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            if (cell != null && cell.CanStopOn(player))
                cell.SetMoveRangeHighlight(true);
        }
    }

    // ============================================================
    //                        IMGUI 面板
    // ============================================================

    private void OnGUI()
    {
        if (!showOverlay || level == null) return;
        EnsureStyles();

        // 还没生成出玩家（LevelManager 正在布阵）时给个提示就返回
        if (player == null)
        {
            GUI.Label(new Rect(12f, 12f, 420f, 24f), "等待关卡布阵…", labelStyle);
            return;
        }

        DrawPlayerPanel();
        DrawSlotPanel();
        DrawEnemyPanel();
        DrawTipBar();

        if (cardSystem != null && cardSystem.IsChoosing)
            DrawChoosePanel();

        if (showControlHelp)
            DrawHelpPanel();

        if (IsBattleOver)
            DrawResultPanel();
    }

    private void DrawPlayerPanel()
    {
        Rect panel = new Rect(12f, 12f, 350f, 152f);
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 8f, 330f, 22f),
            "第 " + (game != null ? game.turnCount : 0) + " 回合 · " +
            (turnManager != null ? turnManager.Phase.ToString() : "无 TurnManager"), titleStyle);

        DrawBar(new Rect(panel.x + 10f, panel.y + 36f, 330f, 18f),
            player.GetMaxHealth() > 0 ? (float)player.GetCurrentHealth() / player.GetMaxHealth() : 0f,
            new Color(0.85f, 0.25f, 0.25f, 0.95f),
            "生命 " + player.GetCurrentHealth() + " / " + player.GetMaxHealth() +
            (player.GetShield() > 0 ? "  护盾 " + player.GetShield() : ""));

        DrawBar(new Rect(panel.x + 10f, panel.y + 58f, 330f, 16f),
            player.maxStamina > 0 ? (float)player.CurrentStamina / player.maxStamina : 0f,
            new Color(0.35f, 0.6f, 0.95f, 0.95f),
            "耐力 " + player.CurrentStamina + " / " + player.maxStamina);

        DrawBar(new Rect(panel.x + 10f, panel.y + 78f, 330f, 16f),
            player.maxThought > 0 ? (float)player.CurrentThought / player.maxThought : 0f,
            new Color(0.6f, 0.45f, 0.95f, 0.95f),
            "思绪 " + player.CurrentThought + " / " + player.maxThought);

        string pos = "位置 (" + player.GetCoord().q + "," + player.GetCoord().r + ")";
        if (player.IsDead) pos += "  <color=#ff6666>已阵亡</color>";
        GUI.Label(new Rect(panel.x + 10f, panel.y + 100f, 330f, 20f), pos, smallStyle);
        GUI.Label(new Rect(panel.x + 10f, panel.y + 120f, 330f, 20f),
            "选中的槽位：" + (selectedSlot >= 0 ? (selectedSlot + 1) + " 【" + SlotName(selectedSlot) + "】" : "无"), smallStyle);
    }

    private void DrawSlotPanel()
    {
        Rect panel = new Rect(12f, Screen.height - 260f, 300f, 190f);
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, 280f, 20f),
            "技能槽位（Q W E R T 选中）", titleStyle);

        for (int i = 0; i < MaxSlots; i++)
        {
            float y = panel.y + 30f + i * 30f;
            bool valid = cardSystem != null && i < cardSystem.Slots.Count;
            CardSlot slot = valid ? cardSystem.Slots[i] : null;

            if (i == selectedSlot)
            {
                GUI.color = new Color(0.25f, 0.5f, 0.85f, 0.55f);
                GUI.DrawTexture(new Rect(panel.x + 6f, y - 2f, panel.width - 12f, 26f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            string text;
            if (slot == null || slot.IsEmpty)
            {
                text = "[" + SlotKeys[i] + "] 空";
            }
            else
            {
                string uses = slot.RemainingForUI < 0 ? "∞" : slot.RemainingForUI.ToString();
                text = "[" + SlotKeys[i] + "] " + slot.card.cardName +
                       "   思绪" + slot.card.thoughtCost +
                       " 耐力" + slot.card.GetFinalActionCost(player) +
                       "   剩" + uses;
            }
            GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, 22f), text, smallStyle);
        }
    }

    private void DrawEnemyPanel()
    {
        CollectEnemies();

        float h = 40f + enemyBuffer.Count * 46f;
        Rect panel = new Rect(Screen.width - 330f - 12f, 12f, 330f, Mathf.Max(h, 60f));
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, 300f, 20f),
            "敌人（本回合预告）", titleStyle);

        if (enemyBuffer.Count == 0)
        {
            GUI.Label(new Rect(panel.x + 10f, panel.y + 30f, 300f, 22f), "场上没有敌人", smallStyle);
            return;
        }

        for (int i = 0; i < enemyBuffer.Count; i++)
        {
            EnemyUnit e = enemyBuffer[i];
            float y = panel.y + 30f + i * 46f;

            DrawBar(new Rect(panel.x + 10f, y, 200f, 15f),
                e.GetMaxHealth() > 0 ? (float)e.GetCurrentHealth() / e.GetMaxHealth() : 0f,
                new Color(0.8f, 0.3f, 0.3f, 0.9f),
                e.data.unitName + "  " + e.GetCurrentHealth() + "/" + e.GetMaxHealth());

            string intents = "";
            foreach (CardData card in e.PlannedSkills)
                intents += (intents.Length > 0 ? " + " : "") + card.cardName;
            if (string.IsNullOrEmpty(intents)) intents = "（没有预告）";

            GUI.Label(new Rect(panel.x + 216f, y - 2f, 110f, 20f),
                "d=" + player.GetCoord().Distance(e.GetCoord()), smallStyle);
            GUI.Label(new Rect(panel.x + 10f, y + 17f, 300f, 20f), "预告：" + intents, smallStyle);
        }
    }

    private void CollectEnemies()
    {
        enemyBuffer.Clear();
        foreach (EnemyUnit e in FindObjectsOfType<EnemyUnit>())
        {
            if (e != null && !e.IsDead)
                enemyBuffer.Add(e);
        }
    }

    private void DrawTipBar()
    {
        float width = Mathf.Min(Screen.width - 380f, 760f);
        if (width < 300f) width = Screen.width - 24f;

        Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height - 60f, width, 46f);
        DrawPanel(panel);

        // 刚操作过的提示优先；没有新提示时按当前处境给一句"该做什么"
        string text = Time.unscaledTime < tipUntil && !string.IsNullOrEmpty(tip)
            ? tip
            : DefaultHint();
        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, panel.width - 20f, 36f), text, centerStyle);
    }

    /// <summary>
    /// 没在显示操作反馈时，按当前阶段/资源状态给一句引导。
    /// 演示时最容易卡住的是"槽位全空不知道要抽牌"，所以这一条优先级最高。
    /// </summary>
    private string DefaultHint()
    {
        if (cardSystem != null && cardSystem.IsChoosing)
            return "按 1 / 2 / 3 选一张灌注到空槽位，0 放弃";

        if (turnManager != null)
        {
            switch (turnManager.Phase)
            {
                case TurnPhase.回合开始:
                    return "回合开始 · 正在展示敌方预告";
                case TurnPhase.Buff结算:
                    return "结算中…";
                case TurnPhase.敌方行动:
                    return "敌人行动中…";
                case TurnPhase.战斗结束:
                    return "战斗结束 · 按 R 重开";
            }
        }

        if (cardSystem != null)
        {
            bool hasCard = false;
            foreach (CardSlot slot in cardSystem.Slots)
            {
                if (!slot.IsEmpty) { hasCard = true; break; }
            }

            if (!hasCard)
                return "槽位是空的 —— 按 1 抽攻击牌 / 2 抽防御牌（开抽 5 思绪，选牌还要付牌面费用）";
        }

        if (player.CurrentStamina <= 0 && player.CurrentThought <= 0)
            return "耐力和思绪都空了 —— 按空格结束回合";

        return "左键点格子移动 / 点敌人出牌 · QWERT 选槽位 · 空格结束回合 · F1 帮助";
    }

    /// <summary>选牌弹窗：3 张待选牌，按 1/2/3 灌注，0 放弃。</summary>
    private void DrawChoosePanel()
    {
        IReadOnlyList<CardData> choices = cardSystem.CurrentChoices;
        float width = Mathf.Min(640f, Screen.width - 40f);
        float height = 90f + choices.Count * 56f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x + 14f, panel.y + 8f, width - 28f, 24f),
            "灌注选择（来源：" + cardSystem.Source + "）", titleStyle);

        for (int i = 0; i < choices.Count; i++)
        {
            CardData card = choices[i];
            float y = panel.y + 40f + i * 56f;

            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(new Rect(panel.x + 10f, y - 4f, width - 20f, 50f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string attackHint = card.IsAttack
                ? "伤害" + card.GetFinalDamage(player) + " 射程" + card.minRange + "~" + card.GetEffectiveMaxRange(player)
                : (card.defenseValue > 0 ? card.defenseEffectType + " " + card.defenseValue : "辅助");

            GUI.Label(new Rect(panel.x + 18f, y, width - 36f, 22f),
                "[" + (i + 1) + "] " + card.cardName + "   思绪" + card.thoughtCost +
                " 耐力" + card.GetFinalActionCost(player) + "   " + attackHint, labelStyle);
            GUI.Label(new Rect(panel.x + 18f, y + 20f, width - 36f, 22f), card.description, smallStyle);
        }

        GUI.Label(new Rect(panel.x + 14f, panel.y + height - 30f, width - 28f, 22f),
            "按 1 / 2 / 3 灌注其中一张（还要再付牌面思绪）；0 或退格全部放弃", smallStyle);
    }

    private void DrawHelpPanel()
    {
        float h = 176f;
        Rect panel = new Rect(12f, Screen.height - 260f - h - 8f, 300f, h);
        DrawPanel(panel);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, 280f, 20f), "操作（F1 关闭）", titleStyle);

        string[] lines =
        {
            "左键点格子：移动（自动寻路）",
            "左键点敌人：出牌 / 普通攻击",
            "左键点自己：对自己用防御牌",
            "1 / 2：抽攻击 / 防御牌",
            "选牌时 1/2/3 选，0 放弃",
            "Q W E R T：选中 1~5 号槽位",
            "3：普通攻击最近的敌人",
            "空格：结束回合",
            "G：补满耐力/思绪（调试）",
            "R：战斗结束后重开",
        };

        for (int i = 0; i < lines.Length; i++)
            GUI.Label(new Rect(panel.x + 12f, panel.y + 28f + i * 14.5f, 280f, 16f), lines[i], smallStyle);
    }

    private void DrawResultPanel()
    {
        bool win = game.CurrentState == GameState.Victory;
        float w = 420f, h = 130f;
        Rect panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f - 40f, w, h);
        DrawPanel(panel);

        bigStyle.normal.textColor = win ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.45f, 0.45f);
        GUI.Label(new Rect(panel.x, panel.y + 18f, w, 46f), win ? "胜  利" : "失  败", bigStyle);

        GUI.Label(new Rect(panel.x, panel.y + 70f, w, 24f),
            "共 " + (game != null ? game.turnCount : 0) + " 回合 · 按 R 重开本场景", centerStyle);
    }

    // ============================================================
    //                        IMGUI 小工具
    // ============================================================

    private void EnsureStyles()
    {
        if (labelStyle != null) return;

        if (uiFont == null)
        {
            // 默认的 Arial 不带中文字形，显式从系统字体里挑一个带中文的
            uiFont = Font.CreateDynamicFontFromOSFont(
                new string[] { "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun", "PingFang SC", "Arial Unicode MS", "Arial" },
                14);
        }
        if (uiFont != null) GUI.skin.font = uiFont;

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            richText = true,
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
        labelStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(labelStyle) { fontSize = 12 };
        smallStyle.normal.textColor = new Color(0.88f, 0.9f, 0.95f);

        centerStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };

        titleStyle = new GUIStyle(labelStyle) { fontSize = 13, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = new Color(1f, 0.9f, 0.6f);

        bigStyle = new GUIStyle(labelStyle)
        {
            fontSize = 34,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
    }

    private void DrawPanel(Rect rect)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    /// <summary>一条进度条 + 居中文字。</summary>
    private void DrawBar(Rect rect, float ratio, Color color, string text)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.14f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        GUI.color = color;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), Texture2D.whiteTexture);

        GUI.color = Color.white;
        GUI.Label(rect, text, centerStyle);
    }
}
