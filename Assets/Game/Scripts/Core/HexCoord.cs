using UnityEngine;
using System;

//六边形轴向坐标（Axial Coordinates），这是我们这次地图用的坐标系
//图片我发在程序的交流群了，后面我会补充在github里的README文件里
[Serializable]
public struct HexCoord : IEquatable<HexCoord>
{
    // 两个坐标轴
    public int q;
    public int r;

    // 构造函数
    public HexCoord(int q, int r)
    {
        this.q = q;
        this.r = r;
    }

    // 6个方向的偏移量（静态数组，共享一份）
    // 这是给六个方向设置一个数组方便调用，是配合下面的GetNeighbor()和GetAllNeighbors()两个方法用的
    // 顺序：右 → 右上 → 左上 → 左 → 左下 → 右下
    private static readonly HexCoord[] _directions = new HexCoord[]
    {
        new HexCoord(1, 0),    // 方向0：右
        new HexCoord(1, -1),   // 方向1：右上
        new HexCoord(0, -1),   // 方向2：左上
        new HexCoord(-1, 0),   // 方向3：左
        new HexCoord(-1, 1),   // 方向4：左下
        new HexCoord(0, 1),    // 方向5：右下
    };

    // 获取指定方向的邻居坐标
    // direction: 0~5
    public HexCoord GetNeighbor(int direction)
    {
        if (direction < 0 || direction > 5)
        {
            Debug.LogError($"方向值 {direction} 无效，应该是 0-5");
            return this;
        }

        HexCoord dir = _directions[direction];
        return new HexCoord(q + dir.q, r + dir.r);
    }

    // 获取所有6个邻居坐标
    public HexCoord[] GetAllNeighbors()
    {
        HexCoord[] neighbors = new HexCoord[6];
        for (int i = 0; i < 6; i++)
        {
            neighbors[i] = GetNeighbor(i);
        }
        return neighbors;
    }

    // 计算到另一个格子的距离（六边形距离）
    // 公式：(|dq| + |dr| + |dq+dr|) / 2，这里不解释了，有点麻烦，可以在网上搜一下进行了解
    public int Distance(HexCoord other)
    {
        int dq = q - other.q;
        int dr = r - other.r;
        int ds = dq + dr;

        return (Mathf.Abs(dq) + Mathf.Abs(dr) + Mathf.Abs(ds)) / 2;
    }

    // 字符串表示（方便调试打印）
    public override string ToString()
    {
        return $"({q}, {r})";
    }

    // 相等判断（让 HexCoord 能当 Dictionary 的 key）
    // 做一下解释：
    // 我们这个坐标系和常规坐标系不同，用的变量也是自己声明的结构体，不能用常规的equals()根据q和r的值相等，来直接证明是同一个格子
    // 这里要重新写一下equals()的逻辑来让原来的equals()功能适用于现在的Axial坐标系
    public override bool Equals(object obj)
    {
        return obj is HexCoord coord && Equals(coord);
    }

    public bool Equals(HexCoord other)
    {
        return q == other.q && r == other.r;
    }

    //把q和r的值合并成一个哈希码，写六边形格子的属性的时候，能更规整一点
    public override int GetHashCode()
    {
        return HashCode.Combine(q, r);
    }

    // 运算符重载（== 和 !=），这里重写的目的和上面的equals一模一样，就不多说一遍了
    public static bool operator ==(HexCoord a, HexCoord b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(HexCoord a, HexCoord b)
    {
        return !a.Equals(b);
    }

    // 坐标加减法
    public static HexCoord operator +(HexCoord a, HexCoord b)
    {
        return new HexCoord(a.q + b.q, a.r + b.r);
    }

    public static HexCoord operator -(HexCoord a, HexCoord b)
    {
        return new HexCoord(a.q - b.q, a.r - b.r);
    }
}
