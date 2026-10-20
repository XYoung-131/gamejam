using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 关卡数据——一个关卡包含若干"变种"（同一关的不同布阵）。
/// 每个变种 = 玩家出生格 + 一批敌人/障碍物的摆放。
///
/// 创建方式：Project 窗口右键 → Create → Game → Level Data
/// 关卡文件放在 Assets/Game/ScriptableObjects/关卡/ 下。
/// </summary>
[CreateAssetMenu(fileName = "Level", menuName = "Game/Level Data")]
public class LevelData : ScriptableObject
{
    [Header("基本信息")]
    public string levelName = "第一关";

    [Tooltip("战斗结束界面点\"跳过奖励\"要进入的下一关（不配则重打本关）")]
    public LevelData nextLevel;

    [Header("变种（每个变种是一套完整布阵）")]
    public List<Variant> variants = new List<Variant>();

    /// <summary>一个变种：玩家站哪儿 + 场上摆什么</summary>
    [System.Serializable]
    public class Variant
    {
        public string variantName = "变种1";

        [Tooltip("玩家出生格子")]
        public HexCoord playerStart = new HexCoord(0, 0);

        [Tooltip("敌人与障碍物（unit 和 obstacle 二选一）")]
        public List<Spawn> spawns = new List<Spawn>();

        [Tooltip("特殊地形（把 TerrainData 铺到指定格子上，由 TerrainSystem 结算）")]
        public List<TerrainSpawn> terrains = new List<TerrainSpawn>();
    }

    /// <summary>一块特殊地形的摆放信息</summary>
    [System.Serializable]
    public class TerrainSpawn
    {
        [Tooltip("地形数据（灼热/剧毒/刀刃/烟雾/吸血/克勒妮绒毯）")]
        public TerrainData terrain;

        [Tooltip("格子坐标 (q, r)")]
        public HexCoord coord;
    }

    /// <summary>单个棋子的摆放信息</summary>
    [System.Serializable]
    public class Spawn
    {
        [Tooltip("敌人数据（留空表示这格放障碍物）")]
        public UnitData unit;

        [Tooltip("障碍物数据")]
        public ObstacleData obstacle;

        [Tooltip("格子坐标 (q, r)")]
        public HexCoord coord;
    }
}
