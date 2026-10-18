using UnityEngine;
using System.Collections.Generic;

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

    [Header("战斗高亮贴图（例图方案：不用颜色，改用选取格图形）")]
    [Tooltip("位移选取格（绿圈）——移动可到达格")]
    public Sprite moveHighlightSprite;
    [Tooltip("攻击选取格（红圈）——攻击范围格")]
    public Sprite attackHighlightSprite;

    // 运行时高亮覆盖层（懒创建的子物体，显示在格子之上、棋子之下）
    private SpriteRenderer highlightOverlay;

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
    /// 设置高亮（通用选中提示，用位移选取格图形；未配置贴图时退回旧染色方案）
    /// </summary>
    public void SetHighlight(bool active)
    {
        if (active && moveHighlightSprite != null)
        {
            ShowOverlay(moveHighlightSprite, Color.white);
            return;
        }

        // 关键：清除时必须同时关掉贴图覆盖层，否则上个技能的圈会留在场景里
        if (!active)
            ShowOverlay(null, Color.white);

        if (spriteRenderer != null)
            spriteRenderer.color = active ? highlightColor : defaultColor;
    }

    /// <summary>直接设置格子颜色（旧方案保留；战斗高亮已改走贴图覆盖层）</summary>
    public void SetColor(Color color)
    {
        if (spriteRenderer != null)
            spriteRenderer.color = color;
    }

    // ============================================================
    //          战斗高亮（例图方案：选取格贴图，不再用染色）
    // ============================================================

    /// <summary>可移动范围高亮（位移选取格·绿圈）</summary>
    public void SetMoveRangeHighlight(bool active) => ShowOverlay(active ? moveHighlightSprite : null, Color.white);

    /// <summary>攻击范围高亮（攻击选取格·红圈）</summary>
    public void SetAttackRangeHighlight(bool active) => ShowOverlay(active ? attackHighlightSprite : null, Color.white);

    /// <summary>攻击范围内有目标的高亮（红圈加深，和普通攻击格区分）</summary>
    public void SetTargetHighlight(bool active) => ShowOverlay(active ? attackHighlightSprite : null, new Color(0.75f, 0.2f, 0.2f, 1f));

    /// <summary>瞄准高亮（敌方预备技能指向的格子，红圈染黄）</summary>
    public void SetAimHighlight(bool active) => ShowOverlay(active ? attackHighlightSprite : null, new Color(1f, 0.85f, 0.3f, 1f));

    /// <summary>
    /// 显示/隐藏高亮覆盖层。sprite 传 null 表示隐藏。
    /// 覆盖层是懒创建的子物体，自动缩放到和格子贴图一样大。
    /// </summary>
    private void ShowOverlay(Sprite sprite, Color tint)
    {
        if (sprite == null)
        {
            if (highlightOverlay != null)
                highlightOverlay.enabled = false;
            return;
        }

        if (highlightOverlay == null)
        {
            Transform child = transform.Find("HighlightOverlay");
            if (child == null)
            {
                GameObject go = new GameObject("HighlightOverlay");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                highlightOverlay = go.AddComponent<SpriteRenderer>();
                highlightOverlay.sortingOrder = 5;   // 格子(0)之上、棋子(10)之下
            }
            else
            {
                highlightOverlay = child.GetComponent<SpriteRenderer>();
            }
        }

        highlightOverlay.sprite = sprite;
        highlightOverlay.color = tint;
        highlightOverlay.enabled = true;

        // 把覆盖层缩放到和格子贴图一样大（局部空间对比两张 sprite 的 bounds）
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Vector2 target = spriteRenderer.sprite.bounds.size;
            Vector2 source = sprite.bounds.size;
            if (source.x > 0.0001f && source.y > 0.0001f)
                highlightOverlay.transform.localScale = new Vector3(
                    target.x / source.x, target.y / source.y, 1f);
        }
    }

    /// <summary>
    /// Unity 碰撞体点击事件（PolygonCollider2D + Camera 射线）。
    /// 和 BattleHUD 的坐标拾取互为兜底：任一条通道可用，点格子就能生效。
    /// </summary>
    private void OnMouseDown()
    {
        EventManager.Trigger("CellClicked", this);
    }

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

    // ============================================================
    //            通行判定（寻路 / 移动 / 位移 / 高亮 全走这里）
    // ============================================================

    /// <summary>
    /// 格子上的障碍物是否阻挡移动。
    /// 交给 Obstacle.BlocksMovement() 决定：柱子/石堆挡路，炸药桶这类可以走过去踩爆的不挡。
    /// 没有挂 Obstacle 脚本的装饰性物体不算阻挡（obstacle 字段为 null）。
    /// </summary>
    public bool IsBlockedByObstacle()
    {
        return obstacle != null && obstacle.BlocksMovement();
    }

    /// <summary>
    /// 指定单位能否"进入"这个格子。
    /// 三个条件：① 可行走 ② 没有阻挡移动的障碍物 ③ 没有别的活着的单位。
    ///
    /// mover 传 null 表示"不关心是谁"（纯地形判定）。
    /// 已经 Die() 但还没被 Destroy 的单位不占位——Unity 的 Destroy 延迟到帧末，
    /// 不判 IsDead 的话会出现"打死了但格子还是占着，谁都走不进去"。
    /// </summary>
    public bool CanEnter(Unit mover = null)
    {
        if (!isWalkable) return false;
        if (IsBlockedByObstacle()) return false;

        if (occupyUnit != null && occupyUnit != mover && !occupyUnit.IsDead)
            return false;

        return true;
    }

    /// <summary>
    /// 能否"停留"在这里。
    /// 现在和 CanEnter 等价，单独留一个方法是为了以后加"可以穿过但不能停"的地形
    /// （比如尖刺/传送带）时只改这一处。
    /// </summary>
    public bool CanStopOn(Unit mover = null)
    {
        return CanEnter(mover);
    }

    /// <summary>
    /// 格子是否"空"（可通行 + 没活单位 + 没阻挡障碍物）
    /// </summary>
    public bool IsEmpty()
    {
        return CanEnter(null);
    }

    /// <summary>
    /// 格子是否被占用（有活单位，或有阻挡移动的障碍物）
    /// </summary>
    public bool IsOccupied()
    {
        return (occupyUnit != null && !occupyUnit.IsDead) || IsBlockedByObstacle();
    }

    /// <summary>
    /// 获取 6 个相邻格子（越界的自动跳过）。
    /// 返回的是 HexCell 而不是 HexCoord，省掉调用方每层都 GetCell 一次。
    /// </summary>
    public List<HexCell> GetNeighborCells()
    {
        List<HexCell> result = new List<HexCell>(6);
        if (HexGrid.Instance == null) return result;

        for (int i = 0; i < 6; i++)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord.GetNeighbor(i));
            if (cell != null) result.Add(cell);
        }
        return result;
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
