using UnityEngine;
using System.Collections.Generic;

// ============================================================
//  六边形寻路（A*）
// ============================================================

/// <summary>
/// 六边形寻路算法。静态类，直接 HexPathfinding.FindPath(start, end)。
/// </summary>
public static class HexPathfinding
{
    /// <summary>
    /// 寻路参数。
    /// 注意：所有字段的**默认值就是"按规则阻挡"**（障碍物挡、其他单位挡、不限代价），
    /// 所以 default(PathOptions) / 不传参 得到的就是正确行为，不需要额外初始化。
    /// </summary>
    public struct PathOptions
    {
        [Tooltip("true = 允许穿过障碍物（无视碰撞的突进类技能用；默认 false = 障碍物阻挡）")]
        public bool ignoreObstacle;

        [Tooltip("true = 忽略其他单位（穿过身体；默认 false = 单位阻挡）")]
        public bool ignoreUnits;

        [Tooltip("总代价上限，>0 时超过就放弃（0 = 不限）。玩家按耐力限制移动距离时可用")]
        public int maxCost;

        [Tooltip("移动者自己。设了之后，它脚下那一格不会因为\"有单位\"而被判成阻挡")]
        public Unit mover;
    }

    /// <summary>寻路节点（只在内部用）</summary>
    private class PathNode
    {
        public HexCoord coord;
        public PathNode parent;
        public int gCost;            // 从起点走到这里的实际代价
        public int hCost;            // 到终点的估算代价
        public int fCost => gCost + hCost;
        public bool inOpen;          // 是否还在开放列表里
        public bool closed;          // 是否已确定最优（出过开放列表）

        public PathNode(HexCoord coord)
        {
            this.coord = coord;
        }
    }

    // ============================================================
    //                        对外接口
    // ============================================================

    /// <summary>
    /// 寻找从 start 到 end 的最短路径。返回含起点和终点的坐标列表，找不到返回 null。
    /// </summary>
    public static List<HexCoord> FindPath(HexCoord start, HexCoord end)
    {
        return FindPath(start, end, default);
    }

    /// <summary>
    /// 寻找从 start 到 end 的最短路径（可指定通行参数）。
    /// </summary>
    public static List<HexCoord> FindPath(HexCoord start, HexCoord end, PathOptions options)
    {
        List<HexCoord> path;
        int cost;
        return TryFindPath(start, end, out path, out cost, options) ? path : null;
    }

    /// <summary>
    /// 寻路并返回总代价（按 cell.moveCost 累加）。
    /// 返回 false 时 path 为 null、totalCost 为 0。
    /// </summary>
    public static bool TryFindPath(HexCoord start, HexCoord end, out List<HexCoord> path, out int totalCost)
    {
        return TryFindPath(start, end, out path, out totalCost, default);
    }

    /// <summary>
    /// 寻路并返回总代价（带通行参数）。
    /// </summary>
    public static bool TryFindPath(HexCoord start, HexCoord end,
                                   out List<HexCoord> path, out int totalCost, PathOptions options)
    {
        path = null;
        totalCost = 0;

        if (HexGrid.Instance == null)
        {
            Debug.LogWarning("HexGrid.Instance 不存在，无法寻路");
            return false;
        }

        HexCell startCell = HexGrid.Instance.GetCell(start);
        HexCell endCell = HexGrid.Instance.GetCell(end);
        if (startCell == null || endCell == null) return false;

        // 起点和终点相同
        if (start == end)
        {
            path = new List<HexCoord> { start };
            return true;
        }

        // 终点必须能落脚（阻挡障碍物 / 别的活单位 都算不行）
        if (!CanStepInto(endCell, options)) return false;

        List<PathNode> nodes = Search(
            start,
            coord => coord == end,
            coord => coord.Distance(end),
            options,
            out totalCost);

        if (nodes == null) return false;

        path = ToCoords(nodes);
        return true;
    }

    /// <summary>
    /// 只找下一步要走的格子（适合敌人 AI，不用关心完整路径）。
    /// 终点被别人占着时，自动改成"走到它旁边"——旧版这种情况下会直接返回起点，
    /// 表现就是敌人明明能靠近却站着不动。
    /// </summary>
    public static HexCoord FindNextStep(HexCoord start, HexCoord end)
    {
        return FindNextStep(start, end, default);
    }

    /// <summary>
    /// 只找下一步要走的格子（带通行参数）。
    /// </summary>
    public static HexCoord FindNextStep(HexCoord start, HexCoord end, PathOptions options)
    {
        List<HexCoord> path = FindPath(start, end, options);

        // 终点被占 / 不可达 → 退化到"走到它相邻的一格"
        if (path == null || path.Count < 2)
            path = FindPathToRange(start, end, 1, 1, options);

        if (path == null || path.Count < 2) return start;
        return path[1];   // path[0] 是起点
    }

    /// <summary>
    /// 判断两点之间是否可达
    /// </summary>
    public static bool HasPath(HexCoord start, HexCoord end)
    {
        return HasPath(start, end, default);
    }

    /// <summary>
    /// 判断两点之间是否可达（带通行参数）
    /// </summary>
    public static bool HasPath(HexCoord start, HexCoord end, PathOptions options)
    {
        return FindPath(start, end, options) != null;
    }

    /// <summary>
    /// 获取 start 在 moveRange 步（按 moveCost 算）内可以"停留"的所有格子。
    /// 用于移动范围高亮；不含起点。
    /// 注意：返回的是空列表而不是 null，调用方可以放心 foreach。
    /// </summary>
    public static List<HexCoord> GetReachableCells(HexCoord start, int moveRange)
    {
        return GetReachableCells(start, moveRange, default);
    }

    /// <summary>
    /// 获取可达格子（带通行参数）。
    /// </summary>
    public static List<HexCoord> GetReachableCells(HexCoord start, int moveRange, PathOptions options)
    {
        List<HexCoord> result = new List<HexCoord>();
        if (HexGrid.Instance == null || moveRange <= 0) return result;
        if (HexGrid.Instance.GetCell(start) == null) return result;

        Dictionary<HexCoord, int> cost = new Dictionary<HexCoord, int>();
        HashSet<HexCoord> seen = new HashSet<HexCoord>();
        List<HexCoord> frontier = new List<HexCoord>();

        cost[start] = 0;
        frontier.Add(start);

        while (frontier.Count > 0)
        {
            // 取当前代价最小的（地图就几十格，线性扫描比维护堆更快也更省事）
            int bestIdx = 0;
            for (int i = 1; i < frontier.Count; i++)
            {
                if (cost[frontier[i]] < cost[frontier[bestIdx]])
                    bestIdx = i;
            }

            HexCoord current = frontier[bestIdx];
            frontier.RemoveAt(bestIdx);

            int currentCost = cost[current];
            if (currentCost >= moveRange) continue;

            for (int dir = 0; dir < 6; dir++)
            {
                HexCoord next = current.GetNeighbor(dir);
                HexCell cell = HexGrid.Instance.GetCell(next);
                if (!CanStepInto(cell, options)) continue;

                int newCost = currentCost + GetStepCost(cell);
                if (newCost > moveRange) continue;

                // 已经有更优解就不用再更新
                if (cost.TryGetValue(next, out int oldCost) && oldCost <= newCost) continue;

                cost[next] = newCost;
                frontier.Add(next);

                if (next != start && seen.Add(next))
                    result.Add(next);
            }
        }

        return result;
    }

    /// <summary>
    /// 从 start 出发，走到"离 target 距离在 minRange~maxRange 之间"的位置。
    /// 敌人 AI 用：射程够不着时先靠近，返回路径的最后一格就是能出手的位置。
    ///
    /// 返回的路径含起点：如果起点就已经在射程内，返回 [start]（长度 1，调用方不用移动）。
    /// 找不到返回 null。
    /// </summary>
    public static List<HexCoord> FindPathToRange(HexCoord start, HexCoord target,
                                                 int minRange, int maxRange)
    {
        return FindPathToRange(start, target, minRange, maxRange, default);
    }

    /// <summary>
    /// 走到"能打到 target 的位置"（带通行参数）。
    /// </summary>
    public static List<HexCoord> FindPathToRange(HexCoord start, HexCoord target,
                                                 int minRange, int maxRange, PathOptions options)
    {
        if (HexGrid.Instance == null) return null;
        if (HexGrid.Instance.GetCell(start) == null) return null;

        if (minRange < 0) minRange = 0;
        if (maxRange < minRange) maxRange = minRange;

        int totalCost;
        List<PathNode> nodes = Search(
            start,
            // 目标条件：距离落在 [minRange, maxRange] 内，且这一格能落脚
            coord =>
            {
                int d = coord.Distance(target);
                if (d < minRange || d > maxRange) return false;
                HexCell cell = HexGrid.Instance.GetCell(coord);
                return coord == start || CanStepInto(cell, options);
            },
            // 启发：离"射程边界"还差几格（可采纳，不会高估）
            coord => Mathf.Max(0, coord.Distance(target) - maxRange),
            options,
            out totalCost);

        return nodes == null ? null : ToCoords(nodes);
    }

    /// <summary>
    /// 一条路径的总代价（按每格 moveCost 累加，不含起点）。
    /// PlayerUnit.TryMoveTo 里那句 `path.Count - 1` 应该换成它，
    /// 否则沼泽/难走地形依旧只扣 1 点耐力。
    /// </summary>
    public static int GetPathCost(List<HexCoord> path)
    {
        if (path == null || path.Count <= 1) return 0;

        int total = 0;
        for (int i = 1; i < path.Count; i++)
        {
            HexCell cell = HexGrid.Instance != null ? HexGrid.Instance.GetCell(path[i]) : null;
            total += cell != null ? GetStepCost(cell) : 1;
        }
        return total;
    }

    // ============================================================
    //                        A* 核心
    // ============================================================

    /// <summary>
    /// A* 搜索本体。isGoal 判定到达、heuristic 给估算值。
    /// 返回从起点到目标经过的节点链（含首尾），找不到返回 null。
    /// </summary>
    private static List<PathNode> Search(HexCoord start,
                                         System.Func<HexCoord, bool> isGoal,
                                         System.Func<HexCoord, int> heuristic,
                                         PathOptions options,
                                         out int totalCost)
    {
        totalCost = 0;

        PathNode startNode = new PathNode(start);
        startNode.gCost = 0;
        startNode.hCost = heuristic(start);
        startNode.inOpen = true;

        List<PathNode> openList = new List<PathNode> { startNode };
        Dictionary<HexCoord, PathNode> nodeDict = new Dictionary<HexCoord, PathNode> { [start] = startNode };

        while (openList.Count > 0)
        {
            // 1. 取 fCost 最小的节点（f 相同取 h 小的，走得更"直"）
            int bestIdx = 0;
            for (int i = 1; i < openList.Count; i++)
            {
                PathNode a = openList[i];
                PathNode b = openList[bestIdx];
                if (a.fCost < b.fCost || (a.fCost == b.fCost && a.hCost < b.hCost))
                    bestIdx = i;
            }

            PathNode current = openList[bestIdx];
            openList.RemoveAt(bestIdx);
            current.inOpen = false;
            current.closed = true;

            // 2. 到达目标
            if (isGoal(current.coord))
            {
                totalCost = current.gCost;
                return Retrace(current);
            }

            // 3. 展开 6 个邻居
            for (int dir = 0; dir < 6; dir++)
            {
                HexCoord nextCoord = current.coord.GetNeighbor(dir);
                HexCell nextCell = HexGrid.Instance.GetCell(nextCoord);

                if (!CanStepInto(nextCell, options)) continue;

                int newG = current.gCost + GetStepCost(nextCell);

                // 代价上限（玩家按耐力限制移动距离时用）
                if (options.maxCost > 0 && newG > options.maxCost) continue;

                PathNode nextNode;
                if (!nodeDict.TryGetValue(nextCoord, out nextNode))
                {
                    nextNode = new PathNode(nextCoord);
                    nodeDict[nextCoord] = nextNode;
                }
                else if (nextNode.closed)
                {
                    // 代价非负，已确定的节点不会再有更优路径
                    continue;
                }

                // 首次发现，或找到了更便宜的走法 → 更新并（必要时）重新入队
                if (!nextNode.inOpen || newG < nextNode.gCost)
                {
                    nextNode.gCost = newG;
                    nextNode.hCost = heuristic(nextCoord);
                    nextNode.parent = current;

                    if (!nextNode.inOpen)
                    {
                        nextNode.inOpen = true;
                        openList.Add(nextNode);
                    }
                }
            }
        }

        return null;   // 开放列表空了还没到 = 不可达
    }

    // ============================================================
    //                        辅助方法
    // ============================================================

    /// <summary>
    /// 这一格能不能走进去（地形 + 障碍物 + 单位，三件事只在这里判一次）。
    /// 起点不用判——单位本来就站在上面。
    /// </summary>
    private static bool CanStepInto(HexCell cell, PathOptions options)
    {
        if (cell == null) return false;
        if (!cell.isWalkable) return false;
        if (!options.ignoreObstacle && cell.IsBlockedByObstacle()) return false;

        if (!options.ignoreUnits)
        {
            Unit occupant = cell.occupyUnit;
            if (occupant != null && occupant != options.mover && !occupant.IsDead)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 进入这一格的代价。
    /// 下限 1：asset 里 moveCost 忘了填（=0）时，0 代价会让 A* 在等价格之间疯狂扩散。
    /// </summary>
    private static int GetStepCost(HexCell cell)
    {
        return cell == null ? 1 : Mathf.Max(1, cell.moveCost);
    }

    /// <summary>节点链 → 坐标列表</summary>
    private static List<HexCoord> ToCoords(List<PathNode> nodes)
    {
        List<HexCoord> path = new List<HexCoord>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
            path.Add(nodes[i].coord);
        return path;
    }

    /// <summary>从终点回溯到起点</summary>
    private static List<PathNode> Retrace(PathNode endNode)
    {
        List<PathNode> path = new List<PathNode>();
        PathNode current = endNode;

        while (current != null)
        {
            path.Add(current);
            current = current.parent;
        }

        path.Reverse();
        return path;
    }
}
