using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 远程投射物的冒烟测试（批处理用，也能在编辑器里点菜单跑）：
///   进 Play → 判定一张远程卡 / 一张近战卡 → 朝敌人发射一枚投射物
///   → 采样它的高度，验证走的是抛物线（先升后降）→ 等它销毁、爆散粒子出现。
///
/// 用法：
///   Unity.exe -batchmode -nographics -projectPath &lt;项目&gt; -executeMethod ProjectileVFXTest.Run
/// 结果看日志里的 [ProjectileVFXTest] 行。
/// </summary>
public static class ProjectileVFXTest
{
    private const string ScenePath = "Assets/Game/Scenes/Battle.unity";
    private const string RangedCardPath = "Assets/Game/ScriptableObjects/技能/攻击技能/矢驰.asset";
    private const string MeleeCardPath = "Assets/Game/ScriptableObjects/技能/攻击技能/劈砍.asset";
    private const string Key = "ProjectileVFXTest.state";

    private static bool Running
    {
        get => SessionState.GetBool(Key + ".running", false);
        set => SessionState.SetBool(Key + ".running", value);
    }

    private static int Phase
    {
        get => SessionState.GetInt(Key + ".phase", 0);
        set => SessionState.SetInt(Key + ".phase", value);
    }

    private static float PhaseStart
    {
        get => SessionState.GetFloat(Key + ".time", 0f);
        set => SessionState.SetFloat(Key + ".time", value);
    }

    private static float RunStart
    {
        get => SessionState.GetFloat(Key + ".run", 0f);
        set => SessionState.SetFloat(Key + ".run", value);
    }

    private static float startY;
    private static float launchY;
    private static bool failed;

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Game/测试 远程投射物 (Projectile Test)")]
    public static void Run()
    {
        // 编辑器里手点菜单时先问一下要不要保存当前场景；批处理模式直接开
        if (!Application.isBatchMode)
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Running = true;
        Phase = 0;
        failed = false;
        RunStart = (float)EditorApplication.timeSinceStartup;
        PhaseStart = RunStart;
        Debug.Log("[ProjectileVFXTest] 开始：进 Play → 发射一枚投射物 → 检查抛物线");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            PhaseStart = (float)EditorApplication.timeSinceStartup;
    }

    private static void Tick()
    {
        if (!Running) return;

        float now = (float)EditorApplication.timeSinceStartup;
        if (now - RunStart > 60f)
        {
            Debug.LogError("[ProjectileVFXTest] 超时");
            Finish(false);
            return;
        }

        switch (Phase)
        {
            case 0:   // 等布阵完成
                if (EditorApplication.isPlaying && now - PhaseStart > 1.2f)
                {
                    Fire();
                    Phase = 1;
                    PhaseStart = now;
                }
                break;

            case 1:   // 弧顶：应该比出发点和落点都高
                if (now - PhaseStart > expectedDuration * 0.45f)
                {
                    apexY = projectile != null ? projectile.position.y : float.NaN;
                    CheckApex();
                    Phase = 2;
                    PhaseStart = now;
                }
                break;

            case 2:   // 接近落地：应该已经比弧顶低（先升后降）
                if (now - PhaseStart > expectedDuration * 0.45f)
                {
                    CheckDescent();
                    Phase = 3;
                    PhaseStart = now;
                }
                break;

            case 3:   // 落地后：投射物应该已经销毁、爆散粒子出现过
                if (now - PhaseStart > 0.5f)
                {
                    CheckImpact();
                    Phase = 4;
                    EditorApplication.isPlaying = false;
                }
                break;

            case 4:
                if (!EditorApplication.isPlaying)
                    Finish(!failed);
                break;
        }
    }

    private static ProjectileSpawner spawner;
    private static Transform projectile;
    private static Vector3 targetPosition;
    private static float expectedDuration = 0.3f;
    private static float apexY;

    private static void Fire()
    {
        var ranged = AssetDatabase.LoadAssetAtPath<CardData>(RangedCardPath);
        var melee = AssetDatabase.LoadAssetAtPath<CardData>(MeleeCardPath);
        var player = Object.FindObjectOfType<PlayerUnit>();
        var enemy = Object.FindObjectOfType<EnemyUnit>();

        spawner = Object.FindObjectOfType<ProjectileSpawner>();
        if (spawner == null) { Debug.LogError("[ProjectileVFXTest] 场景里没有 ProjectileSpawner"); failed = true; return; }
        if (player == null || enemy == null) { Debug.LogError("[ProjectileVFXTest] 场上缺玩家或敌人"); failed = true; return; }

        Debug.Log($"[ProjectileVFXTest] 远程判定：{RangedCardPath} → {spawner.IsRanged(ranged)}（应为 True），" +
                  $"{MeleeCardPath} → {spawner.IsRanged(melee)}（应为 False）");
        if (!spawner.IsRanged(ranged) || spawner.IsRanged(melee))
            failed = true;

        targetPosition = enemy.transform.position;
        launchY = player.transform.position.y;
        startY = (launchY + targetPosition.y) * 0.5f;   // 抛物线两端连线的中点高度

        // 采样点必须落在飞行过程中：先把预计时长算出来（和生成器同一套算法）
        float distance = Vector2.Distance(player.transform.position, targetPosition);
        expectedDuration = ProjectileVFX.ComputeDuration(
            distance, spawner.speed, spawner.minDuration, spawner.maxDuration);
        Debug.Log($"[ProjectileVFXTest] 目标距离 {distance:F2}，预计飞行 {expectedDuration:F2} 秒");

        var vfx = spawner.Launch(player, targetPosition, enemy, ranged);
        projectile = vfx != null ? vfx.transform : null;
        Debug.Log($"[ProjectileVFXTest] 已发射：起点 {player.transform.position.y:F2} → 目标 {targetPosition.y:F2}");
    }

    private static void CheckApex()
    {
        if (projectile == null) { Debug.LogError("[ProjectileVFXTest] 没拿到投射物对象"); failed = true; return; }

        float y = projectile.position.y;
        bool higher = y > startY;
        Debug.Log($"[ProjectileVFXTest] 飞行中 y={y:F3}，两端连线中点 y={startY:F3} → " +
                  (higher ? "比连线更高（抛物线弧顶 ✔）" : "没有高于连线 ✗"));
        if (!higher) failed = true;
    }

    /// <summary>接近落地时应该已经往下走了（先升后降才是抛物线）</summary>
    private static void CheckDescent()
    {
        if (projectile == null)
        {
            // 已经落地销毁也算正常，交给 CheckImpact 判定
            Debug.Log("[ProjectileVFXTest] 采样时投射物已落地并销毁（飞行较短时正常）");
            return;
        }

        float y = projectile.position.y;
        bool descending = !float.IsNaN(apexY) && y < apexY;
        Debug.Log($"[ProjectileVFXTest] 接近落地 y={y:F3}，弧顶 y={apexY:F3} → " +
                  (descending ? "已经下降（先升后降 ✔）" : "还没落下来 ✗"));
        if (!descending) failed = true;
    }

    private static void CheckImpact()
    {
        int projectiles = 0, impacts = 0;
        foreach (Transform t in Object.FindObjectsOfType<Transform>())
        {
            if (t.name == "Projectile") projectiles++;
            if (t.name == "ProjectileImpact") impacts++;
        }

        Debug.Log($"[ProjectileVFXTest] 落地后：场上投射物 {projectiles} 个（应为 0）、命中特效 {impacts} 个（应 ≥1）");
        if (projectiles != 0 || impacts < 1) failed = true;
    }

    private static void Finish(bool ok)
    {
        Running = false;
        Phase = 0;
        if (ok) Debug.Log("[ProjectileVFXTest] 通过：抛物线 + 命中爆散都正常");
        // 只有批处理才关掉编辑器；在编辑器里手点时把结果留在 Console
        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }
}
