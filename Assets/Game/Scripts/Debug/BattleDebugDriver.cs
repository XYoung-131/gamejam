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
//  操作说明弹窗已移除：状态栏等 UI 由 BattleHUD 承载：
//    左键点空格子   → 移动（自动寻路，按路径扣耐力）
//    左键点敌人     → 选中槽位则对该敌人出牌，没选中则普通攻击（需相邻）
//    左键点自己     → 对自身使用当前槽位的防御/增益牌
//    左键点障碍物   → 对障碍物使用当前槽位的攻击牌
//    1 / 2          → 从攻击 / 防御牌库抽卡（出现 3 张待选）
//    1 / 2 / 3      → 选牌阶段：灌注第 1/2/3 张（0 或退格放弃）
//                     实际可按键数跟随 PlayerCardSystem.offerCount（默认 6，即 6 选 3）
//    Q W E R T      → 选中第 1~5 号槽位（再按一次取消）
//    空格           → 结束回合
//    3              → 普通攻击最近的敌人
//    G              → 调试：补满耐力/思绪
//    F2             → 开关整个调试面板
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

    // 选牌阶段的数字键。PlayerCardSystem.offerCount 默认是 6（规则里的"6 选 3"），
    // 所以这里必须准备到 6 个，只写 1/2/3 会让后 3 张牌看得到却按不动。
    private static readonly KeyCode[] ChooseKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
        KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
    };

    // ============================================================
    //                       交互状态
    // ============================================================

    private int selectedSlot = -1;     // 当前选中的槽位，-1 = 没选（此时点敌人走普通攻击）
    private float highlightAge;        // 高亮还有多久刷新
    private float refRefreshTimer;     // 引用重查的节流计时
    private bool highlightDirty = true;
    private bool paintedHighlight = false;   // 自己是否画过射程高亮（没画过就别清别人的）

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
        Say("抽卡请点左下角\"技能\"按钮 · QWERT选槽位 · 空格结束回合");
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

        if (Input.GetKeyDown(KeyCode.F2)) showOverlay = !showOverlay;

        if (player == null || cardSystem == null) return;

        // ---------- 正在选牌：只接受 1~N 和 0 ----------
        if (cardSystem.IsChoosing)
        {
            HandleChooseKeys();
            UpdateHighlights();
            return;
        }

        // ---------- 常规操作 ----------
        // 抽卡（原 1/2 键）已按需求移除：抽牌统一走 BattleHUD 的"技能"按钮 → 牌堆选择
        if (Input.GetKeyDown(KeyCode.Alpha3))
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

    /// <summary>选牌阶段的按键：1~N 灌注第 N 张，0 或退格放弃。</summary>
    private void HandleChooseKeys()
    {
        int count = cardSystem.CurrentChoices.Count;

        for (int i = 0; i < ChooseKeys.Length && i < count; i++)
        {
            if (!Input.GetKeyDown(ChooseKeys[i])) continue;

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

    /// <summary>
    /// 鼠标左键：点格子 → 出牌 / 打障碍物。
    /// 需求规则：常态（未选任何技能）禁止点击移动和普通攻击——行动只能通过
    /// 选中技能后进行（键盘选槽位，或 BattleHUD 的攻击/位移按钮）。
    /// </summary>
    private void HandleMouse()
    {
        // HUD 瞄准模式时让位：点击由 HUD 处理，避免双重结算
        if (BattleHUD.Instance != null && BattleHUD.Instance.IsAiming) return;

        // 常态不响应世界点击（不能再点格子直接走/平A）
        if (selectedSlot < 0) return;

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

    /// <summary>操作反馈：屏幕底部提示条已移除，统一写 Console</summary>
    private void Say(string text)
    {
        Debug.Log("[Battle] " + text);
    }

    private void MarkDirty() => highlightDirty = true;

    // ============================================================
    //                        格子高亮
    // ============================================================

    /// <summary>
    /// 选中槽位时高亮该牌的射程（粉红）；平时不高亮任何格子，也不清除别人的高亮。
    /// 注意：BattleHUD 的瞄准高亮是常亮的，这里没画过高亮就绝不能调 ClearAllHighlights，
    /// 否则会把 HUD 的常亮圈每 0.35 秒抹一次（表现为高亮只闪一瞬）。
    /// 每 0.35 秒最多重算一次，避免每帧跑 BFS。
    /// </summary>
    private void UpdateHighlights()
    {
        if (HexGrid.Instance == null) return;

        highlightAge -= Time.deltaTime;
        if (!highlightDirty && highlightAge > 0f) return;

        highlightDirty = false;
        highlightAge = 0.35f;

        bool hasSlot = player != null && !player.IsDead && InPlayerAction
                       && selectedSlot >= 0 && selectedSlot < cardSystem.Slots.Count
                       && !cardSystem.Slots[selectedSlot].IsEmpty;

        if (!hasSlot)
        {
            // 自己没在画高亮：什么都不动（HUD 的高亮归 HUD 管）
            if (paintedHighlight)
            {
                HexGrid.Instance.ClearAllHighlights();
                paintedHighlight = false;
            }
            return;
        }

        HexGrid.Instance.ClearAllHighlights();
        paintedHighlight = true;

        CardData card = cardSystem.Slots[selectedSlot].card;
        int maxRange = Mathf.Clamp(card.GetEffectiveMaxRange(player), 0, 12);
        HexCoord from = player.GetCoord();

        foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(from, maxRange))
        {
            if (cell.coord.Distance(from) < card.minRange) continue;
            cell.SetAttackRangeHighlight(true);
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

        DrawSlotPanel();
        DrawEnemyPanel();
        // 提示条已按需求移除：抽卡等引导提示改由 BattleHUD 的技能按钮流程承载

        // BattleHUD 在场时选牌界面由 HUD 负责，调试面板不再重复画一份（否则两套弹窗叠在一起）
        if (cardSystem != null && cardSystem.IsChoosing && BattleHUD.Instance == null)
            DrawChoosePanel();

        if (IsBattleOver)
            DrawResultPanel();
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
        float panelH = Mathf.Max(h, 60f);
        // 右下角"下一回合"按钮（UGUI，底部约 90px 高）的上方
        Rect panel = new Rect(Screen.width - 330f - 12f, Screen.height - panelH - 100f, 330f, panelH);
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


    /// <summary>选牌弹窗：列出待选牌（默认 3 张，取自 offerCount），按 1~N 灌注，0 放弃。</summary>
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
            "按 " + ChooseKeyRange() + " 灌注其中一张（还要再付牌面思绪）；0 或退格全部放弃", smallStyle);
    }

    /// <summary>
    /// 选牌阶段能按的数字键范围。张数取自 PlayerCardSystem.offerCount（默认 3），
    /// 不要写死成 "1 / 2 / 3" —— 张数以后再调大时键位要跟得上。
    /// </summary>
    private string ChooseKeyRange()
    {
        int n = cardSystem != null && cardSystem.offerCount > 0 ? cardSystem.offerCount : 3;
        n = Mathf.Min(n, ChooseKeys.Length);
        return n <= 1 ? "1" : "1 ~ " + n;
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
