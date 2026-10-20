using System.Collections;
using UnityEngine;

/// <summary>
/// 远程攻击的抛物线投射物——"会飞的粒子源"。
///
/// 组成：
///   · 核心：一张正方形贴图（可换成你连好的 Shader Graph 材质，脚本只驱动 _Glow 一个属性）
///   · 跟随粒子：一个 Local 空间（simulationSpace = Local）的粒子系统，用正方形贴图，
///     在球体体积内随机生成 → 一群灰色方块"不规则聚集"在粒子源周围，随它一起飞
///   · 命中：落点再爆散一次粒子，然后自销毁，并回调 onImpact
///
/// 抛物线用的是真正的二次曲线（两端为 0、中间最高）：
///   p(t) = Lerp(起点, 终点, t) + up * 4 * apex * t * (1 - t)
///
/// 注意：伤害是瞬时结算的（方案 C），本组件只负责表现；命中回调用来把目标的
/// "受击闪红"延后到方块真正落下的这一刻（见 ProjectileSpawner.Launch）。
/// </summary>
public class ProjectileVFX : MonoBehaviour
{
    [Header("飞行")]
    [Tooltip("飞行速度（世界单位/秒），实际时长会被 Min/Max Duration 夹住")]
    public float speed = 10f;
    [Tooltip("最短飞行时间——相邻格互射也要看得清，别设太小")]
    public float minDuration = 0.25f;
    public float maxDuration = 0.6f;
    [Tooltip("弧顶高度 = 飞行距离 × 这个比例")]
    public float apexRatio = 0.25f;
    public float minApex = 0.6f;
    public float maxApex = 2f;
    [Tooltip("飞行中自转速度（度/秒）")]
    public float spinSpeed = 540f;

    [Header("核心方块")]
    public bool drawCore = true;
    [Tooltip("核心方块的大小（世界单位）")]
    public float coreSize = 0.28f;
    public Sprite squareSprite;
    [Tooltip("核心材质：把你连好的 Shader Graph 材质放这里；留空用默认 sprite 材质")]
    public Material coreMaterial;
    public Color coreColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    [Tooltip("Shader Graph 里发光强度的属性名")]
    public string glowProperty = "_Glow";

    [Header("跟随的灰色方块粒子")]
    public int clusterMaxParticles = 64;
    public float clusterRate = 45f;
    [Tooltip("粒子源周围聚集的球体半径")]
    public float clusterRadius = 0.28f;
    public Vector2 clusterSizeRange = new Vector2(0.06f, 0.16f);
    public Vector2 clusterLifetimeRange = new Vector2(0.25f, 0.6f);
    public Vector2 clusterSpeedRange = new Vector2(0.02f, 0.3f);
    public Color clusterColor = new Color(0.72f, 0.72f, 0.72f, 0.95f);
    [Tooltip("粒子材质（URP Particles/Unlit + 正方形贴图），菜单 Game/特效/投射物：准备资源 会自动建")]
    public Material particleMaterial;

    [Header("命中")]
    public int impactParticleCount = 16;
    public float impactRadius = 0.06f;
    public Vector2 impactSizeRange = new Vector2(0.08f, 0.2f);
    public Color impactColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    [Tooltip("命中粒子残留多久（秒）")]
    public float impactLingerTime = 0.7f;

    [Header("层级")]
    public int sortingOrder = 20;

    private Vector2 from;
    private Vector2 to;
    private float duration;
    private float apex;
    private Color tint = Color.white;
    private System.Action onImpact;

    private SpriteRenderer core;
    private ParticleSystem cluster;
    private bool visualsReady;
    private MaterialPropertyBlock propertyBlock;
    private int glowId;
    private static Sprite fallbackSquare;
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    /// <summary>飞行时长（生成器要用同一个算法算"闪红延后多久"，所以做成静态方法）</summary>
    public static float ComputeDuration(float distance, float speed, float min, float max)
    {
        return Mathf.Clamp(distance / Mathf.Max(0.01f, speed), min, max);
    }

    /// <summary>把生成器上的默认资源/参数灌进来（用代码建投射物时调，用预制体时不必调）</summary>
    public void Configure(Sprite square, Material particleMat, Material coreMat,
                          float flightSpeed, float arcRatio, int order,
                          float flightMinDuration, float flightMaxDuration)
    {
        if (square != null) squareSprite = square;
        if (particleMat != null) particleMaterial = particleMat;
        if (coreMat != null) coreMaterial = coreMat;
        speed = flightSpeed;
        // 时长上下限也要同步：生成器用这两个值算"闪红延后多久"，对不上就会不同步
        minDuration = flightMinDuration;
        maxDuration = flightMaxDuration;
        apexRatio = arcRatio;
        sortingOrder = order;
    }

    /// <summary>发射：从 start 抛物线飞到 end，落地后回调 impactCallback</summary>
    public void Launch(Vector2 start, Vector2 end, Color color, System.Action impactCallback)
    {
        from = start;
        to = end;
        tint = color;
        onImpact = impactCallback;

        float distance = Vector2.Distance(start, end);
        duration = ComputeDuration(distance, speed, minDuration, maxDuration);
        apex = Mathf.Clamp(distance * apexRatio, minApex, maxApex);

        BuildVisuals();
        transform.position = new Vector3(start.x, start.y, transform.position.z);
        StartCoroutine(FlightRoutine());
    }

    // ============================================================
    //                          搭建
    // ============================================================

    /// <summary>
    /// 搭出 Core / Cluster 两个子物体。预制体里已经搭好时直接取引用，不重复搭。
    /// 编辑器菜单"投射物：生成预制体"也会调它，所以是 public。
    /// </summary>
    public void BuildVisuals()
    {
        if (visualsReady) return;
        visualsReady = true;

        glowId = Shader.PropertyToID(glowProperty);
        if (transform.childCount > 0)     // 预制体里已经搭好了
        {
            core = GetComponentInChildren<SpriteRenderer>();
            cluster = GetComponentInChildren<ParticleSystem>();
            return;
        }

        if (drawCore)
        {
            var coreGo = new GameObject("Core");
            coreGo.transform.SetParent(transform, false);
            coreGo.transform.localScale = Vector3.one * coreSize;

            core = coreGo.AddComponent<SpriteRenderer>();
            core.sprite = squareSprite != null ? squareSprite : (fallbackSquare != null ? fallbackSquare : (fallbackSquare = BuildSquareSprite()));
            core.color = coreColor * tint;
            core.sortingOrder = sortingOrder;
            if (coreMaterial != null)
                core.sharedMaterial = coreMaterial;
        }

        BuildCluster();
    }

    private void BuildCluster()
    {
        var clusterGo = new GameObject("Cluster");
        clusterGo.transform.SetParent(transform, false);
        cluster = clusterGo.AddComponent<ParticleSystem>();

        // AddComponent 出来的粒子系统会立刻开始播放，这时改 duration 会被 Unity 拒绝并报
        // "Setting the duration while system is still playing is not supported"。
        // 先停掉并清空（配置完下面再 Play），才能安全地写 duration。
        cluster.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = cluster.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(clusterLifetimeRange.x, clusterLifetimeRange.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(clusterSpeedRange.x, clusterSpeedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(clusterSizeRange.x, clusterSizeRange.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = clusterColor * tint;
        main.gravityModifier = 0f;
        main.maxParticles = clusterMaxParticles;
        // Local：粒子跟着粒子源一起走，才会"围着它聚"，而不是拖在身后
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = cluster.emission;
        emission.rateOverTime = clusterRate;

        // 球体体积内随机 → 聚得"不规则"
        var shape = cluster.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = clusterRadius;
        shape.radiusThickness = 1f;

        // 每颗方块自己乱转
        var rotation = cluster.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

        var sizeOverLife = cluster.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

        var colorOverLife = cluster.colorOverLifetime;
        colorOverLife.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = new ParticleSystem.MinMaxGradient(fade);

        var renderer = cluster.GetComponent<ParticleSystemRenderer>();
        renderer.material = particleMaterial != null ? particleMaterial : BuildFallbackParticleMaterial();
        renderer.sortingOrder = sortingOrder + 1;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        if (Application.isPlaying)
            cluster.Play();
    }

    // ============================================================
    //                          飞行
    // ============================================================

    private IEnumerator FlightRoutine()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            Vector2 p = Vector2.Lerp(from, to, t);
            p.y += 4f * apex * t * (1f - t);          // 二次曲线：两端为 0，中间最高
            transform.position = new Vector3(p.x, p.y, transform.position.z);

            if (!Mathf.Approximately(spinSpeed, 0f))
                transform.Rotate(0f, 0f, -spinSpeed * Time.deltaTime);

            SetCoreFloat(glowId, Mathf.Sin(t * Mathf.PI));   // 发光随飞行先强后弱

            yield return null;
        }

        transform.position = new Vector3(to.x, to.y, transform.position.z);
        PlayImpact();

        onImpact?.Invoke();      // ← 受击闪红延后到这一刻播

        yield return new WaitForSeconds(0.06f);
        Destroy(gameObject);
    }

    private void SetCoreFloat(int propertyId, float value)
    {
        if (core == null) return;
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        core.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(propertyId, value);
        core.SetPropertyBlock(propertyBlock);
    }

    /// <summary>命中爆散：一次性的世界空间粒子，播完自己销毁</summary>
    private void PlayImpact()
    {
        var go = new GameObject("ProjectileImpact");
        go.transform.SetParent(transform.parent, false);
        go.transform.position = new Vector3(to.x, to.y, transform.position.z);

        var ps = go.AddComponent<ParticleSystem>();
        // 同上：先停掉并清空，才改得动 duration（末尾 ps.Play() 会重新开播）
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = 0.3f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(impactSizeRange.x, impactSizeRange.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = impactColor * tint;
        main.gravityModifier = 0.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)impactParticleCount) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = impactRadius;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = particleMaterial != null ? particleMaterial : BuildFallbackParticleMaterial();
        renderer.sortingOrder = sortingOrder + 2;

        ps.Play();
        Destroy(go, impactLingerTime + 1f);
    }

    // ============================================================
    //                        兜底资源
    // ============================================================

    /// <summary>没给正方形贴图时，运行时生成一张灰方块，保证特效不会空白</summary>
    private static Sprite BuildSquareSprite(int size = 64)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside = x >= 10 && x <= 53 && y >= 10 && y <= 53;
                bool border = x == 10 || x == 53 || y == 10 || y == 53;
                Color32 c = new Color32(0, 0, 0, 0);
                if (inside)
                {
                    byte g = (byte)Mathf.Lerp(120, 210, y / (float)(size - 1));
                    c = border ? new Color32(58, 58, 58, 255) : new Color32(g, g, g, 255);
                }
                pixels[y * size + x] = c;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        // PPU 44 和美术资源那边保持一致：贴图中间的 44×44 方块 = 1 个世界单位
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 44f);
    }

    /// <summary>
    /// 没配粒子材质时的兜底：URP 的 Particles/Unlit + 正方形贴图。
    /// 这里要读实例上的 squareSprite，所以不是 static。
    /// </summary>
    private Material BuildFallbackParticleMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) return null;

        var material = new Material(shader);
        var texture = squareSprite != null ? squareSprite.texture : null;
        if (texture == null) texture = fallbackSquare != null ? fallbackSquare.texture : null;
        if (texture != null) material.SetTexture(BaseMapId, texture);
        return material;
    }
}
