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
//  操作（正式 UI 由 BattleHUD 承载，以下为调试快捷键）：
//    左键点空格子   → 进入移动选格；再点目标空格子移动，成功后退出
//    左键点敌人     → 选中槽位则对该敌人出牌，没选中则普通攻击（需相邻）
//    左键点自己     → 防御牌先确认自己；有目标的再点紫色格子
//    左键点障碍物   → 对障碍物使用当前槽位的攻击牌
//    1 / 2          → 从攻击 / 防御牌库抽卡（出现 3 张待选）
//    1 / 2 / 3      → 选牌阶段：灌注第 1/2/3 张（0 或退格放弃）
//    Q W E R T      → 选中第 1~5 号槽位（再按一次取消）
//    F              → 依次选择药水等授予的免费防御技能
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

    // ============================================================
    //                       交互状态
    // ============================================================

    private int selectedSlot = -1;     // 当前选中的槽位，-1 = 没选（此时点敌人走普通攻击）
    private int selectedFreeDefense = -1;
    private bool moveSelectionActive;
    private readonly List<HexCoord> pendingAttackCells = new List<HexCoord>();
    private readonly List<HexCoord> pendingDefenseCells = new List<HexCoord>();
    private bool defenseChoosingTargets;
    private readonly List<AttackAreaCell> recentAttackArea = new List<AttackAreaCell>();
    private float recentAttackAreaUntil;
    private bool paintedHighlight;     // 本组件当前是否画着高亮（没画过就绝不清别人的）
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
        Say("抽卡请点左下角\"技能\"按钮 · QWERT选槽位 · 空格结束回合");
    }

    private void OnEnable()
    {
        EventManager.AddListener<CardData, Unit, List<AttackAreaCell>>("CardAreaResolved", OnCardAreaResolved);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<CardData, Unit, List<AttackAreaCell>>("CardAreaResolved", OnCardAreaResolved);
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
            ClearMoveSelection();
            if (Input.GetKeyDown(KeyCode.R))
                RestartBattle();
            UpdateHighlights();
            return;
        }

        if (Input.GetKeyDown(KeyCode.F2)) showOverlay = !showOverlay;

        if (player == null || cardSystem == null)
        {
            ClearMoveSelection();
            return;
        }

        // ---------- 正在选牌：只接受 1/2/3 和 0 ----------
        if (cardSystem.IsChoosing)
        {
            ClearMoveSelection();
            HandleChooseKeys();
            UpdateHighlights();
            return;
        }

        // ---------- 常规操作 ----------
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
        if (Input.GetKeyDown(KeyCode.F)) SelectFreeDefense();

        if (Input.GetKeyDown(KeyCode.Escape) && pendingAttackCells.Count > 0)
        {
            pendingAttackCells.Clear();
            Say("已取消轰击目标选择");
            MarkDirty();
        }
        else if (Input.GetKeyDown(KeyCode.Escape) && defenseChoosingTargets)
        {
            defenseChoosingTargets = false;
            pendingDefenseCells.Clear();
            Say("已取消防御技能目标选择");
            MarkDirty();
        }
        else if (Input.GetKeyDown(KeyCode.Escape) && moveSelectionActive)
        {
            ClearMoveSelection();
            Say("已取消移动选格");
        }
        else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) &&
                 (selectedSlot >= 0 || GetSelectedFreeDefense() != null))
        {
            CardData card = GetSelectedFreeDefense() ?? cardSystem.Slots[selectedSlot].card;
            if (card != null && !card.IsAttack)
            {
                if (!defenseChoosingTargets || pendingDefenseCells.Count == 0) return;
                bool defenseOk = UseSelectedDefense(pendingDefenseCells);
                Say(defenseOk ? "【" + card.cardName + "】已释放" : "目标或耐力不符合要求");
                if (defenseOk) { defenseChoosingTargets = false; pendingDefenseCells.Clear(); }
                AfterPlay();
                return;
            }
            bool autoArea = card != null && IsClickToCastArea(card.attackTargetRule);
            if (!autoArea && pendingAttackCells.Count == 0) return;
            bool ok = cardSystem.UseSlotCardOnCells(selectedSlot, autoArea ? new HexCoord[0] : pendingAttackCells);
            Say(ok ? "【" + card.cardName + "】已释放" : "释放失败：请检查目标、射程、视线和耐力");
            if (ok) pendingAttackCells.Clear();
            AfterPlay();
        }

        HandleMouse();
        UpdateHighlights();
    }

    private void ClearMoveSelection()
    {
        if (moveSelectionActive) MarkDirty();
        moveSelectionActive = false;
    }

    private static bool IsClickToCastArea(AttackTargetRule rule) =>
        rule == AttackTargetRule.自身范围 || rule == AttackTargetRule.全场 ||
        rule == AttackTargetRule.全场随机 || rule == AttackTargetRule.固定地图图案;

    /// <summary>无需另选中心格的范围技能：这些高亮格都可以点击以释放。</summary>
    private IEnumerable<HexCoord> GetClickToCastCells(CardData card)
    {
        if (card.attackTargetRule == AttackTargetRule.固定地图图案)
        {
            // 礼炮轰炸在释放时才随机选四图之一，选牌时显示四图可能覆盖的格子。
            for (int pattern = 0; pattern < 4; pattern++)
                foreach (HexCoord coord in AttackAreaGeometry.GetFixedBombardmentPattern(pattern))
                    if (HexGrid.Instance.GetCell(coord) != null) yield return coord;
            yield break;
        }

        if (card.attackTargetRule == AttackTargetRule.全场随机)
        {
            foreach (HexCell cell in HexGrid.Instance.GetAllCells())
                if (cell != null && cell.occupyUnit != null &&
                    cell.occupyUnit.GetCurrentHealth() > 0 &&
                    ((cell.occupyUnit is PlayerUnit) != (player is PlayerUnit)))
                    yield return cell.coord;
            yield break;
        }

        foreach (AttackAreaCell entry in card.GetAttackArea(player, player.GetCoord()))
            if (HexGrid.Instance.GetCell(entry.Coord) != null)
                yield return entry.Coord;
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
            Say(ok ? "灌注成功：【" + pickedName + "】" : "灌注失败（思绪不足）");
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
        // HUD 瞄准模式时让位：点击由 HUD 处理，避免双重结算
        if (BattleHUD.Instance != null && BattleHUD.Instance.IsAiming) return;

        // 有 HUD 时常态不响应世界点击（移动/普攻只能通过 HUD 按钮）；纯调试场景保留原选格移动流程
        if (BattleHUD.Instance != null && selectedSlot < 0) return;

        if (!InPlayerAction) return;
        if (cam == null) cam = Camera.main;
        if (cam == null || HexGrid.Instance == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
        HexCell cell = HexGrid.Instance.GetCell(HexGrid.Instance.WorldToHex(world));
        if (cell == null) return;   // 点到地图外面，忽略

        if (moveSelectionActive)
        {
            if (cell.occupyUnit != null || cell.obstacle != null || !cell.CanStopOn(player))
            {
                Say("请选择绿色范围内的空地，或按 Esc 取消移动");
                return;
            }
            if (player.TryMoveTo(cell.coord))
            {
                ClearMoveSelection();
                Say("移动到 (" + cell.coord.q + "," + cell.coord.r + ")");
            }
            else
                Say("移动失败：请选绿色范围内的空地，或检查耐力");
            return;
        }

        if (selectedSlot >= 0 && selectedSlot < cardSystem.Slots.Count && !cardSystem.Slots[selectedSlot].IsEmpty)
        {
            CardData selectedCard = cardSystem.Slots[selectedSlot].card;
            if (!selectedCard.IsAttack)
            {
                HandleDefenseClick(selectedCard, cell);
                return;
            }
            if (selectedCard.IsAttack && IsClickToCastArea(selectedCard.attackTargetRule))
            {
                bool highlighted = false;
                foreach (HexCoord coord in GetClickToCastCells(selectedCard))
                    if (coord == cell.coord) { highlighted = true; break; }
                if (!highlighted)
                {
                    Say("请选择紫色高亮的地块释放【" + selectedCard.cardName + "】");
                    return;
                }
                bool ok = cardSystem.UseSlotCardOnCells(selectedSlot, new HexCoord[0]);
                Say(ok ? "【" + selectedCard.cardName + "】已释放" : "释放失败：请检查耐力和行动状态");
                AfterPlay();
                return;
            }
            if (selectedCard.attackTargetRule == AttackTargetRule.地块 ||
                selectedCard.attackTargetRule == AttackTargetRule.穿透单位 ||
                selectedCard.attackTargetRule == AttackTargetRule.连锁单位)
            {
                bool ok = cardSystem.UseSlotCardOnCells(selectedSlot, new[] { cell.coord });
                Say(ok ? "【" + selectedCard.cardName + "】已释放" : "该格不是有效目标，或耐力不足");
                AfterPlay();
                return;
            }
            if (selectedCard.attackTargetRule == AttackTargetRule.相邻地块定向扇形)
            {
                bool ok = cardSystem.UseSlotCardOnCells(selectedSlot, new[] { cell.coord });
                Say(ok ? "巨尾拍击已释放" : "请选择自己相邻的地块，或检查耐力");
                AfterPlay();
                return;
            }
            if (selectedCard.attackTargetRule == AttackTargetRule.多个单位)
            {
                EnemyUnit chosenEnemy = cell.occupyUnit as EnemyUnit;
                if (chosenEnemy == null || chosenEnemy.IsDead ||
                    !selectedCard.CanSelectAttackCell(player, cell.coord))
                {
                    Say("请选择紫色高亮的敌方单位");
                    return;
                }
                if (pendingAttackCells.Contains(cell.coord) ||
                    pendingAttackCells.Count >= Mathf.Max(1, selectedCard.maxSelectedTargets))
                {
                    bool ok = cardSystem.UseSlotCardOnCells(selectedSlot, pendingAttackCells);
                    Say(ok ? "【" + selectedCard.cardName + "】已释放" : "释放失败：请检查射程、视线和耐力");
                    if (ok) pendingAttackCells.Clear();
                    AfterPlay();
                    return;
                }
                pendingAttackCells.Add(cell.coord);
                Say("已选 " + pendingAttackCells.Count + " 个目标；点新目标继续选，再点已选目标释放");
                MarkDirty();
                return;
            }
        }

        CardData freeDefense = GetSelectedFreeDefense();
        if (freeDefense != null)
        {
            HandleDefenseClick(freeDefense, cell);
            return;
        }

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
            Say(ok ? "对自己使用【" + SlotName(selectedSlot) + "】" : "出牌失败（目标不符或耐力不足）");
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

        if (selectedSlot >= 0)
        {
            Say("已选中技能，请点击有效目标；再次按该槽位键取消后才能移动");
            return;
        }

        // ④ 第一次点空地：进入选格状态，第二次点目标空地才移动
        if (cell.CanStopOn(player) && player.CurrentStamina > 0)
        {
            selectedSlot = -1;
            pendingAttackCells.Clear();
            moveSelectionActive = true;
            Say("移动选格：再点绿色范围内的空地，Esc 取消");
            MarkDirty();
        }
        else
            Say("无法进入移动选格：耐力不足或地块不可进入");
    }

    private void HandleDefenseClick(CardData card, HexCell cell)
    {
        if (!defenseChoosingTargets)
        {
            if (cell.occupyUnit != player)
            {
                Say("先点击紫色高亮的自己");
                return;
            }
            bool needsTarget = card.defenseTargetRule != DefenseTargetRule.自身 ||
                (card.defenseEffectRule == DefenseEffectRule.神圣祝福 && player.HasNamedBuff("神圣"));
            if (!needsTarget)
            {
                bool ok = UseSelectedDefense(new HexCoord[0]);
                Say(ok ? "【" + card.cardName + "】已释放" : "释放失败：请检查耐力、行动状态和落点");
                AfterPlay();
                return;
            }
            bool hasLegalTarget = false;
            foreach (HexCoord coord in DefenseSkillTargeting.GetSelectableCells(card, player,
                         pendingDefenseCells))
                if (card.defenseTargetRule != DefenseTargetRule.地块 ||
                    DefenseSkillResolver.CanResolve(card, player, new[] { coord }))
                    { hasLegalTarget = true; break; }
            if (!hasLegalTarget)
            {
                Say("当前没有合法目标或落点");
                return;
            }
            defenseChoosingTargets = true;
            pendingDefenseCells.Clear();
            Say("请选择紫色高亮的目标或落点；Esc 取消");
            MarkDirty();
            return;
        }

        bool legal = DefenseSkillTargeting.GetSelectableCells(card, player,
            pendingDefenseCells).Contains(cell.coord);
        if (!legal || (card.defenseTargetRule == DefenseTargetRule.地块 &&
                       !DefenseSkillResolver.CanResolve(card, player, new[] { cell.coord })))
        {
            Say("该格不是有效目标或落点");
            return;
        }
        if (card.defenseTargetRule == DefenseTargetRule.多个单位)
        {
            if (!pendingDefenseCells.Contains(cell.coord) &&
                pendingDefenseCells.Count < Mathf.Max(1, card.maxDefenseTargets))
            {
                pendingDefenseCells.Add(cell.coord);
                Say("已选 " + pendingDefenseCells.Count + " 个目标；再次点击已选目标或回车释放");
                MarkDirty();
                return;
            }
            bool multiOk = UseSelectedDefense(pendingDefenseCells);
            Say(multiOk ? "【" + card.cardName + "】已释放" : "释放失败：目标或耐力不符合要求");
            if (multiOk) { defenseChoosingTargets = false; pendingDefenseCells.Clear(); }
            AfterPlay();
            return;
        }
        bool okSingle = UseSelectedDefense(new[] { cell.coord });
        Say(okSingle ? "【" + card.cardName + "】已释放" : "释放失败：目标或耐力不符合要求");
        if (okSingle) { defenseChoosingTargets = false; pendingDefenseCells.Clear(); }
        AfterPlay();
    }

    private CardData GetSelectedFreeDefense()
    {
        if (cardSystem == null || selectedFreeDefense < 0 ||
            selectedFreeDefense >= cardSystem.FreeCards.Count) return null;
        FreeCardCharge charge = cardSystem.FreeCards[selectedFreeDefense];
        return charge != null && !charge.IsEmpty && charge.card != null &&
            !charge.card.IsAttack ? charge.card : null;
    }

    private bool UseSelectedDefense(IReadOnlyList<HexCoord> selected)
    {
        bool free = GetSelectedFreeDefense() != null;
        bool ok = free
            ? cardSystem.UseFreeDefenseCardOnCells(selectedFreeDefense, selected)
            : cardSystem.TryUseDefenseCardOnCells(selectedSlot, selected);
        if (ok && free) selectedFreeDefense = -1;
        return ok;
    }

    private void SelectFreeDefense()
    {
        if (cardSystem == null) return;
        int next = selectedFreeDefense;
        do { next++; } while (next < cardSystem.FreeCards.Count &&
            (cardSystem.FreeCards[next].IsEmpty || cardSystem.FreeCards[next].card.IsAttack));
        selectedFreeDefense = next < cardSystem.FreeCards.Count ? next : -1;
        selectedSlot = -1;
        defenseChoosingTargets = false;
        pendingDefenseCells.Clear();
        pendingAttackCells.Clear();
        ClearMoveSelection();
        Say(selectedFreeDefense < 0 ? "已取消免费防御技能选择" :
            "选中免费技能【" + cardSystem.FreeCards[selectedFreeDefense].card.cardName + "】；先点自己");
        MarkDirty();
    }

    /// <summary>点敌人：有选中槽位就出牌，否则普通攻击。</summary>
    private void ActOnEnemy(EnemyUnit enemy)
    {
        if (selectedSlot >= 0)
        {
            string name = SlotName(selectedSlot);
            bool ok = cardSystem.UseSlotCard(selectedSlot, enemy);
            Say(ok ? "对 " + enemy.data.unitName + " 使用【" + name + "】"
                   : "出牌失败：射程 / 视线 / 目标类型 / 耐力不足（详见 Console）");
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
        selectedFreeDefense = -1;
        pendingAttackCells.Clear();
        defenseChoosingTargets = false;
        pendingDefenseCells.Clear();
        ClearMoveSelection();
        turnManager.RequestEndPlayerTurn();
        Say("结束回合，敌人开始行动");
        MarkDirty();
    }

    /// <summary>选中/取消选中一个槽位。</summary>
    private void SelectSlot(int index)
    {
        if (cardSystem == null || index < 0 || index >= cardSystem.Slots.Count)
            return;

        pendingAttackCells.Clear();
        selectedFreeDefense = -1;
        defenseChoosingTargets = false;
        pendingDefenseCells.Clear();
        ClearMoveSelection();

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
            Say("选中【" + card.cardName + "】 使用耐力 " + player.DefenseState.GetStaminaCost(card, player) + " / 射程 " +
                card.minRange + "~" + card.GetEffectiveMaxRange(player));
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
        if (selectedSlot < 0) pendingAttackCells.Clear();
        if (GetSelectedFreeDefense() == null) selectedFreeDefense = -1;
        if (selectedSlot < 0 && selectedFreeDefense < 0)
        { defenseChoosingTargets = false; pendingDefenseCells.Clear(); }
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
        Debug.Log("[BattleDebug] " + text);
    }

    private void MarkDirty() => highlightDirty = true;

    private void OnCardAreaResolved(CardData card, Unit caster, List<AttackAreaCell> area)
    {
        recentAttackArea.Clear();
        if (area != null) recentAttackArea.AddRange(area);
        recentAttackAreaUntil = Time.unscaledTime + 1f;
        MarkDirty();
    }

    private void HighlightArea(IEnumerable<AttackAreaCell> area)
    {
        foreach (AttackAreaCell entry in area)
        {
            HexCell cell = HexGrid.Instance.GetCell(entry.Coord);
            if (cell == null) continue;
            if (entry.Damage >= 3) cell.SetColor(new Color(1f, 0.35f, 0.3f, 0.8f));
            else if (entry.Damage == 2) cell.SetColor(new Color(1f, 0.65f, 0.25f, 0.8f));
            else cell.SetColor(new Color(1f, 0.85f, 0.3f, 0.8f));
        }
    }

    private void HighlightRecentArea()
    {
        if (Time.unscaledTime < recentAttackAreaUntil)
            HighlightArea(recentAttackArea);
    }

    // ============================================================
    //                        格子高亮
    // ============================================================

    /// <summary>
    /// 选中槽位时高亮该牌的射程；第一次点空地后高亮可移动范围。
    /// 每 0.35 秒最多重算一次，避免每帧跑 BFS。
    /// </summary>
    private void UpdateHighlights()
    {
        if (HexGrid.Instance == null) return;

        highlightAge -= Time.deltaTime;
        if (!highlightDirty && highlightAge > 0f) return;

        highlightDirty = false;
        highlightAge = 0.35f;

        // 只有本组件真的要画高亮时才清空：HUD 的常亮圈归 HUD 管，
        // 没画过高亮就调 ClearAllHighlights，会把 HUD 的高亮每 0.35 秒抹一次（表现为只闪一瞬）。
        bool willPaint = player != null && !player.IsDead && InPlayerAction
                         && (GetSelectedFreeDefense() != null || moveSelectionActive
                             || (selectedSlot >= 0 && selectedSlot < cardSystem.Slots.Count
                                 && !cardSystem.Slots[selectedSlot].IsEmpty));

        if (!willPaint)
        {
            if (paintedHighlight)
            {
                HexGrid.Instance.ClearAllHighlights();
                paintedHighlight = false;
            }
            HighlightRecentArea();
            return;
        }

        HexGrid.Instance.ClearAllHighlights();
        paintedHighlight = true;

        CardData freeCard = GetSelectedFreeDefense();
        if (freeCard != null)
        {
            HighlightRecentArea();
            HighlightDefense(freeCard);
            return;
        }

        if (selectedSlot >= 0 && selectedSlot < cardSystem.Slots.Count && !cardSystem.Slots[selectedSlot].IsEmpty)
        {
            CardData card = cardSystem.Slots[selectedSlot].card;
            HighlightRecentArea();
            if (!card.IsAttack)
            {
                HighlightDefense(card);
                return;
            }
            if (card.IsAttack && IsClickToCastArea(card.attackTargetRule))
            {
                foreach (HexCoord coord in GetClickToCastCells(card))
                {
                    HexCell targetCell = HexGrid.Instance.GetCell(coord);
                    if (targetCell != null) targetCell.SetAttackRangeHighlight(true);
                }
                return;
            }
            int maxRange = Mathf.Clamp(card.GetEffectiveMaxRange(player), 0, 12);
            HexCoord from = player.GetCoord();

            foreach (HexCell cell in HexGrid.Instance.GetCellsInRange(from, maxRange))
            {
                if (card.IsAttack && !card.CanSelectAttackCell(player, cell.coord)) continue;
                if (cell.coord.Distance(from) < card.minRange) continue;
                cell.SetAttackRangeHighlight(true);
            }
            if (card.attackTargetRule == AttackTargetRule.多个单位)
                foreach (HexCoord coord in pendingAttackCells)
                {
                    HexCell selected = HexGrid.Instance.GetCell(coord);
                    if (selected != null) selected.SetColor(new Color(0.65f, 0.25f, 0.9f, 0.85f));
                }
            else if (card.attackTargetRule == AttackTargetRule.相邻地块定向扇形 && cam != null)
            {
                Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
                HexCoord hovered = HexGrid.Instance.WorldToHex(world);
                HighlightArea(AttackAreaGeometry.GetCone(from, hovered));
            }
            else if ((card.attackTargetRule == AttackTargetRule.地块 ||
                      card.attackTargetRule == AttackTargetRule.穿透单位) && cam != null)
            {
                HexCoord hovered = HexGrid.Instance.WorldToHex(cam.ScreenToWorldPoint(Input.mousePosition));
                if (card.CanSelectAttackCell(player, hovered))
                    HighlightArea(card.GetAttackArea(player, hovered));
            }
            return;
        }

        if (moveSelectionActive)
            foreach (HexCoord coord in HexPathfinding.GetReachableCells(player.GetCoord(), player.CurrentStamina))
            {
                HexCell cell = HexGrid.Instance.GetCell(coord);
                if (cell != null && cell.CanStopOn(player))
                    cell.SetMoveRangeHighlight(true);
            }
        HighlightRecentArea();
    }

    private void HighlightDefense(CardData card)
    {
        if (!defenseChoosingTargets)
        {
            HexCell self = HexGrid.Instance.GetCell(player.GetCoord());
            if (self != null) self.SetAttackRangeHighlight(true);
            return;
        }
        foreach (HexCoord coord in DefenseSkillTargeting.GetSelectableCells(card, player,
                     pendingDefenseCells))
        {
            HexCell targetCell = HexGrid.Instance.GetCell(coord);
            if (targetCell != null &&
                (card.defenseTargetRule != DefenseTargetRule.地块 ||
                 DefenseSkillResolver.CanResolve(card, player, new[] { coord })))
                targetCell.SetAttackRangeHighlight(true);
        }
        foreach (HexCoord coord in pendingDefenseCells)
        {
            HexCell selected = HexGrid.Instance.GetCell(coord);
            if (selected != null) selected.SetColor(new Color(0.65f, 0.25f, 0.9f, 0.85f));
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

        // 选牌界面由 BattleHUD 承载；只有 HUD 不在场（纯调试场景）时才画 IMGUI 版
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
                       "   耐力" + slot.card.GetPlayActionCost(player) + "   剩" + uses;
            }
            GUI.Label(new Rect(panel.x + 12f, y, panel.width - 24f, 22f), text, smallStyle);
        }
    }

    private void DrawEnemyPanel()
    {
        CollectEnemies();

        float h = 40f + enemyBuffer.Count * 46f;
        // 右下角：锚定在"下一回合"按钮上方（底部留 100px 给按钮区）
        Rect panel = new Rect(Screen.width - 330f - 12f, Screen.height - Mathf.Max(h, 60f) - 100f,
            330f, Mathf.Max(h, 60f));
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
                "[" + (i + 1) + "] " + card.cardName + "   灌注思绪" + card.thoughtCost +
                " 使用耐力" + card.GetPlayActionCost(player) + "   " + attackHint, labelStyle);
            GUI.Label(new Rect(panel.x + 18f, y + 20f, width - 36f, 22f), card.description, smallStyle);
        }

        GUI.Label(new Rect(panel.x + 14f, panel.y + height - 30f, width - 28f, 22f),
            "按 1 / 2 / 3 灌注其中一张（只支付牌面思绪）；0 或退格放弃", smallStyle);
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
