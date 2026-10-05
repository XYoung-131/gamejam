using UnityEngine;

/// <summary>
/// 相机控制器——正交相机，支持拖拽平移、滚轮缩放、边界限制
/// 挂在 Main Camera 上
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("移动")]
    [Tooltip("拖拽移动速度")]
    public float panSpeed = 1f;
    [Tooltip("键盘移动速度")]
    public float keyPanSpeed = 10f;

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

    // 拖拽状态
    private bool isDragging = false;
    private Vector3 lastMousePosition;

    // ============================================================
    //                     初始化
    // ============================================================

    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void Start()
    {
        // 如果 HexGrid 存在，自动设置边界
        if (HexGrid.Instance != null)
        {
            AutoSetBounds();
        }
    }

    // ============================================================
    //                     每帧更新
    // ============================================================

    private void Update()
    {
        // 暂停时不响应输入
        if (GameManager.Instance != null && GameManager.Instance.IsPaused)
            return;

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

        pos.x = Mathf.Clamp(pos.x, minBounds.x + viewHalfWidth, maxBounds.x - viewHalfWidth);
        pos.y = Mathf.Clamp(pos.y, minBounds.y + viewHalfHeight, maxBounds.y - viewHalfHeight);

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
