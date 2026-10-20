using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  地形系统——把 HexCell 上配置的 TerrainData 在回合节点上结算
//
//  结算时机（与设计文档"行动顺序：回合开始—…—结算所有 buff—…—回合结束"一致）：
//  - 回合开始：刷新"克勒妮绒毯"的力量加成（站在上面才给，离开就移除）；
//  - 单位移动后：立刻重新判定该单位脚下的绒毯加成；
//  - 所有 buff 结算时（TurnManager 抛 BuffsSettled）：结算灼热/剧毒/刀刃/吸血地板，
//    并推进烟雾地板的喷烟计时。
//
//  挂载位置：场景里任意一个常驻 GameObject（和 TurnManager 类似，全场景唯一）。
// ============================================================
public class TerrainSystem : MonoBehaviour
{
    public static TerrainSystem Instance { get; private set; }

    [Header("调试")]
    [Tooltip("是否在战斗开始时把烟雾地板初始化成随机开/关（文档：初始状态随机）")]
    public bool randomizeSmokeAtStart = true;

    // 已经过了多少个"结算 buff"节点，用来算烟雾的喷发周期
    private int settledRounds = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        EventManager.AddListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.AddListener<Unit>("UnitMoved", HandleUnitMoved);
        EventManager.AddListener("BuffsSettled", HandleBuffsSettled);
    }

    private void OnDisable()
    {
        EventManager.RemoveListener<int>("PlayerTurnStarted", HandlePlayerTurnStarted);
        EventManager.RemoveListener<Unit>("UnitMoved", HandleUnitMoved);
        EventManager.RemoveListener("BuffsSettled", HandleBuffsSettled);
    }

    private void Start()
    {
        if (randomizeSmokeAtStart)
            InitializeSmoke();
    }

    // ============================================================
    //                       烟雾初始化
    // ============================================================

    /// <summary>把所有烟雾地板随机设成开/关（文档：初始状态随机）</summary>
    private void InitializeSmoke()
    {
        foreach (var cell in GetTerrainCells(TerrainType.烟雾地板))
            cell.isSmoky = Random.value < 0.5f;
    }

    // ============================================================
    //              ① 回合开始：刷新克勒妮绒毯的力量加成
    // ============================================================

    private void HandlePlayerTurnStarted(int turnCount)
    {
        foreach (var unit in FindObjectsOfType<Unit>())
            EvaluateBlanket(unit);
    }

    private void HandleUnitMoved(Unit unit)
    {
        if (unit != null)
            EvaluateBlanket(unit);
    }

    /// <summary>
    /// 判定单位脚下的绒毯加成：
    /// 站在克勒妮绒毯上且名字符合 → 挂上力量 buff；否则移除。
    /// （文档："神庙鼠/克勒妮之子/克勒妮之盾 位于上面时，获得 1 点力量"）
    /// </summary>
    private void EvaluateBlanket(Unit unit)
    {
        if (unit == null || unit.GetCurrentHealth() <= 0) return;
        if (HexGrid.Instance == null) return;

        HexCell cell = HexGrid.Instance.GetCell(unit.GetCoord());
        TerrainData terrain = cell != null ? cell.terrain : null;

        bool onBlanket = terrain != null &&
                         terrain.terrainType == TerrainType.克勒妮绒毯 &&
                         terrain.strengthBuff != null &&
                         IsEligible(terrain, unit);

        if (onBlanket)
        {
            if (!unit.HasBuff(terrain.strengthBuff))
            {
                for (int i = 0; i < Mathf.Max(1, terrain.strengthStacks); i++)
                    unit.ApplyBuff(terrain.strengthBuff);
            }
        }
        else
        {
            // 离开绒毯就把之前给过的力量收回
            foreach (var t in GetAllTerrainData())
            {
                if (t.terrainType == TerrainType.克勒妮绒毯 && t.strengthBuff != null && unit.HasBuff(t.strengthBuff))
                    unit.RemoveBuff(t.strengthBuff);
            }
        }
    }

    /// <summary>单位是否符合该地形的受益名单（名单为空 = 全部受益）</summary>
    private bool IsEligible(TerrainData terrain, Unit unit)
    {
        if (terrain.grantedUnits == null || terrain.grantedUnits.Count == 0)
            return true;

        if (unit.data == null) return false;
        return terrain.grantedUnits.Contains(unit.data);
    }

    // ============================================================
    //      ② 结算所有 buff 时：灼热/剧毒/吸血地板 + 烟雾计时
    // ============================================================

    private void HandleBuffsSettled()
    {
        settledRounds++;

        ApplyDamageFloors();
        AdvanceSmoke();
    }

    /// <summary>灼热/剧毒/刀刃/吸血地板：回合结束对上面的单位生效</summary>
    private void ApplyDamageFloors()
    {
        // ---------- 挂 buff 的三块地板 ----------
        ApplyBuffFloor(TerrainType.灼热地板, t => t.burnBuff);
        ApplyBuffFloor(TerrainType.剧毒地板, t => t.poisonBuff);
        ApplyBuffFloor(TerrainType.刀刃地板, t => t.bleedBuff);

        // ---------- 吸血地板：直接造成真实伤害 ----------
        foreach (var cell in GetTerrainCells(TerrainType.吸血地板))
        {
            if (cell.occupyUnit == null) continue;
            TerrainData t = cell.terrain;
            // 文档：对上面的所有单位造成真实伤害
            cell.occupyUnit.TakeDamage(t.trueDamagePerTurnEnd, DamageType.真实);
        }
    }

    /// <summary>给地板上站着的单位挂 buff（对应的 buff 资产没配就整块跳过）</summary>
    private void ApplyBuffFloor(TerrainType type, System.Func<TerrainData, BuffData> pickBuff)
    {
        foreach (var cell in GetTerrainCells(type))
        {
            if (cell.occupyUnit == null) continue;
            BuffData buff = pickBuff(cell.terrain);
            if (buff != null)
                ApplyStacks(cell.occupyUnit, buff, cell.terrain.stacksPerTurnEnd);
        }
    }

    /// <summary>按层数调用多次 ApplyBuff（ApplyBuff 每次只叠 1 层）</summary>
    private void ApplyStacks(Unit unit, BuffData buff, int stacks)
    {
        for (int i = 0; i < Mathf.Max(0, stacks); i++)
            unit.ApplyBuff(buff);
    }

    /// <summary>推进烟雾喷发：每到 smokeInterval 个回合切换一次开关</summary>
    private void AdvanceSmoke()
    {
        foreach (var cell in GetTerrainCells(TerrainType.烟雾地板))
        {
            TerrainData t = cell.terrain;
            int interval = Mathf.Max(1, t.smokeInterval);
            if (settledRounds % interval == 0)
                cell.isSmoky = !cell.isSmoky;
        }
    }

    // ============================================================
    //                        工具方法
    // ============================================================

    /// <summary>拿到场景里所有配置了指定类型地形的格子</summary>
    private List<HexCell> GetTerrainCells(TerrainType type)
    {
        List<HexCell> result = new List<HexCell>();
        if (HexGrid.Instance == null) return result;

        foreach (var cell in HexGrid.Instance.GetAllCells())
        {
            if (cell.terrain != null && cell.terrain.terrainType == type)
                result.Add(cell);
        }
        return result;
    }

    /// <summary>拿到场景里所有出现过的地形数据（用于回收绒毯 buff）</summary>
    private List<TerrainData> GetAllTerrainData()
    {
        List<TerrainData> result = new List<TerrainData>();
        if (HexGrid.Instance == null) return result;

        foreach (var cell in HexGrid.Instance.GetAllCells())
        {
            if (cell.terrain != null && !result.Contains(cell.terrain))
                result.Add(cell.terrain);
        }
        return result;
    }
}
