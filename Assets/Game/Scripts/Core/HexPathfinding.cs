using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 六边形 AI 寻路算法
/// 静态类，直接调用 HexPathfinding.FindPath()
/// </summary>
public static class HexPathfinding
{
    /// <summary>
    /// 寻路节点
    /// </summary>
    private class PathNode
    {
        public HexCoord coord;
        public PathNode parent;    // 来路节点
        public int gCost;          // 当前行动时已经花费的行动力/代价
        public int hCost;          // 当前行动的所要花费的行动力/代价（估计值）
        public int fCost => gCost + hCost;  // 总代价

        public PathNode(HexCoord coord)
        {
            this.coord = coord;
        }
    }

    /// <summary>
    /// 寻找从 start 到 end 的最短路径
    /// 返回路径列表（包含起点和终点），找不到返回 null
    /// </summary>
    public static List<HexCoord> FindPath(HexCoord start, HexCoord end)
    {
        if (HexGrid.Instance == null)
        {
            Debug.LogWarning("HexGrid.Instance 不存在，无法寻路");
            return null;
        }

        HexCell startCell = HexGrid.Instance.GetCell(start);
        HexCell endCell = HexGrid.Instance.GetCell(end);

        if (startCell == null || endCell == null)
            return null;

        // 起点和终点相同
        if (start == end)
            return new List<HexCoord> { start };

        // 终点不可通行
        if (!endCell.isWalkable || endCell.occupyUnit != null)
            return null;

        // 开放列表（待检查的节点）
        List<PathNode> openList = new List<PathNode>();
        // 关闭列表（已检查过的节点）
        HashSet<HexCoord> closedSet = new HashSet<HexCoord>();
        // 快速查找：坐标 → 节点
        Dictionary<HexCoord, PathNode> nodeDict = new Dictionary<HexCoord, PathNode>();

        // 添加起点
        PathNode startNode = new PathNode(start);
        startNode.gCost = 0;
        startNode.hCost = GetDistance(start, end);
        openList.Add(startNode);
        nodeDict[start] = startNode;

        while (openList.Count > 0)
        {
            // 找 fCost 最小的节点
            PathNode current = GetLowestFCostNode(openList);

            // 到达终点
            if (current.coord == end)
            {
                return RetracePath(current);
            }

            // 移到关闭列表
            openList.Remove(current);
            closedSet.Add(current.coord);

            // 遍历 6 个邻居
            for (int i = 0; i < 6; i++)
            {
                HexCoord neighborCoord = current.coord.GetNeighbor(i);

                // 已检查过
                if (closedSet.Contains(neighborCoord))
                    continue;

                HexCell neighborCell = HexGrid.Instance.GetCell(neighborCoord);

                // 格子不存在或不可通行
                if (neighborCell == null || !neighborCell.isWalkable)
                    continue;

                // 有单位阻挡（终点除外，终点已经检查过了）
                if (neighborCell.occupyUnit != null && neighborCoord != end)
                    continue;

                // 计算新的 gCost
                int moveCost = current.gCost + neighborCell.moveCost;

                PathNode neighborNode;
                if (nodeDict.TryGetValue(neighborCoord, out neighborNode))
                {
                    // 已有节点，检查新路径是否更优
                    if (moveCost < neighborNode.gCost)
                    {
                        neighborNode.gCost = moveCost;
                        neighborNode.parent = current;
                    }
                }
                else
                {
                    // 新节点
                    neighborNode = new PathNode(neighborCoord);
                    neighborNode.gCost = moveCost;
                    neighborNode.hCost = GetDistance(neighborCoord, end);
                    neighborNode.parent = current;
                    openList.Add(neighborNode);
                    nodeDict[neighborCoord] = neighborNode;
                }
            }
        }

        // 没找到路径
        return null;
    }

    /// <summary>
    /// 只找下一步要走的格子（不用算完整路径）
    /// 适合敌人 AI，性能更好
    /// </summary>
    public static HexCoord FindNextStep(HexCoord start, HexCoord end)
    {
        var path = FindPath(start, end);
        if (path == null || path.Count < 2)
            return start;
        return path[1];  // path[0] 是起点，path[1] 是下一步
    }

    /// <summary>
    /// 判断两点之间是否可达
    /// </summary>
    public static bool HasPath(HexCoord start, HexCoord end)
    {
        return FindPath(start, end) != null;
    }

    /// <summary>
    /// 获取某个格子 moveRange 步内可到达的所有格子
    /// 用于显示移动范围
    /// </summary>
    public static List<HexCoord> GetReachableCells(HexCoord start, int moveRange)
    {
        if (HexGrid.Instance == null) return null;

        List<HexCoord> reachable = new List<HexCoord>();
        Dictionary<HexCoord, int> costDict = new Dictionary<HexCoord, int>();
        Queue<HexCoord> queue = new Queue<HexCoord>();

        queue.Enqueue(start);
        costDict[start] = 0;
        reachable.Add(start);

        while (queue.Count > 0)
        {
            HexCoord current = queue.Dequeue();
            int currentCost = costDict[current];

            if (currentCost >= moveRange)
                continue;

            for (int i = 0; i < 6; i++)
            {
                HexCoord neighbor = current.GetNeighbor(i);
                HexCell cell = HexGrid.Instance.GetCell(neighbor);

                if (cell == null || !cell.isWalkable)
                    continue;

                int newCost = currentCost + cell.moveCost;
                if (newCost > moveRange)
                    continue;

                if (costDict.ContainsKey(neighbor))
                {
                    if (newCost < costDict[neighbor])
                    {
                        costDict[neighbor] = newCost;
                        queue.Enqueue(neighbor);
                    }
                }
                else
                {
                    costDict[neighbor] = newCost;
                    reachable.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        // 移除起点
        reachable.Remove(start);
        return reachable;
    }

    // ============================================================
    //                       辅助方法
    // ============================================================

    /// <summary>
    /// 两个六边形坐标的距离（用作估算值 hCost）
    /// </summary>
    private static int GetDistance(HexCoord a, HexCoord b)
    {
        return a.Distance(b);
    }

    /// <summary>
    /// 从终点回溯路径
    /// </summary>
    private static List<HexCoord> RetracePath(PathNode endNode)
    {
        List<HexCoord> path = new List<HexCoord>();
        PathNode current = endNode;

        while (current != null)
        {
            path.Add(current.coord);
            current = current.parent;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// 在开放列表中找 fCost 最小的节点
    /// </summary>
    private static PathNode GetLowestFCostNode(List<PathNode> openList)
    {
        PathNode lowest = openList[0];
        for (int i = 1; i < openList.Count; i++)
        {
            if (openList[i].fCost < lowest.fCost ||
                (openList[i].fCost == lowest.fCost && openList[i].hCost < lowest.hCost))
            {
                lowest = openList[i];
            }
        }
        return lowest;
    }
}
