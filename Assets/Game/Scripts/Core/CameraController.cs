using UnityEngine;

/// <summary>
/// 相机控制器——正交相机，支持拖拽平移、滚轮缩放、边界限制
/// 挂在 Main Camera 上
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("开局视野")]
    [Tooltip("开局把地图整体放大的倍数：1 = 保持场景里摆的视野；1.2 = 地图大 20%。相机拉近实现，" +
             "背景/格子/棋子/障碍等比放大，滚轮仍可自由缩放")]
    public float startZoom = 1.2f;

    [Header("移动")]
    [Tooltip("拖拽移动速度")]
    public float panSpeed = 1f;
    [Tooltip("键盘移动速度")]
    public float keyPanSpeed = 10f;
    [Tooltip("按键/摇杆的死区：低于这个值当作没输入，避免手柄漂移让相机自己慢慢跑")]
    public float panDeadzone = 0.15f;
    [Tooltip("开局把相机对准地图中心（推荐开着；关掉就保持场景里摆的位置）")]
    public bool centerOnMapAtStart = true;

    [Header("缩放")]
    [Tooltip("最小缩放（拉近）")]
    public float minZoom = 2f;
    [Tooltip("最大缩放（拉远）")]
    public float maxZoom = 15f;
    [Tooltip("缩放速度")]
    public float zoomSpeed = 1f;

    [Header("边界限制")]
    [Tooltip("是否限制相机移动范围")]
    public bool clampToBounds = true;
    public Vector2 minBounds = new Vector2(-10f, -10f);
    public Vector2 maxBounds = new Vector2(10f, 10f);

    // 组件引用
    private Camera cam;
    private float baseOrthoSize = -1f;   // Awake 记录的场景初始正交尺寸（startZoom 的基准）

    // 拖拽状态
    private bool isDragging = false;
    private Vector3 lastMousePosition;

    // 边界是否已经按真实格子算好（格子在 LevelManager.Start 里生成，可能比本脚本的 Start 晚）
    private bool boundsReady = false;

    // ============================================================
    //                     初始化
    // ============================================================

    private void Awake()
    {
        cam = GetComponent<Camera>();
        baseOrthoSize = cam.orthographicSize;   // 场景里摆的视野当作基准
    }

    private void Start()
    {
        // 开局按倍数拉近：除以 startZoom = 地图等比放大 startZoom 倍
        if (cam.orthographic && startZoom > 0f && baseOrthoSize > 0f)
            cam.orthographicSize = Mathf.Clamp(baseOrthoSize / startZoom, minZoom, maxZoom);

        AutoSetBounds();
    }

    // ============================================================
    //                     每帧更新
    // ============================================================

    private void Update()
    {
        // 暂停时不响应输入
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
            return;

        // 格子可能是后生成的，还没拿到真实边界前每帧重试一次
        if (!boundsReady)
            AutoSetBounds();

        HandleZoom();
        HandleDragPan();
        HandleKeyPan();

        if (clampToBounds)
            ClampPosition();
    }

    // ============================================================
    //                       缩放
    // ============================================================

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        if (Mathf.Abs(scroll) > 0.01f)
        {
            if (cam.orthographic)
            {
                // 正交相机：修改 size
                cam.orthographicSize -= scroll * zoomSpeed;
                cam.orthographicSize = Mathf.Clamp(
                    cam.orthographicSize, minZoom, maxZoom);
            }
            else
            {
                // 透视相机：前后移动
                transform.Translate(Vector3.forward * scroll * zoomSpeed * 10f, Space.Self);
            }
        }
    }

    // ============================================================
    //                     拖拽平移
    // ============================================================

    private void HandleDragPan()
    {
        // 中键或右键拖拽
        if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
        {
            isDragging = true;
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2))
        {
            isDragging = false;
        }

        if (isDragging)
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            Vector3 move = new Vector3(-delta.x, -delta.y, 0f) * panSpeed * 0.01f;

            // 根据相机朝向平移
            transform.Translate(move, Space.World);
            lastMousePosition = Input.mousePosition;
        }
    }

    // ============================================================
    //                     键盘平移
    // ============================================================

    private void HandleKeyPan()
    {
        float horizontal = Input.GetAxis("Horizontal"); // A/D 或 左右箭头
        float vertical = Input.GetAxis("Vertical");     // W/S 或 上下箭头

        // 死区：Input.GetAxis 会把摇杆的微小漂移也算进来，不加这个相机会自己慢慢移动
        if (Mathf.Abs(horizontal) < panDeadzone) horizontal = 0f;
        if (Mathf.Abs(vertical) < panDeadzone) vertical = 0f;

        if (Mathf.Abs(horizontal) > 0.01f || Mathf.Abs(vertical) > 0.01f)
        {
            Vector3 move = new Vector3(horizontal, vertical, 0f) * keyPanSpeed * Time.deltaTime;
            transform.Translate(move, Space.World);
        }
    }

    // ============================================================
    //                     边界限制
    // ============================================================

    private void ClampPosition()
    {
        Vector3 pos = transform.position;

        // 考虑当前缩放对视野范围的影响
        float viewHalfHeight = cam.orthographic ? cam.orthographicSize : 5f;
        float viewHalfWidth = viewHalfHeight * cam.aspect;

        float minX = minBounds.x + viewHalfWidth;
        float maxX = maxBounds.x - viewHalfWidth;
        float minY = minBounds.y + viewHalfHeight;
        float maxY = maxBounds.y - viewHalfHeight;

        // 视野比地图还大时上下限会算反，Mathf.Clamp 会因此每帧在两端来回跳（画面闪烁）；
        // 这种情况下直接把相机放到地图中心。
        pos.x = minX <= maxX ? Mathf.Clamp(pos.x, minX, maxX) : (minBounds.x + maxBounds.x) * 0.5f;
        pos.y = minY <= maxY ? Mathf.Clamp(pos.y, minY, maxY) : (minBounds.y + maxBounds.y) * 0.5f;

        transform.position = pos;
    }

    // ============================================================
    //                      辅助方法
    // ============================================================

    /// <summary>
    /// 根据网格大小自动设置边界
    /// </summary>
    public void AutoSetBounds()
    {
        if (HexGrid.Instance == null) return;

        // 优先用真实格子的包围盒（考虑了画稿的偏移、纵向压缩和自定义形状）
        if (HexGrid.Instance.TryGetWorldBounds(out Bounds mapBounds))
        {
            minBounds = new Vector2(mapBounds.min.x, mapBounds.min.y);
            maxBounds = new Vector2(mapBounds.max.x, mapBounds.max.y);
            if (!boundsReady)
            {
                boundsReady = true;
                Vector3 center = new Vector3(mapBounds.center.x, mapBounds.center.y, transform.position.z);
                if (centerOnMapAtStart)
                {
                    transform.position = center;
                    FocusOn(center);
                }
                Debug.Log($"[CameraController] 地图边界 = x[{minBounds.x:F2}, {maxBounds.x:F2}] " +
                          $"y[{minBounds.y:F2}, {maxBounds.y:F2}]，相机对到中心 ({center.x:F2}, {center.y:F2})");
            }
            return;
        }

        // 格子还没生成：先按网格参数估一个，等真实格子出现后会覆盖
        float hexSize = HexGrid.Instance.hexSize;
        int radius = HexGrid.Instance.gridRadius;

        // 估算地图尺寸（Flat-top 六边形）
        float mapWidth = radius * hexSize * 2.5f;
        float mapHeight = radius * hexSize * Mathf.Sqrt(3) * 1.5f;

        minBounds = new Vector2(-mapWidth, -mapHeight);
        maxBounds = new Vector2(mapWidth, mapHeight);
    }

    /// <summary>
    /// 聚焦到某个坐标
    /// </summary>
    public void FocusOn(HexCoord coord)
    {
        if (HexGrid.Instance == null) return;

        Vector3 worldPos = HexGrid.Instance.HexToWorld(coord);
        Vector3 target = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        transform.position = target;
    }

    /// <summary>
    /// 平滑移动到某个坐标（协程版本可以自己扩展）
    /// </summary>
    public void FocusOn(Vector3 worldPos)
    {
        Vector3 target = new Vector3(worldPos.x, worldPos.y, transform.position.z);
        transform.position = target;
    }
}
