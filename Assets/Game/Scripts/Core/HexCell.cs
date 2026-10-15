using UnityEngine;

/// <summary>
/// 六边形格子——地图的最小单元
/// 挂在每个格子 GameObject 上，记录格子坐标、通行性、上面的单位和障碍物
/// </summary>
public class HexCell : MonoBehaviour
{
    [Header("格子坐标")]
    [Tooltip("六边形轴向坐标（q, r）")]
    public HexCoord coord;

    [Header("通行属性")]
    [Tooltip("是否可以行走")]
    public bool isWalkable = true;

    [Tooltip("行走消耗的移动力（普通地形=1，沼泽=2）")]
    public int moveCost = 1;

    [Header("格子内容")]
    [Tooltip("站在这个格子上的单位（玩家/敌人/召唤物）")]
    public Unit occupyUnit;

    [Tooltip("格子上的障碍物（柱子/炸药桶等）")]
    public Obstacle obstacle;

    [Header("视觉")]
    [Tooltip("格子的 SpriteRenderer，用于变色高亮")]
    public SpriteRenderer spriteRenderer;

    [Tooltip("默认颜色")]
    public Color defaultColor = Color.white;

    [Tooltip("高亮颜色（选中/可移动提示）")]
    public Color highlightColor = new Color(0.5f, 0.8f, 1f, 0.6f);

    [Header("战斗高亮颜色（文档：移动=绿，攻击范围=粉红，有目标=深红，瞄准=变色）")]
    [Tooltip("玩家使用移动技能时，可到达格子变成绿色")]
    public Color moveRangeColor = new Color(0.4f, 1f, 0.4f, 0.6f);
    [Tooltip("玩家使用攻击技能时，攻击范围内格子变成粉红色")]
    public Color attackRangeColor = new Color(1f, 0.45f, 0.8f, 0.6f);
    [Tooltip("攻击范围内有目标时，该格子变成深红色")]
    public Color targetColor = new Color(0.8f, 0f, 0f, 0.85f);
    [Tooltip("瞄准类技能被作用的格子（敌方预备技能时变色）")]
    public Color aimColor = new Color(1f, 0.8f, 0f, 0.6f);

    [Header("地形 / 状态")]
    [Tooltip("格子上的特殊地形数据（克勒妮绒毯/灼热/剧毒/烟雾/吸血地板）")]
    public TerrainData terrain;
    [Tooltip("是否处于烟雾中（烟雾中的单位不能被远程攻击选中）")]
    public bool isSmoky;

    /// <summary>
    /// 初始化格子
    /// </summary>
    public void Initialize(HexCoord hexCoord, bool walkable = true, int cost = 1)
    {
        coord = hexCoord;
        isWalkable = walkable;
        moveCost = cost;

        // 如果没指定 SpriteRenderer，尝试从子物体获取
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>
    /// 设置高亮
    /// </summary>
    public void SetHighlight(bool active)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = active ? highlightColor : defaultColor;
        }
    }

    /// <summary>直接设置格子颜色（战斗高亮统一入口）</summary>
    public void SetColor(Color color)
    {
        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    /// <summary>可移动范围高亮（绿色）</summary>
    public void SetMoveRangeHighlight(bool active) => SetColor(active ? moveRangeColor : defaultColor);

    /// <summary>攻击范围高亮（粉红色）</summary>
    public void SetAttackRangeHighlight(bool active) => SetColor(active ? attackRangeColor : defaultColor);

    /// <summary>有目标的高亮（深红色）</summary>
    public void SetTargetHighlight(bool active) => SetColor(active ? targetColor : defaultColor);

    /// <summary>瞄准高亮（敌方预备技能时变色）</summary>
    public void SetAimHighlight(bool active) => SetColor(active ? aimColor : defaultColor);

    /// <summary>
    /// 获取所有相邻格子
    /// </summary>
    public HexCoord[] GetAllNeighbors()
    {
        return coord.GetAllNeighbors();
    }

    /// <summary>
    /// 获取某个方向的相邻格子坐标
    /// </summary>
    public HexCoord GetNeighbor(int direction)
    {
        return coord.GetNeighbor(direction);
    }

    /// <summary>
    /// 计算到目标格子的距离
    /// </summary>
    public int DistanceTo(HexCell other)
    {
        return coord.Distance(other.coord);
    }

    /// <summary>
    /// 计算到目标坐标的距离
    /// </summary>
    public int DistanceTo(HexCoord otherCoord)
    {
        return coord.Distance(otherCoord);
    }

    /// <summary>
    /// 格子是否"空"（可通行 + 没单位 + 没障碍物）
    /// </summary>
    public bool IsEmpty()
    {
        return isWalkable && occupyUnit == null && obstacle == null;
    }

    /// <summary>
    /// 格子是否被占用（有单位或有障碍物）
    /// </summary>
    public bool IsOccupied()
    {
        return occupyUnit != null || obstacle != null;
    }

    /// <summary>
    /// 清除格子上的所有内容（重开/重置用）
    /// </summary>
    public void Clear()
    {
        occupyUnit = null;
        obstacle = null;
        SetHighlight(false);
    }
}
