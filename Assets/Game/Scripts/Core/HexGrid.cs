using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 六边形网格管理器——单例模式
/// 负责生成网格、坐标转换、格子查询
/// </summary>
public class HexGrid : MonoBehaviour
{
    // ========== 单例 ==========
    public static HexGrid Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ========== 配置 ==========

    [Header("网格配置")]
    [Tooltip("六边形的大小（外接圆半径，单位：Unity 单位）")]
    public float hexSize = 1f;

    [Tooltip("网格半径（六边形层数，0=只有中心格，2=中心+2圈）")]
    public int gridRadius = 4;

    [Tooltip("格子预制体（需要挂 HexCell 脚本）")]
    public HexCell cellPrefab;

    [Header("地图形状")]
    [Tooltip("进入游戏时生成格子")]
    public bool generateOnStart = false;

    [Tooltip("使用下方坐标列表代替规则半径地图")]
    public bool useCustomShape = false;

    public List<HexCoord> customCells = new List<HexCoord>();

    [Header("绘制对齐")]
    [Tooltip("整张网格在世界坐标中的偏移")]
    public Vector2 originOffset = Vector2.zero;

    [Tooltip("纵向间距倍率，用于匹配有透视感的地图画稿")]
    public float verticalScale = 1f;

    [Header("坐标方向")]
    [Tooltip("六边形朝向：Flat-top（平顶）或 Pointy-top（尖顶）")]
    public HexOrientation orientation = HexOrientation.FlatTop;

    /// <summary>
    /// 地图根物体在世界里的位置。画稿（SpriteRenderer）挂在这个根物体上，
    /// 所以格子和单位都要以它为基准，这样挪动地图时画稿和格子不会错开。
    /// </summary>
    public Vector2 WorldOrigin => new Vector2(transform.position.x, transform.position.y);

    // ========== 运行时数据 ==========

    private Dictionary<HexCoord, HexCell> cells = new Dictionary<HexCoord, HexCell>();
    private Transform cellsParent;

    private void Start()
    {
        if (generateOnStart)
            GenerateGrid();
    }

    // ========== 网格生成 ==========

    /// <summary>
    /// 生成六边形网格
    /// </summary>
    public void GenerateGrid()
    {
        // 缩放/旋转会让格子和画稿对不上，给出提示
        Vector3 scale = transform.lossyScale;
        if (Mathf.Abs(scale.x - 1f) > 0.001f || Mathf.Abs(scale.y - 1f) > 0.001f)
            Debug.LogWarning($"[HexGrid] 地图根物体的缩放是 {scale.x}×{scale.y}，格子尺寸会和画稿不一致");
        if (Mathf.Abs(transform.eulerAngles.z) > 0.001f)
            Debug.LogWarning($"[HexGrid] 地图根物体旋转了 {transform.eulerAngles.z} 度，格子会和画稿错开");

        // 创建格子父物体
        if (cellsParent == null)
        {
            GameObject parentObj = new GameObject("Cells");
            parentObj.transform.SetParent(transform);
            cellsParent = parentObj.transform;
        }

        // 清空旧格子
        foreach (var cell in cells.Values)
            Destroy(cell.gameObject);
        cells.Clear();

        if (useCustomShape)
        {
            foreach (HexCoord coord in customCells)
            {
                if (!cells.ContainsKey(coord))
                    CreateCell(coord);
            }
        }
        else
        {
            // 按半径生成（轴向坐标）
            for (int q = -gridRadius; q <= gridRadius; q++)
            {
                int r1 = Mathf.Max(-gridRadius, -q - gridRadius);
                int r2 = Mathf.Min(gridRadius, -q + gridRadius);
                for (int r = r1; r <= r2; r++)
                    CreateCell(new HexCoord(q, r));
            }
        }

        Debug.Log($"六边形网格生成完成，共 {cells.Count} 个格子，半径={gridRadius}");
    }

    /// <summary>
    /// 创建单个格子
    /// </summary>
    HexCell CreateCell(HexCoord coord)
    {
        if (cellPrefab == null)
        {
            Debug.LogError("HexGrid: cellPrefab 未设置！");
            return null;
        }

        // 实例化
        HexCell cell = Instantiate(cellPrefab, cellsParent);
        cell.name = $"Cell_{coord.q}_{coord.r}";

        // 初始化
        cell.Initialize(coord);

        // 设置世界坐标
        cell.transform.position = HexToWorld(coord);

        // 注册到字典
        cells[coord] = cell;

        return cell;
    }

    // ========== 坐标转换 ==========

    /// <summary>
    /// 六边形坐标 → 世界坐标
    /// Flat-top（平顶）公式：
    ///   x = size * (3/2 * q)
    ///   z = size * (sqrt(3) * r + sqrt(3)/2 * q)
    /// （2D 游戏中 z 对应 y 轴）
    /// </summary>
    public Vector3 HexToWorld(HexCoord hex)
    {
        Vector2 p = HexToWorld2D(hex);
        return new Vector3(p.x, p.y, 0f);
    }

    /// <summary>
    /// 六边形坐标 → 世界坐标（二维版）。已经算上 originOffset 和地图根物体的位置。
    /// Flat-top（平顶）：x = size * 3/2 * q，y = size * (√3 r + √3/2 q) * verticalScale
    /// </summary>
    public Vector2 HexToWorld2D(HexCoord hex)
    {
        float x, y;
        if (orientation == HexOrientation.FlatTop)
        {
            x = hexSize * (3f / 2f * hex.q);
            y = hexSize * (Mathf.Sqrt(3) * hex.r + Mathf.Sqrt(3) / 2f * hex.q) * verticalScale;
        }
        else // Pointy-top（尖顶）
        {
            x = hexSize * (Mathf.Sqrt(3) * hex.q + Mathf.Sqrt(3) / 2f * hex.r);
            y = hexSize * (3f / 2f * hex.r) * verticalScale;
        }

        return new Vector2(x + originOffset.x + WorldOrigin.x,
                           y + originOffset.y + WorldOrigin.y);
    }

    /// <summary>
    /// 世界坐标 → 六边形坐标
    /// 把像素坐标转换为最近的六边形格子坐标
    /// </summary>
    public HexCoord WorldToHex(Vector3 worldPos)
    {
        float x = worldPos.x - originOffset.x - WorldOrigin.x;
        float y = (worldPos.y - originOffset.y - WorldOrigin.y) / Mathf.Max(verticalScale, 0.0001f);

        float q, r;

        if (orientation == HexOrientation.FlatTop)
        {
            q = (2f / 3f * x) / hexSize;
            r = (-1f / 3f * x + Mathf.Sqrt(3) / 3f * y) / hexSize;
        }
        else // Pointy-top
        {
            q = (Mathf.Sqrt(3) / 3f * x - 1f / 3f * y) / hexSize;
            r = (2f / 3f * y) / hexSize;
        }

        // 轴向坐标取整（hex round）
        return HexRound(q, r);
    }

    /// <summary>
    /// 六边形坐标取整（找到最近的整数坐标）
    /// </summary>
    HexCoord HexRound(float q, float r)
    {
        float s = -q - r;

        int rq = Mathf.RoundToInt(q);
        int rr = Mathf.RoundToInt(r);
        int rs = Mathf.RoundToInt(s);

        // 修正：确保 q + r + s = 0
        float q_diff = Mathf.Abs(rq - q);
        float r_diff = Mathf.Abs(rr - r);
        float s_diff = Mathf.Abs(rs - s);

        if (q_diff > r_diff && q_diff > s_diff)
            rq = -rr - rs;
        else if (r_diff > s_diff)
            rr = -rq - rs;

        return new HexCoord(rq, rr);
    }

    // ========== 格子查询 ==========

    /// <summary>
    /// 根据坐标获取格子
    /// </summary>
    public HexCell GetCell(HexCoord coord)
    {
        if (cells.TryGetValue(coord, out HexCell cell))
            return cell;
        return null;
    }

    /// <summary>
    /// 检查坐标是否在网格内
    /// </summary>
    public bool IsInGrid(HexCoord coord)
    {
        return cells.ContainsKey(coord);
    }

    /// <summary>
    /// 获取所有格子
    /// </summary>
    public Dictionary<HexCoord, HexCell>.ValueCollection GetAllCells()
    {
        return cells.Values;
    }

    /// <summary>
    /// 整张地图的世界包围盒（用真实格子位置算，因此包含了 originOffset、纵向压缩和自定义形状），
    /// 格子还没生成时返回 false。
    /// </summary>
    public bool TryGetWorldBounds(out Bounds mapBounds, float margin = 0f)
    {
        mapBounds = default;
        if (cells.Count == 0)
            return false;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (HexCell cell in cells.Values)
        {
            Vector3 p = cell.transform.position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        float pad = margin > 0f ? margin : hexSize;
        mapBounds = new Bounds(
            new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f),
            new Vector3(maxX - minX + pad * 2f, maxY - minY + pad * 2f, 0f));
        return true;
    }

    /// <summary>
    /// 获取所有空格子
    /// </summary>
    public List<HexCell> GetEmptyCells()
    {
        List<HexCell> result = new List<HexCell>();
        foreach (var cell in cells.Values)
        {
            if (cell.IsEmpty())
                result.Add(cell);
        }
        return result;
    }

    /// <summary>
    /// 获取一个随机空格子
    /// </summary>
    public HexCell GetRandomEmptyCell()
    {
        var emptyCells = GetEmptyCells();
        if (emptyCells.Count == 0) return null;
        return emptyCells[Random.Range(0, emptyCells.Count)];
    }

    /// <summary>
    /// 获取指定范围内的所有格子
    /// </summary>
    public List<HexCell> GetCellsInRange(HexCoord center, int range)
    {
        List<HexCell> result = new List<HexCell>();

        for (int dq = -range; dq <= range; dq++)
        {
            int r1 = Mathf.Max(-range, -dq - range);
            int r2 = Mathf.Min(range, -dq + range);
            for (int dr = r1; dr <= r2; dr++)
            {
                HexCoord coord = new HexCoord(center.q + dq, center.r + dr);
                HexCell cell = GetCell(coord);
                if (cell != null)
                    result.Add(cell);
            }
        }

        return result;
    }

    // ========== 视线 / 阻挡 ==========

    /// <summary>
    /// 判断 from 到 to 之间是否被阻挡。
    /// 文档："当目标与自身之间有其他敌人或者障碍物，那么目标相对于自身就是被阻挡的。"
    /// 起点和终点自身不计入阻挡，只检查中间经过的格子。
    /// </summary>
    public bool IsLineBlocked(HexCoord from, HexCoord to)
    {
        if (from == to) return false;

        List<HexCoord> line = GetHexLine(from, to);
        for (int i = 1; i < line.Count - 1; i++)   // 跳过起点和终点
        {
            HexCell cell = GetCell(line[i]);
            if (cell != null && (cell.obstacle != null || cell.occupyUnit != null))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 取两个格子连线经过的所有格子坐标（含起点、终点）。
    /// 用标准六边形连线算法：在两端点间做线性插值，再取整到最近格子。
    /// </summary>
    public List<HexCoord> GetHexLine(HexCoord a, HexCoord b)
    {
        int dist = a.Distance(b);
        List<HexCoord> result = new List<HexCoord>();
        if (dist == 0)
        {
            result.Add(a);
            return result;
        }

        for (int i = 0; i <= dist; i++)
        {
            float t = (float)i / dist;
            // 加一点点扰动，避免正好落在两格中间时取整抖动
            float q = Mathf.Lerp(a.q, b.q, t) + 1e-6f * (b.q - a.q);
            float r = Mathf.Lerp(a.r, b.r, t) + 1e-6f * (b.r - a.r);
            result.Add(HexRound(q, r));
        }
        return result;
    }

    /// <summary>该格子是否处于烟雾中（烟雾中的单位不能被远程攻击选中）</summary>
    public bool IsCellSmoky(HexCoord coord)
    {
        HexCell cell = GetCell(coord);
        return cell != null && cell.isSmoky;
    }

    // ========== 高亮 ==========

    /// <summary>
    /// 清除所有高亮
    /// </summary>
    public void ClearAllHighlights()
    {
        foreach (var cell in cells.Values)
            cell.SetHighlight(false);
    }

    /// <summary>
    /// 高亮指定范围内的格子
    /// </summary>
    public void HighlightRange(HexCoord center, int range)
    {
        ClearAllHighlights();
        var cellsInRange = GetCellsInRange(center, range);
        foreach (var cell in cellsInRange)
            cell.SetHighlight(true);
    }

    // ========== 清空网格 ==========

    /// <summary>
    /// 清空所有格子上的单位和障碍物（不销毁格子）
    /// </summary>
    public void ClearAllUnits()
    {
        foreach (var cell in cells.Values)
            cell.Clear();
    }

    /// <summary>
    /// 销毁整个网格
    /// </summary>
    public void DestroyGrid()
    {
        foreach (var cell in cells.Values)
            Destroy(cell.gameObject);
        cells.Clear();
    }
}

/// <summary>
/// 六边形朝向
/// </summary>
public enum HexOrientation
{
    FlatTop,    // 平顶六边形（上下平，左右尖）
    PointyTop   // 尖顶六边形（上下尖，左右平）
}
