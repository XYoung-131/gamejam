# gamejam
这是聚光灯gamejam的项目库
请提交文件后在这里说明所提交的文件是什么作用

<img width="800" height="738" alt="4331bf29dbf016e466a3636b5cd90311" src="https://github.com/user-attachments/assets/422e5914-8e27-4dba-af94-da4fc8086378" />

小羊：
这是我们项目所使用的坐标系，Axial坐标系的示意图，这个坐标系的逻辑我已经上传到github库，就是HexCoord.cs文件

小羊：
创建了几个类型文件，供创建.asset文件使用：
< table > < thead > <tr>
      <th align="left">文件名</th>
      <th align="left">类型</th>
      <th align="left">作用</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>ObstacleData.cs</td>
      <td>ScriptableObject</td>
      <td>障碍物配置表，填名字、血量、标签（环境/高大）、特殊能力描述</td>
    </tr>
    <tr>
      <td>CardData.cs</td>
      <td>ScriptableObject</td>
      <td>卡牌配置表，.asset 文件，填消耗、稀有度、标签、效果</td>
    </tr>
    <tr>
      <td>UnitData.cs</td>
      <td>ScriptableObject</td>
      <td>单位配置表，.asset 文件，填名字、血量、品级、技能组</td>
    </tr>
  </tbody>
</table>

小羊：
这次我上传了一部分底层逻辑文件，我会简单的解释一下他们的作用：
< table > < thead > <tr>
      <th align="left">文件名</th>
      <th align="left">类型</th>
      <th align="left">作用</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>HexGrid.cs</td>
      <td>单例 MonoBehaviour</td>
      <td>六边形网格大管家，负责生成整张地图、六边形网格坐标和Unity2D坐标转换、按坐标查格子</td>
    </tr>
    <tr>
      <td>HexCell.cs</td>
      <td>MonoBehaviour</td>
      <td>单个格子，存坐标、能不能走、走起来花费多少行动力、上面站了谁（单位/障碍物）</td>
    </tr>
    <tr>
      <td>HexPathfinding.cs</td>
      <td>静态工具类</td>
      <td>A* 寻路算法，算最短路径、下一步往哪走、某范围内能到哪些格子</td>
    </tr>
    <tr>
      <td>GameManager.cs</td>
      <td>单例 MonoBehaviour</td>
      <td>游戏状态机，管回合切换（玩家回合↔敌人回合）、暂停、胜利/失败（注：此文件可类比main函数）</td>
    </tr>
    <tr>
      <td>EventManager.cs</td>
      <td>静态工具类</td>
      <td>全局事件总线，模块之间发消息用，不用互相引用（注：各种事件都要经过这个文件，这样以后改bug会减少很多工作量）</td>
    </tr>
    <tr>
      <td>CameraController.cs</td>
      <td>MonoBehaviour</td>
      <td>相机控制，拖拽平移、滚轮缩放、边界限制，不让相机移出地图</td>
    </tr>
    <tr>
      <td>Unit.cs</td>
      <td>抽象基类</td>
      <td>所有活物的通用框架：血量、护盾、移动、受伤、死亡、buff 系统、回合生命周期</td>
    </tr>
    <tr>
      <td>PlayerUnit.cs</td>
      <td>MonoBehaviour</td>
      <td>玩家单位，处理玩家输入：选中、移动、攻击、回合结束</td>
    </tr>
    <tr>
      <td>EnemyUnit.cs</td>
      <td>MonoBehaviour</td>
      <td>敌人 AI，自动寻路靠近玩家，到攻击距离就打，打完结束回合</td>
    </tr>
    <tr>
      <td>Obstacle.cs</td>
      <td>MonoBehaviour</td>
      <td>障碍物基类，能被打、能被打爆，"高大"标签的挡视野</td>
    </tr>
  </tbody>
</table>

月下：新增
- TurnManager.cs --- 回合总编排（回合开始预告 → 玩家行动 → Buff结算 → 敌方行动 → 回合结束）
- PlayerCardSystem.cs — 槽位、6选3灌注、窃取敌方技能、出牌

     修改
- CardData.cs — 新增`CardPool{攻击,防御}` 分类字段和统一的`Resolve()` 卡牌结算
- PlayerUnit.cs — 新增思绪/耐力（耐力=现有行动点）消耗接口
- EnemyUnit.cs — 新增技能预告生成、执行、上回合技能记录

    新增
- 增加技能范围
- 增加攻击技能伤害数值
- 增加攻击技能目标类型
- 增加防御技能治疗/护斥盾数值
- 增加防御技能目标类型
- 增加卡牌使用次数

## 药水 / 遗物 类型文件（来源：飞书多维表格《饮料（药水）及效果一览》《思维碎片（遗物）大全》）

新增
- `PotionData.cs`（Assets/Game/Scripts/Progression/）—— 饮料（药水）配置表，ScriptableObject。
  字段：名称、描述、图标、稀有度（普通/特殊）、每场战斗使用上限、效果列表
  （回复耐力/回复思绪/治疗/护盾/免费使用技能/击退相邻目标/施加Buff/清除负面）、
  战斗内使用次数惩罚（"劣质混合饮料"第3瓶扣光资源+眩晕、战斗结束掉落遗物）、策划备注。
  入口：`Resolve(user, usedCountThisBattle)` 结算喝下这瓶饮料的全部效果。
- `RelicData.cs`（Assets/Game/Scripts/Progression/）—— 思维碎片（遗物）配置表，ScriptableObject。
  字段：名称、描述、图标、类型（特殊/事件掉落/精英怪掉落/BOSS掉落/专属）、
  效果列表（触发时机 × 效果类型 × 数值 × 作用范围）、技能转换配置（"玛塔的板牙"）、
  成长计数配置（每击杀 N 个敌人永久加伤）、策划备注。
  入口：`OnAcquire()` 结算获得时效果，`Trigger()` 结算各触发时机的效果；
  另提供 `GetTotalValue()` / `HasEffect()` / `IsImmuneToBuff()` 三个静态查询方法，
  供伤害结算、卡牌系统反查玩家遗物加成。

修改
- `BuffData.cs` —— `BuffEffectType` 补上 `清除所有正面` 成员。`Unit.cs` 第295行已经在用这个成员，
  缺失会导致整个工程编译失败。
- `PlayerUnit.cs` —— 新增 `RestoreStamina` / `RestoreThought` / `RestoreAllResources` /
  `DrainAllResources` 四个方法，供药水和遗物回复 / 清空耐力与思绪。

待接入（代码中已用 EventManager 预留事件，对应系统做好后补实现）
- `FreeSkillGranted` —— 免费使用技能（"快走饮料"）
- `RelicDropItem` —— 遗物掉落（"劣质饮料机"）
- `RelicSkillConvert` —— 技能转换（"玛塔的板牙"）
- `RelicSpecialEffect` —— 特殊机制（"反射神经""垃圾毁灭者""笑靥"）

## Buff 配置表重写（覆盖全部 20 个 buff）

重写
- `BuffData.cs`（Assets/Game/Scripts/Cards/）—— 按策划给的全部 buff 列表重写了类型文件，结构仿 `CardData`。
  - 新增 `BuffCategory`（伤害类 / 影响类），对应"伤害类回合开始结算、影响类获得即生效"的规则。
  - 新增 `BuffStackDecay`（默认 / 不减少 / 回合结束减1 / 回合结束减半 / 回合结束消失 / 未叠加则清空），
    用来表达"没特殊标识就回合结束层数-1""流血层数减半""亲密回合结束消失""中毒3回合未叠加清空"。
  - `BuffEffectType` 补齐到 18 项，新增 `攻击目标限制`（亲密）、`特殊机制`（神圣 / 荧光）。
  - `BuffData` 新增字段：`category`、`stackDecay`、`clearIfNotStackedTurns`、
    `halveStacksOnEliteAndBoss`（残废/无力）、`onlyForPlayerUnit`（力竭/混乱"对敌人无效"）、
    `forbiddenAttackTargets`（亲密）。
  - 新增结算辅助方法：`CalcStacksOnApply` / `CanApplyTo` / `CalcStacksAfterTurnEnd` /
    `ShouldClearForNoStack` / `IsForbiddenAttackTarget`，以及查询属性 `IsDebuff` / `IsDamageOverTime` /
    `IsPermanent` / `IsControl` / `ResolvedDecay`。
  - 文件头注释里带全部 20 个 buff 的填写对照表，以及"倍率类填绝对乘数、加成类填增量"的数值约定。

注意
- `Unit.cs` 引用的所有 `BuffData` 成员名均已保留，未改动运行时逻辑。
- 数值约定：**倍率类字段填绝对乘数**（沉重 1.25、力竭 0.5、易伤 1.25），
  **加成/减免类字段填增量**（力量 +1、无力 -1、殊死一搏 3）。

## 攻击结算接入 Buff（CardData / Unit / PlayerCardSystem / EnemyUnit）

修复
- `Unit.cs` —— **三处 `trigger` 查询与 BuffData 填写约定不一致，导致属性类 buff 完全失效**：
  - `TakeDamage` 里 `受到伤害固定减免`（殊死一搏 -3）原按 `BuffTrigger.受到伤害时` 查询；
  - `TakeDamage` 里 `受到伤害倍率`（易伤 1.25）原按 `BuffTrigger.受到伤害时` 查询；
  - `GetAttack` 里 `攻击伤害加成`（力量 +1 / 无力 -1）原按 `BuffTrigger.攻击时` 查询。
  但 `BuffData` 的约定是"影响类 buff 的属性效果统一填 `立即生效`"，上述三处查询因此永远返回 0。
  已统一改为按 `立即生效` 查询；`GetAttack` 结果增加下限 0 钳制。

新增
- `CardData.cs` —— 新增"buff 生效后的最终数值"区，攻击结算正式接上影响类 buff：
  - `GetFinalDamage(caster)` —— 最终伤害 = 卡牌 `damage` + 「攻击伤害加成」×层数，下限 0。
  - `GetFinalActionCost(caster)` —— 最终耐力消耗 = 攻击技能 `actionCost` × 「攻击耐力消耗倍率」（虚弱 1.25），向上取整；防御技能不受影响。
  - `GetEffectiveMaxRange(caster)` / `IsInEffectiveRange(...)` —— 位移类技能的最大距离受「移动距离加成」修正（敏捷 +1 / 残废 -1），**传送类不修正**（带"传送"标签直接返回原值）。
  - `IsMovementSkill` / `IsTeleportSkill` 两个标签查询属性。
- `Unit.cs` —— 新增 `IsAttackTargetForbidden(Unit)`，遍历自身 buff 判断目标是否落在"亲密"的禁打名单里。

修改
- `CardData.Resolve(Unit, Unit)` / `Resolve(Unit, Obstacle)`：
  - 开头新增"禁止行动"（眩晕）兜底——被控住的单位任何牌都放不出（原先只有玩家侧 `ValidateSlot` 拦过，敌人走 `Resolve` 这条路是漏的）；
  - 伤害改用 `GetFinalDamage(caster)`，日志同步打印真实伤害；
  - 打障碍物也走同一套伤害公式，不再有第二份算法；
  - 新增"亲密"禁打目标的兜底判断。
- `PlayerCardSystem.cs`：
  - `UseSlotCard` / `UseSlotCardOnObstacle` 的耐力扣费改用 `card.GetFinalActionCost(player)`；
  - 新增"亲密"目标校验，**拦在扣费之前**（否则会出现扣了耐力却打不出去）；
  - `IsInRange` 的最大射程改用 `card.GetEffectiveMaxRange(player)`；
  - `FindNearestEnemy` 自动索敌时跳过被"亲密"禁止的单位。
- `EnemyUnit.cs` —— `ExecutePlannedSkills` 的射程校验改用 `card.IsInEffectiveRange(...)`，和玩家侧共用同一份判定逻辑。

待接入
- 上述数值现在"能算"，但要让玩家在 UI 上看到，仍需：目标选择界面按 `IsAttackTargetForbidden` 置灰、卡牌悬浮提示显示 `GetFinalActionCost` 修正后的消耗。
- 遗物侧的常驻加成（强韧布料减伤、黄铜指虎加伤等）仍待 `Unit.TakeDamage` 与 `CardData.GetFinalDamage` 主动反查 `RelicData`。

作者：（待填）

## 修复：倍率叠加、死亡判定、攻击加成读取（Unit / BuffData / CardData）

修复
- `Unit.cs` —— **倍率型效果的层数叠加原来是错的（把乘数当增量相加）**。
  `GetTotalEffectValue` 原实现一律 `total += effect.value * 层数`，
  易伤（1.25）叠 2 层会被算成 2.5 倍，凭空多出一倍基数。现按效果类型区分：
  - **加成型**（力量 +1、无力 -1、殊死一搏 3）：数值直接累加；
  - **倍率型**（易伤 1.25、力竭 0.5、虚弱 1.25、沉重 1.25）：累加"偏离 1"的部分再乘回去，
    即 `最终倍率 = 1 + Σ(value - 1) × 层数`。易伤 2 层 = 1.5，力竭 2 层 = 0；
    没有该类型 buff 时返回 1（不变），加成型返回 0。
  分类依据是新增的 `BuffData.IsMultiplierType(BuffEffectType)`（以后加新"倍率"效果要同步登记）。
- `Unit.cs` —— **新增死亡判定，堵住"给已死单位挂 buff"**。
  Unity 的 `Destroy` 延迟到帧末执行，`Die()` 之后对象还会被调用若干次。新增字段 `isDead` + 只读属性 `IsDead`：
  - `Die()` 开头置位并防重复（避免"持续伤害打死"+"卡牌打死"两条路各跑一次 `Die()`，
    导致 `UnitDied` 发两遍、`PlayerUnit` 重复判定全员阵亡）；
  - `TakeDamage` / `TakeDamageInternal` / `ApplyBuff` / `MoveTo` 开头一律 `if (isDead) return;`；
  - `CanAttack()` 改为 `!isDead && !禁止行动` —— 卡牌结算用它兜底，死亡单位自然放不出牌；
  - `Initialize()` 重置 `isDead = false`。
- `CardData.cs` —— **`Resolve` 里"先打死、后挂 buff"的时序问题**。
  结算分为"造成伤害 → 挂 buff"两步，而伤害可能已经改变生死局面（目标被打死，或施法者被反伤打死）。
  第 3 步的 buff 循环原来复用伤害结算**之前**的存活快照，现改为每次施加前重新判活：
  负面 buff 要求施法者与目标都存活，正面 buff 要求施法者存活。障碍物重载同步处理。
- `CardData.cs` —— `GetFinalActionCost` 的倍率兜底修正：去掉"0 则按 1 处理"的旧写法
  （倍率型查询现在没有 buff 时返回 1），改为 `Mathf.Max(0f, rate)`。
- `BuffData.cs` —— `GetTotalValue` 的合并规则同步改为倍率感知；参数 `trigger` 改为可空，
  省略即"不限时机"，并补上返回值语义说明（倍率型无匹配时返回 1）。

修改
- `Unit.cs` —— `GetTotalEffectValue` 拆成两个重载：
  - `GetTotalEffectValue(effectType)`：**不限触发时机**，属性类加成统一用它。
    策划把效果填成"立即生效"还是"攻击时"/"受到伤害时"都算数，
    彻底消除"填错 trigger 就静默失效"这一类问题；
  - `GetTotalEffectValue(effectType, BuffTrigger? trigger)`：只统计指定时机，
    留给"按回合/按事件触发"的效果用。
  相应地 `GetAttack` / `GetMoveBonus` / `TakeDamage` 的两个属性查询都改用不限时机版本。

已知不一致（待策划确认后统一）
- `RelicData` 的同名效果「技能耐力消耗倍率」用的是**增量**填法（滑板鞋 -25% 填 `-0.25`），
  而 `BuffData` 用的是**绝对乘数**填法（虚弱 +25% 填 `1.25`）。同一件事两种填法，容易填错。
  建议统一为一种（倾向改成"增量"填法，与策划文案"增加 25%"字面一致）。暂未改动。

作者：（待填）

## 合并 PR #3（位移/地形/掉落系统）+ 冲突解决记录

合并
- 合并 origin/main（PR #3 from Zykluna233：位移系统、地形系统、掉落系统、
  怪物行动优先级、远程视线判定、攻击范围高亮、UnitGrade.BOSS、回合结束回 15 资源等）。
- 网络问题：git 直连 GitHub 被重置，已配置仅对 github.com 生效的本地代理
  （`git config --global http.https://github.com.proxy http://127.0.0.1:7897`）。

冲突解决（按"shield 取 B 侧、其余取 A 侧"的约定执行）
- `Unit.cs` —— 取队员版（位移系统完整），重新叠加本地修复：
  `GetTotalEffectValue` 拆成"不限时机"+"指定时机"两个重载（倍率感知叠加）、
  `isDead` 死亡判定（Die 防重复 + TakeDamage/ApplyBuff/MoveTo/CanAttack 拦截）、
  `IsAttackTargetForbidden`（亲密禁打）。
- `BuffData.cs` —— 保留本地重写版（20 buff 完整体系）；
  队员版新增的 `清除所有正面` 已包含，其 `BuffCategory`（伤害类/效果类/护盾类）无其他引用，未引入。
- `CardData.cs` —— 保留本地版（最终数值区 + 判活挂 buff），
  叠加队员的 `damageType` 字段、位移配置块、`SkillPriority`、
  伤害结算改 `TakeDamage(GetFinalDamage(caster), damageType)`、新增位移步骤（第 1.5 步）。
- `PlayerCardSystem.cs` —— 保留本地版（最终消耗 + 亲密前置校验），
  叠加队员的视线判定 `IsTargetReachable`（两处调用点）、抽卡费 `drawThoughtCost` 1→5、
  `HighlightAttackRange` / `ClearHighlights`。
- `Progression/RelicData.cs` —— 融合：本地 22 遗物效果体系为主，补队员简化路径
  （`RelicPool` 枚举 + `pool` 字段 + `permanentBuffs`/`instantEffect`/`instantHeal`/`instantBuffs`
  + 兼容入口 `Apply(Unit)`，内部走 `ApplyPermanentBuff` 再调 `OnAcquire`）。
- `Progression/PotionData.cs` —— 保留本地版（已有兼容的 `Resolve(Unit)` 重载，
  PlayerUnit.UsePotion 直接可用；未引入无引用的 `PotionQuality`）。

已知遗留
- `DamageType.Buff` 注释称"受减伤/易伤影响"，但 `TakeDamageInternal` 实际跳过 buff 计算——文档与实现不一致，待统一。
- 队员 `OnTurnStart` 的 `ClearShield()` 与本地 `OnTurnEnd` 的 `ClearShieldOnTurnEnd()` 并存，
  净行为 = 回合结束清零（`ClearShield()` 空转），待策划确认护盾规则的唯一权威时机。

作者：（待填）
