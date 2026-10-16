using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Buff 套环特效——单位被施加 buff 时，一圈光环从角色身上扫过：
///   负面 buff → 红色环，从上往下扫
///   正面 buff → 蓝色环，从下往上扫
///
/// 数据来源：监听 Unit.ApplyBuff 抛出的 "UnitBuffApplied" 事件（参数：被施加的单位, BuffData），
///           正负极性直接读 BuffData.IsDebuff（也就是 polarity 字段）。
///
/// 几个已定好的口径：
///   · 环的直径按角色 SpriteRenderer 的宽度自适应（大怪也不会"套不住"）；
///   · 同一种 buff 在 sameBuffCooldown 秒内只播一次——一次施加多层不会连闪；
///   · 环贴图默认在运行时用代码生成（白色圆环），想用美术图就给 ringSprite 拖一张，颜色仍由极性决定；
///   · 环会盖在角色上层，并且不会跟着"立牌翻转"一起被压扁。
///
/// 挂载位置：和 Unit 挂在同一个物体上（LevelManager 生成单位时会自动补）。
/// </summary>
public class BuffRingSweep : MonoBehaviour
{
    [Header("触发")]
    [Tooltip("同一种 buff 两次特效之间的最短间隔（秒），防止一次叠多层时连闪")]
    public float sameBuffCooldown = 0.15f;
    [Tooltip("同时最多播放几个环")]
    public int maxConcurrent = 4;

    [Header("颜色")]
    [Tooltip("负面 buff 的颜色（红）")]
    public Color debuffColor = new Color(1f, 0.28f, 0.28f, 0.95f);
    [Tooltip("正面 buff 的颜色（蓝）")]
    public Color buffColor = new Color(0.35f, 0.65f, 1f, 0.95f);

    [Header("表现")]
    [Tooltip("扫过一圈的时长（秒）")]
    public float sweepDuration = 0.4f;
    [Tooltip("环直径 = 角色贴图宽度 × 这个倍率")]
    public float diameterScale = 1.25f;
    [Tooltip("环的最小直径（世界单位）")]
    public float minDiameter = 0.5f;
    [Tooltip("起止位置超出角色上下边缘的距离（世界单位）")]
    public float overshoot = 0.3f;
    [Tooltip("自定义环贴图（留空 = 运行时生成白色圆环，再按极性染色）")]
    public Sprite ringSprite;

    private Unit unit;
    private SpriteRenderer unitSprite;
    private readonly Dictionary<string, float> lastPlayTime = new Dictionary<string, float>();
    private readonly List<GameObject> activeRings = new List<GameObject>();

    private static Sprite cachedRing;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        unitSprite = GetComponentInChildren<SpriteRenderer>();
    }

    private void OnEnable()
    {
        EventManager.AddListener<Unit, BuffData>("UnitBuffApplied", OnBuffApplied);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<Unit, BuffData>("UnitBuffApplied", OnBuffApplied);
        ClearActiveRings();
    }

    private void OnDestroy()
    {
        ClearActiveRings();
    }

    // ============================================================
    //                        事件入口
    // ============================================================

    private void OnBuffApplied(Unit target, BuffData buff)
    {
        if (unit == null || buff == null || target != unit)
            return;

        // 同一种 buff 的节流（一次施加多层时只播一次）
        string key = string.IsNullOrEmpty(buff.buffName) ? buff.name : buff.buffName;
        float last;
        if (lastPlayTime.TryGetValue(key, out last) && Time.time - last < sameBuffCooldown)
            return;
        lastPlayTime[key] = Time.time;

        if (activeRings.Count >= maxConcurrent)
            return;

        PlayRing(buff.IsDebuff);
    }

    /// <summary>播一圈套环：isDebuff = 红环自上而下，否则蓝环自下而上</summary>
    public void PlayRing(bool isDebuff)
    {
        if (!isActiveAndEnabled)
            return;
        StartCoroutine(SweepRoutine(isDebuff ? debuffColor : buffColor, isDebuff ? -1 : 1));
    }

    // ============================================================
    //                        扫过动画
    // ============================================================

    /// <param name="direction">+1 = 从下往上，-1 = 从上往下</param>
    private IEnumerator SweepRoutine(Color color, int direction)
    {
        GameObject ring = CreateRing(color);
        if (ring == null)
            yield break;

        activeRings.Add(ring);
        Transform ringTransform = ring.transform;
        SpriteRenderer ringRenderer = ring.GetComponent<SpriteRenderer>();

        // 角色当前的世界范围：决定环多大、从哪扫到哪
        Bounds bounds = unitSprite != null
            ? unitSprite.bounds
            : new Bounds(transform.position, new Vector3(0.8f, 0.8f, 0f));

        float diameter = Mathf.Max(minDiameter, bounds.size.x * diameterScale);
        float startY = direction > 0 ? bounds.min.y - overshoot : bounds.max.y + overshoot;
        float endY = direction > 0 ? bounds.max.y + overshoot : bounds.min.y - overshoot;

        float elapsed = 0f;
        while (elapsed < sweepDuration && ring != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / sweepDuration);

            Vector3 pos = transform.position;
            ringTransform.position = new Vector3(pos.x, Mathf.Lerp(startY, endY, t), pos.z);

            // 抵消单位自身的缩放：立牌翻转会把 x 压扁，环不该跟着被压
            Vector3 scale = transform.lossyScale;
            ringTransform.localScale = new Vector3(
                diameter / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
                diameter / Mathf.Max(0.0001f, Mathf.Abs(scale.y)),
                1f);

            Color c = color;
            c.a = color.a * Mathf.Sin(t * Mathf.PI);   // 扫过时淡入淡出
            ringRenderer.color = c;

            yield return null;
        }

        activeRings.Remove(ring);
        if (ring != null)
            Destroy(ring);
    }

    private GameObject CreateRing(Color color)
    {
        if (ringSprite == null)
        {
            if (cachedRing == null)
                cachedRing = BuildRingSprite();
            ringSprite = cachedRing;
        }

        var ring = new GameObject("BuffRing");
        ring.transform.SetParent(transform, false);

        var renderer = ring.AddComponent<SpriteRenderer>();
        renderer.sprite = ringSprite;
        renderer.color = color;
        if (unitSprite != null)
        {
            renderer.sortingLayerID = unitSprite.sortingLayerID;
            renderer.sortingOrder = unitSprite.sortingOrder + 1;   // 盖在角色上面
        }

        return ring;
    }

    private void ClearActiveRings()
    {
        foreach (GameObject ring in activeRings)
            if (ring != null) Destroy(ring);
        activeRings.Clear();
    }

    /// <summary>
    /// 运行时生成一张白色圆环贴图（带 1 像素抗锯齿）。
    /// 颜色不在这里定——运行时用 SpriteRenderer.color 染色，红/蓝共用这一张。
    /// </summary>
    private static Sprite BuildRingSprite(int size = 128)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float outer = size * 0.47f;
        float inner = outer - size * 0.075f;   // 环的粗细

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                // 内圈和外圈各羽化 1 像素
                float alpha = Mathf.Clamp01(outer - distance + 0.5f) *
                              Mathf.Clamp01(distance - inner + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // ============================================================
    //                  Inspector 右键测试（Play 中使用）
    // ============================================================

    [ContextMenu("测试：红环（负面 buff，从上往下）")]
    private void DebugPlayDebuff()
    {
        PlayRing(true);
    }

    [ContextMenu("测试：蓝环（正面 buff，从下往上）")]
    private void DebugPlayBuff()
    {
        PlayRing(false);
    }
}
