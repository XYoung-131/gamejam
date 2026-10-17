using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// buff 套环特效的冒烟测试（批处理用，也可以在编辑器里点菜单跑）。
///
/// 做的事情：打开战斗场景 → 进 Play → 给玩家施加一个负面 buff、给敌人施加一个负面 buff，
///          给玩家施加一个正面 buff → 检查生成的环数量、颜色和移动方向（红环向下、蓝环向上）。
///
/// 用法：
///   Unity.exe -batchmode -nographics -projectPath &lt;项目&gt; -executeMethod BuffRingTest.Run
/// 测试结果看日志里的 [BuffRingTest] 行；有问题会以退出码 1 结束。
/// </summary>
public static class BuffRingTest
{
    private const string ScenePath = "Assets/Game/Scenes/Battle.unity";
    private const string DebuffPath = "Assets/Game/ScriptableObjects/Buff/眩晕.asset";
    private const string BuffPath = "Assets/Game/ScriptableObjects/Buff/力量.asset";
    private const string StateKey = "BuffRingTest.state";
    private const string TimeKey = "BuffRingTest.time";

    private static bool Running
    {
        get => SessionState.GetBool(StateKey + ".running", false);
        set => SessionState.SetBool(StateKey + ".running", value);
    }

    private static int Phase
    {
        get => SessionState.GetInt(StateKey + ".phase", 0);
        set => SessionState.SetInt(StateKey + ".phase", value);
    }

    private static float PhaseStart
    {
        get => SessionState.GetFloat(TimeKey + ".phase", 0f);
        set => SessionState.SetFloat(TimeKey + ".phase", value);
    }

    private static float RunStart
    {
        get => SessionState.GetFloat(TimeKey + ".run", 0f);
        set => SessionState.SetFloat(TimeKey + ".run", value);
    }

    // 域重载会让静态字段清零，所以订阅放在 InitializeOnLoadMethod 里重新挂一次
    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Game/测试 buff 套环 (Buff Ring Test)")]
    public static void Run()
    {
        // 编辑器里手点菜单时先问一下要不要保存当前场景；批处理模式直接开
        if (!Application.isBatchMode)
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Running = true;
        Phase = 0;
        RunStart = (float)EditorApplication.timeSinceStartup;
        PhaseStart = RunStart;
        Debug.Log("[BuffRingTest] 开始：进 Play → 施加 buff → 检查套环");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            PhaseStart = (float)EditorApplication.timeSinceStartup;
        }
    }

    private static void Tick()
    {
        if (!Running)
            return;

        float now = (float)EditorApplication.timeSinceStartup;
        if (now - RunStart > 60f)
        {
            Debug.LogError("[BuffRingTest] 超时，退出");
            Finish(false);
            return;
        }

        switch (Phase)
        {
            case 0:   // 等进 Play + 等布阵完成
                if (EditorApplication.isPlaying && now - PhaseStart > 1.2f)
                {
                    ApplyBuffs();
                    Phase = 1;
                    PhaseStart = now;
                }
                break;

            case 1:   // 环在扫的过程中采样两次，看移动方向
                if (now - PhaseStart > 0.15f)
                {
                    SampleRings();
                    Phase = 2;
                    EditorApplication.isPlaying = false;
                }
                break;

            case 2:   // 退出 Play 后收尾
                if (!EditorApplication.isPlaying)
                {
                    Finish(true);
                }
                break;
        }
    }

    private static void ApplyBuffs()
    {
        var debuff = AssetDatabase.LoadAssetAtPath<BuffData>(DebuffPath);
        var buff = AssetDatabase.LoadAssetAtPath<BuffData>(BuffPath);

        if (debuff == null || buff == null)
        {
            Debug.LogError($"[BuffRingTest] 找不到 buff 资产：{DebuffPath} / {BuffPath}");
            return;
        }

        var player = Object.FindObjectOfType<PlayerUnit>();
        var enemies = Object.FindObjectsOfType<EnemyUnit>();

        if (player == null)
        {
            Debug.LogError("[BuffRingTest] 场上没有玩家单位");
            return;
        }

        player.ApplyBuff(debuff);        // 负面 → 红环
        player.ApplyBuff(buff);          // 正面 → 蓝环
        if (enemies.Length > 0)
            enemies[0].ApplyBuff(debuff); // 敌人身上再来一个红环

        Debug.Log($"[BuffRingTest] 已施加：玩家 负面+正面，敌人 {enemies.Length} 个（第一个挂负面）");
        SampleRings(true);
    }

    /// <summary>统计环对象，并记录它们的颜色和位置（用来验证红下蓝上）</summary>
    private static void SampleRings(bool first = false)
    {
        int red = 0, blue = 0;
        string detail = "";
        foreach (Transform t in Object.FindObjectsOfType<Transform>())
        {
            if (t.name != "BuffRing")
                continue;

            var renderer = t.GetComponent<SpriteRenderer>();
            if (renderer == null) continue;

            bool isRed = renderer.color.r > renderer.color.b;
            if (isRed) red++; else blue++;
            detail += $"\n    {(isRed ? "红" : "蓝")}环 y={t.position.y:F3} alpha={renderer.color.a:F2}";

            if (!first)
            {
                float previous = SessionState.GetFloat(StateKey + ".y" + t.GetInstanceID(), float.NaN);
                if (!float.IsNaN(previous))
                    detail += $"  相比0.15秒前 {(t.position.y - previous >= 0 ? "↑" : "↓")}{Mathf.Abs(t.position.y - previous):F3}";
            }
            else
            {
                SessionState.SetFloat(StateKey + ".y" + t.GetInstanceID(), t.position.y);
            }
        }

        Debug.Log($"[BuffRingTest] {(first ? "刚施加时" : "0.15 秒后")}：环共 {red + blue} 个（红 {red} / 蓝 {blue}）{detail}");

        if (!first)
        {
            if (red + blue < 3)
                Debug.LogError("[BuffRingTest] 期望至少 3 个环（玩家红+蓝、敌人红），实际不足");
            else
                Debug.Log("[BuffRingTest] 通过：红环(负面)与蓝环(正面)都生成了");
        }
    }

    private static void Finish(bool completed)
    {
        Running = false;
        Phase = 0;
        // 只有批处理才关掉编辑器；在编辑器里手点时把结果留在 Console
        if (Application.isBatchMode)
            EditorApplication.Exit(completed ? 0 : 1);
    }
}
