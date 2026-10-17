using System.Collections;
using UnityEngine;

/// <summary>
/// 立牌翻转特效——玩家或敌人被"打得太狠"时有概率整块翻面。
///
/// 触发规则（可在 Inspector 调）：
///   单次受到的伤害 > 自身最大血量 × 50%，且伤害 ≥ 3 点时才有机会触发；
///   每超过半血 1 点伤害，触发概率 +10%（第一点就是 10%，上限 100%）。
///   例：最大血量 10（半血 5）时——6 点伤害 → 10%，7 点 → 20%，10 点 → 50%。
///
/// 表现：像纸质立牌翻面——先横向压成一条竖线，再展开（展开后是镜像），
///       这算"一周"；一整段 0.3 秒里连转 4 周，转完回到原来的朝向。
///       实现上就是把 localScale.x 按余弦曲线从 1 走到 -1 再走回来：
///       每一周正好是余弦的半个周期（1 → 0 → -1）。
///
/// 挂载位置：挂在单位预制体的根物体上（和 Unit 同一个物体）。
/// 概率用的伤害：默认取"真正扣掉的血量"（被护盾挡掉的不算），
///               想按"打到身上的总伤害"算就把 countShieldAbsorbed 打开。
/// </summary>
public class StandeeFlip : MonoBehaviour
{
    [Header("触发条件")]
    [Tooltip("伤害要超过 最大血量 × 这个比例 才可能触发")]
    public float healthThresholdRatio = 0.5f;
    [Tooltip("伤害低于这个值一律不触发")]
    public int minDamage = 3;
    [Tooltip("每超过阈值 1 点伤害，触发概率增加多少（0.1 = 10%）")]
    public float chancePerExtraPoint = 0.1f;
    [Tooltip("被护盾挡掉的部分算不算在这伤害里（关掉 = 只按真正扣血的量算）")]
    public bool countShieldAbsorbed = false;
    [Tooltip("这一击把单位打死时是否也翻转")]
    public bool playWhenFatal = false;

    [Header("表现")]
    [Tooltip("整段翻转的时长（秒）")]
    public float duration = 0.3f;
    [Tooltip("整段里转几周（1 周 = 压成竖线再镜像展开）")]
    public int spins = 4;
    [Tooltip("压平时保留的最小宽度比例（0 = 完全压成一条线，会有一帧看不见）")]
    public float squashTo = 0.02f;

    private Unit unit;
    private Vector3 baseScale;
    private bool playing;

    /// <summary>正在翻转中</summary>
    public bool IsPlaying => playing;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        baseScale = transform.localScale;

        if (unit == null)
            Debug.LogWarning($"[StandeeFlip] {name} 上没有 Unit 组件，立牌翻转不会触发");
    }

    private void OnEnable()
    {
        EventManager.AddListener<Unit, int, int>("UnitDamagedDetail", OnUnitDamaged);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<Unit, int, int>("UnitDamagedDetail", OnUnitDamaged);
    }

    /// <summary>
    /// 受到伤害：先按规则掷一次概率，中了才开始翻转。
    /// incomingDamage = 减免/易伤之后、扣护盾之前的伤害；
    /// healthDamage  = 真正扣掉的血量。
    /// </summary>
    private void OnUnitDamaged(Unit damaged, int incomingDamage, int healthDamage)
    {
        if (playing || unit == null || damaged != unit)
            return;

        int damage = countShieldAbsorbed ? incomingDamage : healthDamage;
        float chance = RollChance(damage);
        if (chance <= 0f)
            return;

        // 这一击把单位打死了就不翻（要翻就把 playWhenFatal 打开）
        if (!playWhenFatal && unit.GetCurrentHealth() <= 0)
            return;

        if (Random.value <= chance)
            Play();
    }

    /// <summary>
    /// 按规则算触发概率（0 表示这条伤害不满足条件）。
    /// 抽出来是为了方便调试和以后做 UI 提示。
    /// </summary>
    public float RollChance(int damage)
    {
        if (unit == null || unit.GetMaxHealth() <= 0)
            return 0f;
        if (damage < minDamage)
            return 0f;

        float half = unit.GetMaxHealth() * healthThresholdRatio;
        if (damage <= half)
            return 0f;

        // 超过半血的点数（0.1 也算 1 点，和项目里其他取整规则一致）
        int extraPoints = Mathf.CeilToInt(damage - half);
        return Mathf.Clamp01(extraPoints * chancePerExtraPoint);
    }

    /// <summary>播放一次翻转（4 周，共 duration 秒）</summary>
    public void Play()
    {
        if (playing || !isActiveAndEnabled)
            return;
        StartCoroutine(FlipRoutine());
    }

    private IEnumerator FlipRoutine()
    {
        playing = true;
        baseScale = transform.localScale;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // spins 周 = 余弦走过 spins 个半周期：1 → 0 → -1 就是"一周"（压平再镜像展开）
            float phase = Mathf.Cos(t * Mathf.PI * spins);
            // |cos| 从 1 压到 0 再回来，正负号决定是正面还是镜像
            float scaleX = Mathf.Sign(phase) * Mathf.Lerp(squashTo, 1f, Mathf.Abs(phase));

            transform.localScale = new Vector3(baseScale.x * scaleX, baseScale.y, baseScale.z);
            yield return null;
        }

        transform.localScale = baseScale;   // 收尾：回到原朝向
        playing = false;
    }

    // ============================================================
    //                  Inspector 右键测试
    // ============================================================

    [ContextMenu("测试：播放一次翻转")]
    private void DebugPlay()
    {
        Play();
    }

    [ContextMenu("测试：打印这条伤害的触发概率")]
    private void DebugChance()
    {
        int max = unit != null ? unit.GetMaxHealth() : 0;
        string text = $"最大血量 {max}（半血 {max * healthThresholdRatio:F1}）：";
        for (int d = minDamage; d <= max; d++)
            text += $" {d}点={RollChance(d) * 100f:F0}%";
        Debug.Log(text);
    }
}
