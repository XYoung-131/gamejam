using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  这个文件是"战斗节奏的总导演"
//
//  它本身不实现伤害/抽卡，只负责"按什么顺序调用谁"：
//  什么时候展示敌方预告、什么时候允许玩家操作、什么时候结算 buff、
//  什么时候轮到敌人出手。
//
//  和 GameManager 的分工：
//  - GameManager：管"玩家回合 ↔ 敌人回合"这个大开关和胜负判定；
//  - TurnManager：监听 GameManager 抛出的回合事件，把每个大阶段
//    拆成更细的步骤（预告/行动/结算），并用协程控制节奏（等待秒数）。
//
//  驱动方式（事件驱动，不是每帧轮询）：
//  GameManager 抛 "PlayerTurnStarted" → 本类开始回合预告流程
//  玩家点结束回合 → RequestEndPlayerTurn() → 让 GameManager 切敌人回合
//  GameManager 抛 "EnemyTurnStarted"   → 本类执行敌方行动流程
//
//  为什么用协程（IEnumerator）：
//  敌人放多个技能之间要停顿、预告要展示几秒，协程里用
//  yield return new WaitForSeconds(...) 就能按时间线顺序执行，
//  不会卡住游戏主线程。
//
//  挂载位置：场景里任意一个常驻 GameObject 上（全场景只能有一个）。
// ============================================================

/// <summary>
/// 战斗阶段——比 GameManager 的"玩家/敌人回合"更细的状态划分。
/// PlayerCardSystem 会检查这个枚举：只有处于"玩家行动"时才允许抽卡/出牌。
/// </summary>
public enum TurnPhase
{
    回合开始,       // 第 ① 步：敌方技能预告展示中
    玩家行动,       // 第 ② 步：玩家自由操作（灌注、出牌）
    Buff结算,       // 第 ③ 步：玩家点结束回合后，结算回合结束类 buff
    敌方行动,       // 第 ④ 步：敌人依次释放预告技能
    战斗结束        // 胜利或失败，所有流程停止
}

/// <summary>
/// 一条敌方预告信息：哪个敌人 + 它这回合打算放哪些技能。
/// 回合开始时把所有敌人的预告打包成一个列表发给 UI。
/// </summary>
public class EnemyIntent
{
    public EnemyUnit enemy;            // 预告的主人
    public List<CardData> skills;      // 它本回合将依次释放的技能
}

/// <summary>
/// 回合管理器（整体流程和事件见文件顶部注释）。
/// </summary>
public class TurnManager : MonoBehaviour
{
    // 单例：其他脚本通过 TurnManager.Instance 直接访问，不需要拖拽引用
    public static TurnManager Instance { get; private set; }

    [Header("节奏设置（秒）")]
    [Tooltip("回合开始后技能预告展示多久才进入玩家行动")]
    public float intentPreviewTime = 1.0f;
    [Tooltip("同一个敌人的两个技能之间的停顿")]
    public float enemySkillDelay = 0.6f;
    [Tooltip("两个敌人之间行动的停顿")]
    public float enemyInterval = 0.4f;

    /// <summary>当前战斗阶段（外部只读）</summary>
    public TurnPhase Phase { get; private set; } = TurnPhase.回合开始;

    // ============================================================
    //                       单例 + 事件注册
    // ============================================================

    /// <summary>
    /// 单例初始化：场景里出现第二个 TurnManager 时，把后出现的销毁掉。
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>
    /// 组件启用时订阅事件（开场/切回场景时）。
    /// 泛型参数 int / GameState 必须和 EventManager.Trigger 时传的参数类型对应。
    /// </summary>
    private void OnEnable()
    {
        EventManager.AddListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.AddListener("EnemyTurnStarted", HandleEnemyTurnStarted);
        EventManager.AddListener<GameState>("GameStateChanged", HandleGameStateChanged);
    }

    /// <summary>
    /// 组件禁用时必须取消订阅，否则对象销毁后还会收到事件导致报错。
    /// 和 OnEnable 一一对应。
    /// </summary>
    private void OnDisable()
    {
        EventManager.RemoveListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.RemoveListener("EnemyTurnStarted", HandleEnemyTurnStarted);
        EventManager.RemoveListener<GameState>("GameStateChanged", HandleGameStateChanged);
    }

    // ============================================================
    //  ① 回合开始：玩家回资源/吃回合开始 buff → 敌方生成预告 → ②
    // ============================================================

    /// <summary>
    /// 收到 GameManager 的"玩家回合开始"事件。
    /// turnCount 是 GameManager 传来的当前回合数（第几个回合）。
    /// </summary>
    private void HandlePlayerTurnStarted(int turnCount)
    {
        if (!IsBattleRunning()) return;
        StartCoroutine(PlayerTurnSetupRoutine(turnCount));
    }

    /// <summary>
    /// 回合开场的时间线（协程）。
    /// </summary>
    private IEnumerator PlayerTurnSetupRoutine(int turnCount)
    {
        ChangePhase(TurnPhase.回合开始);
        Debug.Log($"===== 第 {turnCount} 回合开始：敌方技能预告 =====");

        // 玩家回合开始结算：OnTurnStart 内部会回满思绪/耐力，
        // 并触发"回合开始"时机的 buff（比如流血扣血）
        PlayerUnit player = FindAlivePlayer();
        if (player != null)
            player.OnTurnStart();

        // 让每个存活敌人提前决定这回合放什么技能，并收集成预告列表
        List<EnemyIntent> intents = new List<EnemyIntent>();
        foreach (var enemy in FindAliveEnemies())
        {
            enemy.PreparePlannedSkills();   // 随机生成技能，存入敌人的 PlannedSkills

            // new List<CardData>(...) 是复制一份，避免 UI 读到的列表被后续流程改动
            intents.Add(new EnemyIntent { enemy = enemy, skills = new List<CardData>(enemy.PlannedSkills) });

            // 控制台打印预告内容（没接 UI 时方便验证）
            string names = "";
            foreach (var card in enemy.PlannedSkills)
                names += $"【{card.cardName}】";
            Debug.Log($"预告：{enemy.data.unitName} 本回合将释放 {names}");
        }

        // 通知 UI：预告数据齐了，可以在敌人头顶画技能图标了
        EventManager.Trigger("EnemyIntentsReady", intents);

        // 等几秒让玩家看清预告（时间在 Inspector 可调）
        yield return new WaitForSeconds(intentPreviewTime);

        // 等待期间战斗可能已结束（比如持续伤害把最后一人跳死），再检查一次
        if (!IsBattleRunning()) yield break;

        // ② 正式进入玩家行动阶段——从此刻起 PlayerCardSystem 才允许操作
        ChangePhase(TurnPhase.玩家行动);
        Debug.Log("----- 玩家行动阶段：可抽卡灌注 / 窃取技能 / 出牌 -----");
        EventManager.Trigger("PlayerActionPhaseStarted");
    }

    // ============================================================
    //  ③ 玩家点"结束回合"：先结算 buff，再通知 GameManager 切回合
    // ============================================================

    /// <summary>
    /// UI 上"结束回合"按钮的点击入口。
    /// 只有玩家行动阶段才响应，防止预告/敌人阶段误触。
    /// </summary>
    public void RequestEndPlayerTurn()
    {
        if (Phase != TurnPhase.玩家行动)
        {
            Debug.Log("当前不是玩家行动阶段，不能结束回合");
            return;
        }

        if (!IsBattleRunning()) return;

        StartCoroutine(EndPlayerTurnRoutine());
    }

    /// <summary>
    /// 结束玩家回合的时间线。
    /// </summary>
    private IEnumerator EndPlayerTurnRoutine()
    {
        ChangePhase(TurnPhase.Buff结算);   // 一切换阶段，玩家就不能再出牌了
        Debug.Log("----- 结束回合：Buff 结算 -----");

        // 玩家"回合结束"时机的 buff（回血、中毒结算、回合结束死亡等）
        PlayerUnit player = FindAlivePlayer();
        if (player != null)
            player.OnTurnEnd();

        EventManager.Trigger("BuffsSettled");

        // 等一帧，让监听 BuffsSettled 的 UI/特效先播完
        yield return null;

        if (!IsBattleRunning()) yield break;

        // 交给 GameManager：它会延迟片刻后抛 "EnemyTurnStarted"，
        // 本类的 HandleEnemyTurnStarted 会接住并继续第 ④ 步
        if (GameManager.Instance != null)
            GameManager.Instance.EndPlayerTurn();
    }

    // ============================================================
    //  ④ 敌方行动：敌人回合开始 buff → 逐个放预告技能 → ⑤
    // ============================================================

    /// <summary>收到 GameManager 的"敌人回合开始"事件。</summary>
    private void HandleEnemyTurnStarted()
    {
        if (!IsBattleRunning()) return;
        StartCoroutine(EnemyPhaseRoutine());
    }

    /// <summary>
    /// 敌方行动的时间线（协程）。
    /// </summary>
    private IEnumerator EnemyPhaseRoutine()
    {
        ChangePhase(TurnPhase.敌方行动);
        Debug.Log("----- 敌方行动阶段 -----");

        // 敌人自己的"回合开始"buff
        foreach (var enemy in FindAliveEnemies())
            enemy.OnTurnStart();

        // 敌人一个接一个行动；每个敌人内部再一个接一个放技能
        // 注意：这里重新 FindAliveEnemies()，因为前面的敌人可能把玩家打死导致战斗结束
        foreach (var enemy in FindAliveEnemies())
        {
            if (!IsBattleRunning()) yield break;          // 玩家死了，立刻停止
            if (enemy.GetCurrentHealth() <= 0) continue;  // 这个敌人已死，跳过

            // ExecutePlannedSkills 本身也是协程：放一个技能、停顿、再放下一个
            // 用 yield return 等它全部放完，才轮到下一个敌人
            yield return enemy.ExecutePlannedSkills(enemySkillDelay);
            yield return new WaitForSeconds(enemyInterval);
        }

        if (!IsBattleRunning())
        {
            ChangePhase(TurnPhase.战斗结束);
            yield break;
        }

        // ⑤ 一回合完整结束。EndEnemyTurn 内部会让回合数 +1，
        // 然后重新抛 "PlayerTurnStarted"，回到第 ① 步形成循环。
        Debug.Log("===== 回合结束 =====");
        if (GameManager.Instance != null)
            GameManager.Instance.EndEnemyTurn();
    }

    // ============================================================
    //                       战斗结束
    // ============================================================

    /// <summary>
    /// 监听 GameManager 的状态变化；一旦胜利或失败，
    /// 立刻进入结束阶段并停掉所有协程（防止敌人还在继续放技能）。
    /// </summary>
    private void HandleGameStateChanged(GameState state)
    {
        if (state == GameState.Victory || state == GameState.Defeat)
        {
            ChangePhase(TurnPhase.战斗结束);
            StopAllCoroutines();
            Debug.Log($"战斗结束：{state}");
        }
    }

    // ============================================================
    //                       工具方法
    // ============================================================

    /// <summary>
    /// 切换阶段并广播事件（UI 靠这个事件显隐按钮、切换提示文字）。
    /// </summary>
    private void ChangePhase(TurnPhase next)
    {
        Phase = next;
        EventManager.Trigger("TurnPhaseChanged", next);
    }

    /// <summary>
    /// 战斗是否还在进行中。
    /// GameManager 还没生成时也返回 true，方便单独测试本脚本。
    /// </summary>
    private bool IsBattleRunning()
    {
        return GameManager.Instance == null || GameManager.Instance.IsInBattle;
    }

    /// <summary>找场上活着的玩家（当前游戏只有一个玩家单位）。</summary>
    private PlayerUnit FindAlivePlayer()
    {
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        return player != null && player.GetCurrentHealth() > 0 ? player : null;
    }

    /// <summary>找场上所有存活敌人，组成列表（每次行动前都会重新查）。</summary>
    private List<EnemyUnit> FindAliveEnemies()
    {
        List<EnemyUnit> result = new List<EnemyUnit>();
        foreach (var enemy in FindObjectsOfType<EnemyUnit>())
        {
            if (enemy.GetCurrentHealth() > 0)
                result.Add(enemy);
        }
        return result;
    }

    // ============================================================
    //                  Inspector 右键测试（无 UI 时调试用）
    // ============================================================

    [ContextMenu("结束玩家回合")]
    private void DebugEndTurn() => RequestEndPlayerTurn();
}
