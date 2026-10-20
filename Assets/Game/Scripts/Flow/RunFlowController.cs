using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Owns one in-memory run and connects route transitions to scenes.</summary>
public sealed class RunFlowController : MonoBehaviour
{
    public static RunFlowController Instance { get; private set; }
    public RunProgression Progression { get; private set; } = new RunProgression();
    private string lastRequestedEventKey;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // RunFlowController 只在这里自举，之后 DontDestroyOnLoad 跨场景常驻。
        // 不能在场景名上设限：直接从 Battle.unity 起播时若不建它，
        // 之后回到主界面点「唤醒」就会报 "RunFlowController is missing"。
        if (Instance != null) return;
        new GameObject("RunFlowController").AddComponent<RunFlowController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        EventManager.AddListener<GameState>("GameStateChanged", OnGameStateChanged);
        RunFlowUI ui = gameObject.AddComponent<RunFlowUI>();
        if (SceneManager.GetActiveScene().name == "MainMenu") ui.Show(Progression);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        EventManager.RemoveListener<GameState>("GameStateChanged", OnGameStateChanged);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    public void StartNewGame()
    {
        RunRouteData route = Resources.Load<RunRouteData>("RunRoute");
        if (!Progression.StartNew(route))
        {
            Debug.LogError("[RunFlow] RunRoute missing or incomplete.");
            return;
        }
        PlayerRunState.Reset();
        lastRequestedEventKey = null;
        ShowInterlude();
        RunSaveSystem.Save();   // 开局就落一次档，之后"唤醒"才会提示已存在存档
    }

    /// <summary>
    /// 从存档继续上一局：读档成功后按恢复出来的相位接着推界面
    /// （战斗 → 直接进战斗场景；事件 → 弹该事件；休息处 → 弹休息界面）。
    /// </summary>
    public bool ContinueRun()
    {
        if (!RunSaveSystem.Load()) return false;
        lastRequestedEventKey = null;
        ShowInterlude();
        return true;
    }

    /// <summary>把存档里的进度灌回 Progression（RunSaveSystem.Load 调用）</summary>
    public bool RestoreProgression(RunRouteData route, RunPhase phase, int chapterIndex, int battleIndex,
        string eventId, int[] eliteChoices, string[] remainingEvents, int variantIndex)
    {
        return Progression.Restore(route, phase, chapterIndex, battleIndex, eventId,
            eliteChoices, remainingEvents, variantIndex);
    }

    /// <summary>
    /// 事件做完 → 存一次档 → 进下一关。
    /// 返回是否真的推进了（并且已经发出换场景的请求），
    /// 供调用方判断"要不要自己收掉界面"——换场景时界面该留着挡视线。
    /// </summary>
    public bool CompleteEvent()
    {
        if (!Progression.CompleteEvent()) return false;
        RunSaveSystem.Save();
        return EnterBattle();
    }

    public void ChooseRest(RestOption option)
    {
        if (Progression.Phase != RunPhase.Rest) return;
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        if (player == null) return;
        switch (option)
        {
            case RestOption.HealHalf:
                player.Heal(Mathf.CeilToInt(player.GetMaxHealth() * .5f));
                break;
            case RestOption.HealthAndHealQuarter:
                player.SetRunHealth(player.GetCurrentHealth(), player.GetMaxHealth() + 2);
                player.Heal(Mathf.CeilToInt(player.GetMaxHealth() * .25f));
                break;
            case RestOption.HealthAndResources:
                player.SetRunHealth(player.GetCurrentHealth(), player.GetMaxHealth() + 2);
                player.maxStamina += 5;
                player.maxThought += 5;
                break;
        }

        // 「草席卷」：在休息处额外恢复 1 点生命值（任何休息选项都加）
        int restBonus = Mathf.RoundToInt(
            RelicData.GetTotalValue(player.Relics, RelicEffectType.休息恢复加成));
        if (restBonus > 0)
        {
            player.Heal(restBonus);
            Debug.Log($"草席卷：休息额外恢复 {restBonus} 点生命值");
        }

        PlayerRunState.Capture(player);
        if (Progression.ChooseRest(option))
        {
            RunSaveSystem.Save();
            EnterBattle();
        }
    }

    public void ContinueAfterRewards()
    {
        if (Progression.Phase != RunPhase.Reward) return;
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        PlayerRunState.Capture(player);
        LootSystem loot = FindObjectOfType<LootSystem>();
        if (loot != null) PlayerRunState.PotionMisses = loot.ConsecutivePotionMisses;
        if (!Progression.ContinueAfterRewards()) return;
        RunSaveSystem.Save();
        if (Progression.Phase == RunPhase.Battle) EnterBattle();
        else ShowInterlude();
    }

    public void ReturnToMenu()
    {
        RunSaveSystem.Delete();   // 这一局已经打完 / 失败了，回主界面前把存档清掉
        GoToMainMenu();
    }

    /// <summary>
    /// ESC 菜单「保存并返回主界面」：把当局进度存档并**保留存档**，回主界面。
    /// 与 ReturnToMenu 的区别就是这里不删档——之后在主界面点「回忆」可以接着打。
    /// </summary>
    public void SaveAndReturnToMenu()
    {
        RunSaveSystem.Save();
        GoToMainMenu();
    }

    /// <summary>回到主界面：清掉本局的内存状态（不动存档文件），加载 Start 场景</summary>
    private void GoToMainMenu()
    {
        PlayerRunState.Reset();
        lastRequestedEventKey = null;
        Progression = new RunProgression();
        LevelManager.pendingNextLevel = null;
        LevelManager.pendingVariantIndex = null;
        Time.timeScale = 1f;   // 万一是从暂停的 ESC 菜单过来的，别把冻结的时间带进主界面
        SceneManager.LoadScene("Start");
    }

    private void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Victory) Progression.CompleteBattle();
        else if (state == GameState.Defeat && Progression.Defeat())
        {
            RunSaveSystem.Delete();   // 这一局失败了，存档作废
            ShowInterlude();
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Start" && Progression.Phase == RunPhase.Menu)
        {
            GetComponent<RunFlowUI>()?.Hide();
            return;
        }
        if (scene.name == "MainMenu" || Progression.Phase == RunPhase.Event ||
            Progression.Phase == RunPhase.Rest || Progression.Phase == RunPhase.Victory ||
            Progression.Phase == RunPhase.Defeat) ShowInterlude();
        if (scene.name == "Battle" && Progression.Phase == RunPhase.Battle)
        {
            LootSystem loot = FindObjectOfType<LootSystem>();
            if (loot != null) loot.RestorePotionMisses(PlayerRunState.PotionMisses);
        }
    }

    private void ShowInterlude()
    {
        RunFlowUI ui = GetComponent<RunFlowUI>();
        if (Progression.Phase == RunPhase.Event)
        {
            if (ui != null) ui.Hide();
            RequestCurrentEvent();
        }
        // 读档续玩时可能直接落在"战斗"相位：这里补一条分支，直接进战斗场景
        else if (Progression.Phase == RunPhase.Battle)
        {
            if (ui != null) ui.Hide();
            EnterBattle();
        }
        else if (ui != null) ui.Show(Progression);
    }

    private void RequestCurrentEvent()
    {
        if (Progression.Phase != RunPhase.Event) return;
        string key = Progression.ChapterIndex + ":" + Progression.BattleIndex + ":" + Progression.EventId;
        if (key == lastRequestedEventKey) return;
        if (!ShowEvent(Progression.EventId))
        {
            Debug.LogError("[RunFlow] Unknown event: " + Progression.EventId);
            return;
        }
        lastRequestedEventKey = key;
        EventManager.Trigger("RunEventRequested", Progression.EventId);
    }

    private static bool ShowEvent(string eventId)
    {
        switch (eventId)
        {
            case "实验开始": EventPanel_实验开始.Show(); break;
            case "垃圾清理": EventPanel_垃圾清理.Show(); break;
            case "奇怪的药剂": EventPanel_奇怪的药剂.Show(); break;
            case "小助手": EventPanel_小助手.Show(); break;
            case "抉择": EventPanel_抉择.Show(); break;
            case "次元饮料": EventPanel_次元饮料.Show(); break;
            case "洗礼": EventPanel_洗礼.Show(); break;
            case "温泉": EventPanel_温泉.Show(); break;
            case "自助售卖机": EventPanel_自助售卖机.Show(); break;
            case "重启": EventPanel_重启.Show(); break;
            default: return false;
        }
        return true;
    }

    /// <summary>切到这一关的战斗场景；返回是否成功发出换场景请求</summary>
    private bool EnterBattle()
    {
        LevelData level = Progression.CurrentLevel;
        if (level == null) { Debug.LogError("[RunFlow] Current battle level missing."); return false; }
        int variant = Progression.CurrentVariantIndex;
        if (variant < 0) { Debug.LogError("[RunFlow] Current battle has no variants."); return false; }
        LevelManager.pendingNextLevel = level;
        LevelManager.pendingVariantIndex = variant;
        RunFlowUI ui = GetComponent<RunFlowUI>();
        if (ui != null) ui.Hide();
        SceneManager.LoadScene("Battle");
        return true;
    }
}
