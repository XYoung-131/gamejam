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
        string sceneName = SceneManager.GetActiveScene().name;
        if ((sceneName != "Start" && sceneName != "MainMenu") || Instance != null) return;
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
    }

    public void CompleteEvent()
    {
        if (!Progression.CompleteEvent()) return;
        EnterBattle();
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
        PlayerRunState.Capture(player);
        if (Progression.ChooseRest(option)) EnterBattle();
    }

    public void ContinueAfterRewards()
    {
        if (Progression.Phase != RunPhase.Reward) return;
        PlayerUnit player = FindObjectOfType<PlayerUnit>();
        PlayerRunState.Capture(player);
        LootSystem loot = FindObjectOfType<LootSystem>();
        if (loot != null) PlayerRunState.PotionMisses = loot.ConsecutivePotionMisses;
        if (!Progression.ContinueAfterRewards()) return;
        if (Progression.Phase == RunPhase.Battle) EnterBattle();
        else ShowInterlude();
    }

    public void ReturnToMenu()
    {
        PlayerRunState.Reset();
        lastRequestedEventKey = null;
        Progression = new RunProgression();
        LevelManager.pendingNextLevel = null;
        LevelManager.pendingVariantIndex = null;
        SceneManager.LoadScene("Start");
    }

    private void OnGameStateChanged(GameState state)
    {
        if (state == GameState.Victory) Progression.CompleteBattle();
        else if (state == GameState.Defeat && Progression.Defeat()) ShowInterlude();
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

    private void EnterBattle()
    {
        LevelData level = Progression.CurrentLevel;
        if (level == null) { Debug.LogError("[RunFlow] Current battle level missing."); return; }
        int variant = Progression.CurrentVariantIndex;
        if (variant < 0) { Debug.LogError("[RunFlow] Current battle has no variants."); return; }
        LevelManager.pendingNextLevel = level;
        LevelManager.pendingVariantIndex = variant;
        RunFlowUI ui = GetComponent<RunFlowUI>();
        if (ui != null) ui.Hide();
        SceneManager.LoadScene("Battle");
    }
}
