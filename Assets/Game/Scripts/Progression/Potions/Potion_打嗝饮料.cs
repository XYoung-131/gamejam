using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  【打嗝饮料】  稀有度：普通
//
//  ── 策划原文（飞书《饮料（药水）及效果一览》）────────────
//    · 效果：将所有相邻目标推远1格
//    · 稀有度：普通
//    · 补充：（无）
//
//  ── 实现方式 ─────────────────────────────────────────────
//  走 PotionData 基类的「击退相邻目标」分支，本类覆写了
//  PushNeighborsAway()，把基类的简易位移换成位移系统的正式接口。
//
//  判定"相邻"：以自己所在格为中心，取 6 个邻居格（HexCoord.GetAllNeighbors），
//              邻居格上站着谁就推谁。不推自己、不推障碍物、不推尸体。
//
//  推出方向：远离自己。位移系统的 ApplyDisplacement 用 reference 定方向，
//            把"自己"当 reference 就是"朝外飞"。
//
//  ── 为什么覆写、而不是直接用基类的 PushBack ───────────────
//  基类的 PushNeighborsAway 用的是 Unit.PushBack —— 那是个"只挪位置、
//  不算碰撞"的简化版。而全局其他击退（技能"推""撞击"等）走的是
//  Unit.ApplyDisplacement，会结算：
//    · 撞上障碍物 / 其它单位 → 立即停下，双方各扣碰撞伤害；
//    · 撞上地图边界 → 只扣自己的碰撞伤害；
//    · 位移途中血量归零 → 先记着，位移结算完再统一死亡（避免中途消失）。
//  同一件"被人推开"的事，不应该因为来源是药水还是技能就有两套判定，
//  所以这里统一走 ApplyDisplacement。
//
//  ⚠ 行为差别提醒策划：用正式位移后，把敌人推进障碍物会**造成碰撞伤害**；
//    基类的简化版不会。如果策划要的是"纯位移、不产生任何伤害"，
//    把本类的 PushNeighborsAway 删掉即可退回基类行为。
//
//  ── 建 asset ─────────────────────────────────────────────
//  编辑器里：Assets 右键 → Create → Game/Potion/打嗝饮料
//  数值已在 Reset() 里预填，创建出来即可用，无需手填。
//  建议存放路径：Assets/Game/ScriptableObjects/饮料/打嗝饮料.asset
// ============================================================

[CreateAssetMenu(fileName = "打嗝饮料", menuName = "Game/Potion/打嗝饮料")]
public class Potion_打嗝饮料 : PotionData
{
    /// <summary>策划表里写死的击退格数</summary>
    private const int PushDistance = 1;

    /// <summary>
    /// 新建资产时预填策划数值（只影响新建/重置）。
    /// </summary>
    private void Reset()
    {
        potionName = "打嗝饮料";
        description = "将所有相邻目标推远1格";
        rarity = PotionRarity.普通;

        // 效果只有一条：把周围 1 格内的单位推远 1 格
        // value 在这里的含义是"击退格数"，不是伤害也不是回复量
        effects.Clear();
        effects.Add(new PotionEffect
        {
            effectType = PotionEffectType.击退相邻目标,
            targetType = PotionTargetType.相邻目标,
            value = PushDistance
        });

        maxUsesPerBattle = 0;   // 0 = 不限次数

        penaltyAfterUses = 0;
        penaltyDrainResources = false;
        penaltyBuff = null;
        rewardRelic = null;

        devNotes = "策划表原文：效果「将所有相邻目标推远1格」，稀有度「普通」。" +
                   "实现上走 Unit.ApplyDisplacement（与技能击退同一套碰撞规则），" +
                   "因此推进障碍物时会结算碰撞伤害。";
    }

    /// <summary>
    /// 把喝饮料的人周围 1 格内的所有单位往外推 distance 格。
    ///
    /// 关键点：
    /// ① 用 HexGrid 取 6 个邻居格里站着的单位，而不是用 Unity 的物理距离——
    ///    六边形棋盘的"相邻"必须按格子算，和射程 / 位移系统保持一致；
    /// ② 先快照出一份要推的名单再逐个推，避免边遍历边改占用关系时漏人；
    /// ③ 跳过尸体（Destroy 延迟到帧末，刚死的单位对象还在场上）。
    /// </summary>
    protected override void PushNeighborsAway(Unit user, int distance)
    {
        if (HexGrid.Instance == null || distance <= 0) return;

        HexCoord center = user.GetCoord();

        // ① 先收集名单（快照）
        var targets = new List<Unit>();

        foreach (var neighbor in center.GetAllNeighbors())
        {
            HexCell cell = HexGrid.Instance.GetCell(neighbor);
            if (cell == null || cell.occupyUnit == null) continue;

            Unit occupant = cell.occupyUnit;
            if (occupant == user) continue;              // 不推自己
            if (occupant.IsDead) continue;               // 不推尸体
            if (occupant.GetCurrentHealth() <= 0) continue;

            targets.Add(occupant);
        }

        // ② 逐个推：以"自己"为参照点 → 方向 = 远离自己
        foreach (var pushed in targets)
        {
            if (pushed == null || pushed.IsDead) continue;
            pushed.ApplyDisplacement(DisplacementType.击退, distance, user);
        }

        Debug.Log($"{user.data.unitName} 喝下【{potionName}】，把 {targets.Count} 个相邻目标推远 {distance} 格");
        EventManager.Trigger("PotionPushed", user, distance);
    }
}
