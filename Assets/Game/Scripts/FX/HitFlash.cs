using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 受击闪红（Shader Graph 版）——挂在玩家 / 敌人 / 障碍物的根物体上（和 SpriteRenderer 同一个 GameObject）
///
/// 特点：不需要改 Unit.cs / Obstacle.cs，全部靠"观察"现有代码来触发：
///   1) 单位（玩家 / 敌人）：订阅 EventManager 的 "UnitDamaged" 事件
///      —— Unit.TakeDamage 结尾本来就会广播这个事件，带上的参数就是受伤的单位
///   2) 障碍物：没有受击事件，所以每帧比对血量 / 护盾，发现变少就闪
///      —— 柱子这类"无限血量"的障碍物血量永远不变，需要调用方自己 Play()
///   3) 其它任何情况（技能、陷阱、卡牌……）：外部直接调用 Play()
///
/// 表现：flashInDuration 秒内 _FlashAmount 从 0 → 1（完全变成 Flash Color），
///       再在 flashOutDuration 秒内从 1 → 0（变回原样）
///
/// 用哪个材质：
///   Flash Material Template 留空，或当前不是 URP 时，会自动退回"改 SpriteRenderer 顶点色"的闪法，
///   所以就算材质没配好、或者项目被切回内建管线，也不会变成一片粉色，只是闪得没那么实。
/// </summary>
[DisallowMultipleComponent]
public class HitFlash : MonoBehaviour
{
    [Header("闪红参数")]
    [Tooltip("变红用时（秒）")]
    public float flashInDuration = 0.1f;

    [Tooltip("恢复用时（秒）")]
    public float flashOutDuration = 0.1f;

    [Tooltip("受击时闪的颜色（会写进材质的 _FlashColor）")]
    public Color flashColor = Color.red;

    [Header("引用")]
    [Tooltip("Shader Graph 生成的材质（模板）。运行时会为每个对象克隆一份实例")]
    public Material flashMaterialTemplate;

    [Tooltip("要变色的 SpriteRenderer，留空自动在自身和子物体里查找")]
    public SpriteRenderer target;

    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    private Material instance;               // 每个对象一份材质实例，避免所有单位一起闪
    private Color baseColor = Color.white;   // 顶点色兜底用的原始颜色
    private bool useMaterial;                // true = 走 Shader Graph 材质，false = 走顶点色
    private float currentAmount;             // 当前闪红进度（0 = 原色，1 = 全闪）

    private Unit unit;                       // 同一个物体上的单位脚本（玩家 / 敌人）
    private Obstacle obstacle;               // 同一个物体上的障碍物脚本
    private int lastHealth;
    private int lastShield;
    private bool hasSnapshot;

    private Coroutine routine;

    // ============================================================
    //                       初始化
    // ============================================================

    private void Awake()
    {
        if (target == null)
            target = GetComponentInChildren<SpriteRenderer>();

        if (target == null)
        {
            enabled = false;   // 没有 SpriteRenderer 就没得闪
            return;
        }

        baseColor = target.color;
        unit = GetComponent<Unit>();
        obstacle = GetComponent<Obstacle>();

        // 只有 URP 真的生效时才把 Shader Graph 材质换上去：
        // 万一项目还是内建管线，URP 材质的精灵会整片变粉，那时候宁可退回顶点色方案
        useMaterial = flashMaterialTemplate != null && IsUrpActive();

        if (useMaterial)
        {
            instance = new Material(flashMaterialTemplate);
            if (instance.HasProperty(FlashColorId)) instance.SetColor(FlashColorId, flashColor);
            if (instance.HasProperty(FlashAmountId)) instance.SetFloat(FlashAmountId, 0f);
            target.material = instance;
        }
    }

    private void OnEnable()
    {
        // 单位受击：TakeDamage 里的 "UnitDamaged" 事件，零改动就能拿到通知
        if (unit != null)
            EventManager.AddListener<Unit>("UnitDamaged", OnUnitDamaged);
    }

    private void OnDisable()
    {
        if (unit != null)
            EventManager.RemoveListener<Unit>("UnitDamaged", OnUnitDamaged);
    }

    private void OnDestroy()
    {
        // 克隆出来的材质要自己销毁，否则会漏
        if (instance != null)
            Destroy(instance);
    }

    private void OnUnitDamaged(Unit damaged)
    {
        if (damaged == unit)
            Play();
    }

    /// <summary>
    /// 障碍物没有受击事件，靠对比血量 / 护盾发现掉血
    /// （无限血量的柱子永远检测不到，那种情况请让伤害来源直接调 Play()）
    /// </summary>
    private void Update()
    {
        if (unit == null && obstacle == null) return;

        int health = unit != null ? unit.GetCurrentHealth() : obstacle.GetCurrentHealth();
        int shield = unit != null ? unit.GetShield() : 0;

        if (hasSnapshot && (health < lastHealth || shield < lastShield))
            Play();

        lastHealth = health;
        lastShield = shield;
        hasSnapshot = true;
    }

    // ============================================================
    //                       对外接口
    // ============================================================

    /// <summary>
    /// 播一次受击闪红（0.1 秒变红 → 0.1 秒恢复）
    /// 正在闪的时候再次受击会从当前进度重新开始，连击 / 多段伤害不会卡住
    /// </summary>
    public void Play()
    {
        if (!isActiveAndEnabled || target == null) return;

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(FlashRoutine());
    }

    /// <summary>
    /// 立刻停止闪红并恢复原色
    /// </summary>
    public void StopFlash()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        SetFlash(0f);
    }

    // ============================================================
    //                       内部实现
    // ============================================================

    private IEnumerator FlashRoutine()
    {
        yield return FadeTo(1f, flashInDuration);    // 变红
        yield return FadeTo(0f, flashOutDuration);   // 变回正常颜色
        routine = null;
    }

    /// <summary>
    /// 把闪红进度插值到目标值
    /// 用 unscaledDeltaTime：暂停（Time.timeScale = 0）时也能正常播完，不会卡在红色
    /// </summary>
    private IEnumerator FadeTo(float amount, float duration)
    {
        float from = currentAmount;

        if (duration <= 0f)
        {
            SetFlash(amount);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetFlash(Mathf.Lerp(from, amount, elapsed / duration));
            yield return null;
        }

        SetFlash(amount);
    }

    /// <summary>
    /// 应用闪红进度：0 = 原始外观，1 = 完全变成 flashColor
    /// </summary>
    private void SetFlash(float amount)
    {
        currentAmount = amount;

        if (useMaterial)
        {
            if (instance != null)
                instance.SetFloat(FlashAmountId, amount);
        }
        else if (target != null)
        {
            target.color = Color.Lerp(baseColor, flashColor, amount);
        }
    }

    /// <summary>
    /// 当前是不是 URP 在生效
    /// （按类型名判断，不用 using URP 命名空间，包被移除也能编译）
    /// </summary>
    private static bool IsUrpActive()
    {
        RenderPipelineAsset pipeline = QualitySettings.renderPipeline != null
            ? QualitySettings.renderPipeline
            : GraphicsSettings.defaultRenderPipeline;

        return pipeline != null
            && pipeline.GetType().FullName.StartsWith("UnityEngine.Rendering.Universal.");
    }
}
