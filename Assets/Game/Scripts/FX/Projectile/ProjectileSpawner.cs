using UnityEngine;

/// <summary>
/// 远程攻击投射物的生成器——靠监听卡牌结算事件工作，不碰任何结算代码。
///
/// 判定"远程"：攻击卡且最大射程 &gt; rangedMaxRangeThreshold（默认 1，也就是近战不飞）。
/// 接入点（CardData.Resolve 里广播的现成事件）：
///   "CardResolved"           (CardData, Unit caster, Unit target)
///   "CardResolvedOnObstacle" (CardData, Unit caster, Obstacle obstacle)
/// 玩家和敌人用的是同一套 Resolve，所以敌人的远程技能（矢驰 / 抛射 …）同样会飞投射物。
///
/// 错位处理（方案 C）：伤害照旧瞬时结算，但会把飞行时长写进目标的 HitFlash，
/// 让"受击闪红"等到方块落下那一刻再播——只动表现层，不改结算时序。
///
/// 场景里没有本组件时会自动创建一个（RuntimeInitializeOnLoadMethod），
/// 编辑器菜单“Game/特效/投射物：准备资源”也会把它挂到 Managers 上，方便在 Inspector 调参。
/// </summary>
[DisallowMultipleComponent]
public class ProjectileSpawner : MonoBehaviour
{
    public static ProjectileSpawner Instance { get; private set; }

    [Header("远程判定")]
    [Tooltip("攻击卡最大射程大于这个值就算远程，会飞投射物")]
    public int rangedMaxRangeThreshold = 1;

    [Tooltip("这些名字的卡不飞投射物")]
    public string[] excludeCardNames = new string[0];

    [Header("外观")]
    [Tooltip("投射物预制体（菜单 Game/特效/投射物：生成预制体 会建好并连上）；留空则运行时用代码搭")]
    public GameObject projectilePrefab;

    [Tooltip("正方形贴图（粒子与核心方块共用）")]
    public Sprite squareSprite;

    [Tooltip("粒子材质（URP Particles/Unlit + 正方形贴图）")]
    public Material particleMaterial;

    [Tooltip("核心方块材质（你连好的 Shader Graph）；留空则用默认 sprite 材质")]
    public Material coreMaterial;

    [Header("飞行手感")]
    public float speed = 10f;
    [Tooltip("最短飞行时间——相邻格互射也要看得清")]
    public float minDuration = 0.25f;
    public float maxDuration = 0.6f;
    [Tooltip("弧顶高度 = 距离 × 这个比例")]
    public float apexRatio = 0.25f;

    [Header("颜色")]
    public Color playerProjectileColor = Color.white;
    public Color enemyProjectileColor = new Color(1f, 0.92f, 0.85f, 1f);

    [Header("层级")]
    public int sortingOrder = 20;

    [Header("调试")]
    [Tooltip("每次生成投射物时在 Console 打一行日志")]
    public bool logEveryShot = false;

    private Transform vfxRoot;

    /// <summary>场景里没有就自动建一个，保证"远程攻击会飞方块"开箱即用</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindObjectOfType<ProjectileSpawner>() != null)
            return;
        var go = new GameObject("ProjectileSpawner");
        go.AddComponent<ProjectileSpawner>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EventManager.AddListener<CardData, Unit, Unit>("CardResolved", OnCardResolved);
        EventManager.AddListener<CardData, Unit, Obstacle>("CardResolvedOnObstacle", OnCardResolvedOnObstacle);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<CardData, Unit, Unit>("CardResolved", OnCardResolved);
        EventManager.RemoveListener<CardData, Unit, Obstacle>("CardResolvedOnObstacle", OnCardResolvedOnObstacle);
    }

    // ============================================================
    //                        事件入口
    // ============================================================

    private void OnCardResolved(CardData card, Unit caster, Unit target)
    {
        if (caster == null || target == null || !IsRanged(card))
            return;

        Launch(caster, target.transform.position, target, card);
    }

    private void OnCardResolvedOnObstacle(CardData card, Unit caster, Obstacle obstacle)
    {
        if (caster == null || obstacle == null || !IsRanged(card))
            return;

        Launch(caster, obstacle.transform.position, null, card);
    }

    /// <summary>是不是远程攻击（要飞投射物）</summary>
    public bool IsRanged(CardData card)
    {
        if (card == null || !card.IsAttack) return false;
        if (card.maxRange <= rangedMaxRangeThreshold) return false;

        if (excludeCardNames != null)
        {
            foreach (string name in excludeCardNames)
                if (!string.IsNullOrEmpty(name) && name == card.cardName)
                    return false;
        }
        return true;
    }

    // ============================================================
    //                        生成投射物
    // ============================================================

    /// <param name="target">目标单位；打障碍物时传 null</param>
    public ProjectileVFX Launch(Unit caster, Vector3 targetPosition, Unit target, CardData card)
    {
        Vector3 start = caster.transform.position;
        float distance = Vector2.Distance(start, targetPosition);
        float duration = ProjectileVFX.ComputeDuration(distance, speed, minDuration, maxDuration);

        // 方案 C：伤害已经瞬时生效了，但让目标"等方块落下来再闪红"
        if (target != null)
        {
            var flash = target.GetComponent<HitFlash>();
            if (flash != null)
                flash.DelayNextFlash(duration);
        }

        ProjectileVFX projectile = CreateProjectile();
        Color color = caster is PlayerUnit ? playerProjectileColor : enemyProjectileColor;
        projectile.Launch(start, targetPosition, color, null);

        if (logEveryShot)
            Debug.Log($"[ProjectileSpawner] {caster.name} 用【{(card != null ? card.cardName : "?")}】扔出方块，飞行 {duration:F2} 秒");

        return projectile;
    }

    private ProjectileVFX CreateProjectile()
    {
        if (vfxRoot == null)
        {
            var root = GameObject.Find("VFX");
            if (root == null) root = new GameObject("VFX");
            vfxRoot = root.transform;
        }

        GameObject go = projectilePrefab != null
            ? Instantiate(projectilePrefab, vfxRoot)
            : new GameObject("Projectile");
        go.transform.SetParent(vfxRoot, false);

        var projectile = go.GetComponent<ProjectileVFX>();
        if (projectile == null)
            projectile = go.AddComponent<ProjectileVFX>();
        projectile.Configure(squareSprite, particleMaterial, coreMaterial,
                             speed, apexRatio, sortingOrder, minDuration, maxDuration);
        return projectile;
    }
}
