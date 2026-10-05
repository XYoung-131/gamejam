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
/// - 暂停/继续（Esc 键切换）
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
            StartBattle();
        }
    }

    private void Update()
    {
        // 按 Esc 切换暂停
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (currentState == GameState.Battle || currentState == GameState.Paused)
                TogglePause();
        }
    }

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
    /// 切换暂停（Esc 键调用）
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
