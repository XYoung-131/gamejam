using UnityEngine;

/// <summary>
/// 游戏状态
/// </summary>
public enum GameState
{
    Battle,      // 战斗中
    Paused,      // 暂停
    Victory,     // 胜利
    Defeat       // 失败
}

/// <summary>
/// 游戏管理器——全局单例，管理游戏状态、回合、胜负
///
/// 当前可用功能（匹配现有代码结构）：
/// - 战斗状态切换（战斗/暂停/胜利/失败）
/// - 回合管理（玩家回合 ↔ 敌人回合）
/// - 胜负判定（玩家全死=失败，敌人全死=胜利）
/// - 暂停/继续（对外仍可用 Pause / Resume / TogglePause；游戏内 Esc 由 EscMenuUI 接管）
///
/// 挂在场景里的空 GameObject 上
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("游戏状态")]
    [SerializeField] private GameState currentState = GameState.Battle;

    [Header("回合")]
    public bool isPlayerTurn = true;
    public int turnCount = 1;

    [Header("关卡 / 波次（供遗物触发：精力充沛 / 肌肉强化 / 次元抹杀 / 劣质饮料机）")]
    [Tooltip("当前楼层（第 1 章 = 1 层）；次元抹杀伤害 = 2 × 楼层")]
    public int floorNumber = 1;
    [Tooltip("本关总波数（策划填写）")]
    public int totalWaves = 1;
    [Tooltip("当前波次序号（从 1 开始）")]
    public int waveIndex = 1;

    [Header("测试设置")]
    [Tooltip("是否在启动时自动开始战斗（测试用）")]
    public bool autoStartBattle = true;
    [Tooltip("敌人回合延迟（秒）")]
    public float enemyTurnDelay = 0.5f;

    /// <summary>
    /// 当前游戏状态
    /// </summary>
    public GameState CurrentState => currentState;

    /// <summary>
    /// 是否在战斗中
    /// </summary>
    public bool IsInBattle => currentState == GameState.Battle;

    /// <summary>
    /// 是否暂停
    /// </summary>
    public bool IsPaused => currentState == GameState.Paused;

    // ============================================================
    //                     单例 + 初始化
    // ============================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (autoStartBattle)
        {
            StartLevel();
        }
    }

    // 注：原来这里用 Esc 切换暂停，现在 Esc 由游戏内 ESC 菜单（EscMenuUI）接管，
    // 暂停改由菜单调用 Pause() / Resume()。

    // ============================================================
    //                     状态切换
    // ============================================================

    /// <summary>
    /// 开始战斗
    /// </summary>
    public void StartBattle()
    {
        ChangeState(GameState.Battle);
        turnCount = 1;
        isPlayerTurn = true;

        // 触发玩家回合开始
        EventManager.Trigger("PlayerTurnStarted", turnCount);
        Debug.Log($"战斗开始！第 {turnCount} 回合，玩家回合");
    }

    // ============================================================
    //                    关卡 / 波次（最小机制）
    //
    //  说明：目前没有完整的关卡地图生成流程，这里只提供"关卡/波次"的
    //  最小骨架——刷新楼层与波次号并广播事件，让依赖它们的遗物
    //  （精力充沛 / 肌肉强化 / 次元抹杀 / 劣质饮料机）能正常触发。
    //  以后接入真正的关卡流程时，改成在这些节点上调用即可。
    // ============================================================

    /// <summary>
    /// 开始一个关卡（楼层）：刷新楼层号 → 广播 LevelStarted → 从第 1 波开始。
    /// floor 传 -1（或不传）表示沿用 Inspector 里填的 floorNumber。
    /// </summary>
    public void StartLevel(int floor = -1)
    {
        if (floor > 0) floorNumber = floor;

        waveIndex = 1;
        EventManager.Trigger("LevelStarted", floorNumber);
        Debug.Log($"===== 第 {floorNumber} 层开始 =====");

        StartWave(1);
    }

    /// <summary>
    /// 开始一波怪物：广播 WaveStarted（带上"是否最后一波"）→ 进入战斗。
    /// index 传 -1（或不传）表示沿用当前 waveIndex。
    /// </summary>
    public void StartWave(int index = -1)
    {
        if (index > 0) waveIndex = index;

        bool isLastWave = waveIndex >= totalWaves;
        EventManager.Trigger("WaveStarted", waveIndex, isLastWave);
        Debug.Log($"第 {floorNumber} 层 第 {waveIndex}/{totalWaves} 波开始");

        StartBattle();
    }

    /// <summary>
    /// 进入下一波（本关还有波次时）。一波的敌人被清空后由流程调用。
    /// </summary>
    public void NextWave()
    {
        if (waveIndex >= totalWaves)
        {
            Debug.Log("已是最后一波，本关结束");
            return;
        }

        StartWave(waveIndex + 1);
    }

    /// <summary>
    /// 切换游戏状态
    /// </summary>
    public void ChangeState(GameState newState)
    {
        if (currentState == newState) return;

        OnExitState(currentState);
        currentState = newState;
        OnEnterState(newState);

        Debug.Log($"GameState → {newState}");
        EventManager.Trigger("GameStateChanged", newState);
    }

    private void OnEnterState(GameState state)
    {
        switch (state)
        {
            case GameState.Battle:
                Time.timeScale = 1f;
                break;
            case GameState.Paused:
                Time.timeScale = 0f;   // 暂停：时间停止
                break;
            case GameState.Victory:
                Time.timeScale = 1f;
                break;
            case GameState.Defeat:
                Time.timeScale = 1f;
                break;
        }
    }

    private void OnExitState(GameState state)
    {
        switch (state)
        {
            case GameState.Paused:
                Time.timeScale = 1f;   // 恢复时间
                break;
        }
    }

    // ============================================================
    //                    暂停 / 继续
    // ============================================================

    /// <summary>
    /// 切换暂停（游戏内由 EscMenuUI 的「设置 / 返回主界面」流程调用）
    /// </summary>
    public void TogglePause()
    {
        if (currentState == GameState.Battle)
            ChangeState(GameState.Paused);
        else if (currentState == GameState.Paused)
            ChangeState(GameState.Battle);
    }

    /// <summary>
    /// 暂停游戏
    /// </summary>
    public void Pause()
    {
        if (currentState == GameState.Battle)
            ChangeState(GameState.Paused);
    }

    /// <summary>
    /// 继续游戏
    /// </summary>
    public void Resume()
    {
        if (currentState == GameState.Paused)
            ChangeState(GameState.Battle);
    }

    // ============================================================
    //                      胜负判定
    // ============================================================

    /// <summary>
    /// 玩家胜利（所有敌人死亡时调用）
    /// 由 EnemyUnit.Die() 中检查并调用
    /// </summary>
    public void OnPlayerVictory()
    {
        if (currentState != GameState.Battle) return;
        ChangeState(GameState.Victory);
        Debug.Log("★ 胜利！所有敌人已被消灭");
    }

    /// <summary>
    /// 玩家失败（所有玩家死亡时调用）
    /// 由 PlayerUnit.Die() 中检查并调用
    /// </summary>
    public void OnPlayerDefeat()
    {
        if (currentState != GameState.Battle) return;
        ChangeState(GameState.Defeat);
        Debug.Log("✗ 失败... 所有玩家已阵亡");
    }

    // ============================================================
    //                      回合管理
    // ============================================================

    /// <summary>
    /// 结束玩家回合 → 开始敌人回合
    /// 由玩家 UI 的"结束回合"按钮调用
    /// </summary>
    public void EndPlayerTurn()
    {
        if (!isPlayerTurn || currentState != GameState.Battle) return;

        isPlayerTurn = false;
        EventManager.Trigger("PlayerTurnEnded");
        Debug.Log("玩家回合结束");

        // 延迟一点再开始敌人回合（给玩家视觉反馈）
        Invoke(nameof(StartEnemyTurn), enemyTurnDelay);
    }

    /// <summary>
    /// 开始敌人回合
    /// </summary>
    private void StartEnemyTurn()
    {
        if (currentState != GameState.Battle) return;

        EventManager.Trigger("EnemyTurnStarted");
        Debug.Log("敌人回合开始");

        // 敌人单位自己监听 "EnemyTurnStarted" 并行动
        // 所有敌人行动完后，每个敌人会检查是否都行动完了
        // 如果都行动完了 → 调用 EndEnemyTurn()
    }

    /// <summary>
    /// 结束敌人回合 → 开始玩家回合
    /// 由 EnemyUnit.CheckAllEnemiesActed() 调用
    /// </summary>
    public void EndEnemyTurn()
    {
        if (isPlayerTurn || currentState != GameState.Battle) return;

        isPlayerTurn = true;
        turnCount++;

        EventManager.Trigger("PlayerTurnStarted", turnCount);
        Debug.Log($"第 {turnCount} 回合，玩家回合开始");
    }

    // ============================================================
    //                       重新开始
    // ============================================================

    /// <summary>
    /// 重新开始战斗（当前场景重开）
    /// 以后有场景管理了再扩展
    /// </summary>
    public void RestartBattle()
    {
        // 简单实现：重置状态并重新开始
        turnCount = 1;
        isPlayerTurn = true;
        StartBattle();
    }
}
