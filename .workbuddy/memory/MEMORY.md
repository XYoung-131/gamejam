# 项目长期记忆

## 项目性质
Unity 2022.3.62f3 六边形俯视角战棋游戏（聚光灯 GameJam），纯 C# 无命名空间，全局类型名。

## 术语约定
- **"类型文件"** = ScriptableObject 的类定义文件（如 `CardData.cs` / `UnitData.cs` / `ObstacleData.cs`），
  供在编辑器里创建 `.asset` 配置资产使用。不是指 `.asset` 本身。
- 配置资产统一放在 `Assets/Game/ScriptableObjects/<分类>/`，脚本放 `Assets/Game/Scripts/<模块>/`。

## 仓库约定
- `README.md` 第 3 行要求：提交文件后必须在 README 里说明所提交文件的作用。
- 代码注释全部用中文，用 `// ═══` 分隔大段，`[Header]` / `[Tooltip]` 也写中文。

## 关键架构
- `EventManager`：静态字符串事件总线，所有跨模块通信都走它。
- `Unit`（抽象基类）：血量/护盾/移动/受伤/死亡/buff 系统。`PlayerUnit` 额外管耐力与思绪。
- `PlayerUnit` 耐力 = 行动资源（`currentStamina`），思绪 = 卡牌资源（`currentThought`）。
- `CardData.Resolve(caster, target)` 是打牌的结算入口；`BuffData` 的 `BuffEffectType` + `BuffTrigger` 驱动 buff。
- 新增系统若依赖尚未实现的模块，惯例是：先广播 `EventManager` 事件预留接入点 + 写 TODO 注释。

## Buff 系统规则（重要，勿改）
- buff 分 `BuffCategory.伤害类` 和 `影响类`：
  - **伤害类**：效果在【单位回合开始时】结算（`Unit.OnTurnStart` → `TriggerBuffs(回合开始)`），
    对应 effects 的 trigger 填 `回合开始`。中毒/灼烧/流血属于此类。
  - **影响类**：获得即生效，持续查询（`TriggerBuffs(立即生效)` / `Unit.GetTotalEffectValue`），
    对应 trigger 填 `立即生效`。
  - 两者的**持续时间和层数都在回合结束时衰减**（`Unit.OnTurnEnd` → `TickBuffDuration`）。
- 层数衰减由 `BuffStackDecay` 表达，留"默认"时按极性走：负面 → 回合结束减 1，正面 → 不减少。
  特殊：流血=回合结束减半（向上取整，1 层清零）；亲密=回合结束消失；中毒=3 回合未叠加则清空全部层数。
- **数值约定（最容易填错）**：字段名带"倍率"填**绝对乘数**（沉重 1.25、力竭 0.5、易伤 1.25）；
  带"加成/减免"填**增量**（力量 +1、无力 -1、殊死一搏 3）。
- 全部 20 个 buff 的填写对照表写在 `BuffData.cs` 文件头注释里。
- **查询约定（踩过坑）**：属性类加成一律用 `Unit.GetTotalEffectValue(effectType)`（**不限触发时机**）。
  曾经用 `BuffTrigger.受到伤害时` / `攻击时` 去查，导致易伤、殊死一搏减伤、力量/无力全部静默失效。
  带 trigger 的重载只留给"按回合/按事件触发"的效果（回合开始的持续伤害等）。
- **倍率 vs 加成的合并规则（踩过坑）**：
  - 加成型（力量 +1、无力 -1、殊死一搏 3）→ 数值直接累加，无 buff 时返回 **0**
  - 倍率型（易伤 1.25、力竭 0.5、虚弱/沉重 1.25）→ `1 + Σ(value-1)×层数`，无 buff 时返回 **1**，下限 0
  - 分类器是 `BuffData.IsMultiplierType()`。**新增任何"XX倍率"效果必须去那里登记**，
    否则会被当加成型处理（1.25+1.25 = 2.5，伤害翻倍）。
- **填表约定**：倍率型填**绝对乘数**（1.25 / 0.5），加成/减免型填**增量**（+1 / -1 / 3）。
- **层数前提**：`Unit.ApplyBuff` 只在 `buffData.canStack == true` 时才叠加层数，
  而 `canStack` 默认 `false`、`maxStack` 默认 `1`。中毒/灼烧/流血/残废/无力这些按层数结算的 buff，
  做 asset 时必须勾 `canStack` 并把 `maxStack` 调大，否则层数永远停在 1。
  同理，地形绒毯的"力量"是按 `strengthStacks` 循环 `ApplyBuff` 叠上去的，也依赖 `canStack`。
- **层数 = 负面 buff 的存活回合数**：回合结束时层数按 `stackDecay` 衰减，归零即消失。
  所以负面 buff 的 `duration` 一般留 -1（靠层数走完生命周期）；正面 buff 反之靠 `duration` 计命。
  ⚠ 给负面 buff 同时填 `duration` 和默认衰减，它会在第一个回合结束就消失（层数 1 → 0）。
- **哪些字段已接运行时**（2026-10-15 补齐，之前有 7 个方法零调用）：
  `onlyForPlayerUnit` → `ApplyBuff` 调 `CanApplyTo`；`halveStacksOnEliteAndBoss` →
  `ApplyBuff` 调 `CalcStacksOnApply`；`stackDecay` → `TickBuffDuration` 调 `CalcStacksAfterTurnEnd`；
  `clearIfNotStackedTurns` → `TickBuffDuration` 调 `ShouldClearForNoStack`；
  耐力/思绪回复倍率 → `PlayerUnit.OnTurnEnd`；移动耐力消耗倍率 → `PlayerUnit.TryMoveTo`。

## 死亡判定（不要绕过）
- `Unit.IsDead` / 内部字段 `isDead`：`Die()` 里置位且**防重复**（避免"持续伤害打死"和
  "卡牌打死"两条路各跑一次 `Die()`，导致 `UnitDied` 发两遍、`LootSystem` 掉两次战利品、
  子类重复判胜负）。
- 原因：Unity `Destroy` **延迟到帧末执行**，`Die()` 之后对象还会被调用若干次。
- **子类一律覆写 `OnDeath()`，不要覆写 `Die()`**：`Die()` 顶部的 `if (isDead) return;`
  只在基类方法体内生效，覆写 `Die()` 的子类代码会绕过它、重复执行。
  这是 2026-10-15 新引入的扩展点，`PlayerUnit` / `EnemyUnit` 已改造。
- 判活覆盖面：`TakeDamage` / `TakeDamageInternal` / `ApplyBuff` / `Heal` / `AddShield` /
  `MoveTo` / `ApplyDisplacement` / `TeleportTo` / `PushBack` / `StepTo` /
  `OnTurnStart` / `OnTurnEnd` 全部已加 `if (isDead) return;`；
  `CanAttack()` 内含 `!isDead`，所以卡牌结算（`CardData.Resolve`）自动拦截死亡单位。
- **`CardData.Resolve` 的时序陷阱**：结算顺序是"造成伤害 → 挂 buff"，伤害可能已改变生死局面
  （目标被打死 / 施法者被反伤打死）。buff 循环里必须**每次施加前重新判活**，
  不能复用伤害结算之前的存活快照。

## 数值结算分工（避免重复计算）
- **施法方加成**在 `CardData` 算：`GetFinalDamage`（攻击伤害加成）、
  `GetFinalActionCost`（攻击耐力消耗倍率）、`GetEffectiveMaxRange`（位移类受移动距离加成，传送类不修正）。
- **挨打方减益**在 `Unit.CalcIncomingDamage` 算（`受到伤害固定减免` → `受到伤害倍率`，顺序不能反），
  `TakeDamage` 与 `TakeDamageInternal`（持续伤害）**共用**这一份。
  `TakeDamageInternal` 与 `TakeDamage` 的差异只剩：不触发"受到伤害时"事件、不广播 `UnitDamaged`
  （避免持续伤害递归）。
  ⚠ 曾经 `TakeDamageInternal` 完全跳过 buff 计算，导致中毒/灼烧/流血不吃易伤、且无敌挡不住 DoT。
- 两边绝不能各算一次同一个倍率，否则伤害会平方叠加。
- 射程判定统一走 `CardData.IsInEffectiveRange(caster, from, to)`，玩家侧和敌人侧共用，不要各写一份。

## 已知缺口
- `RelicData.技能耐力消耗倍率` 用【增量】填法（滑板鞋 -25% 填 `-0.25`），
  `BuffData.攻击耐力消耗倍率` 用【绝对乘数】填法（虚弱 +25% 填 `1.25`）。
  同一概念两种填法，极易填错。倾向统一为增量填法（待策划决定）。
- 护盾清空时机两处并存（`OnTurnStart` 的 `ClearShield()` + `OnTurnEnd` 的 `ClearShieldOnTurnEnd()`），
  净行为 = 回合结束清零，`ClearShield()` 实际空转。待定唯一权威时机。
- 力竭/混乱的资源倍率只作用于`PlayerUnit.OnTurnEnd` 的回合回复，未作用于药水/遗物的即时回复。

## 飞书策划案
- 策划案 Base：`https://qcnj9ebuvbz4.feishu.cn/base/UB8AbotC0aFJJwsQFy0cXSIBnkb`
- 主要表：攻击技能大全 `tblvYy6GtVL0UdER`、防御技能大全 `tbl4v5ZSB25fCuuN`、
  怪物大全 `tblzzbI4i02Psv1l`、障碍物大全 `tblW6dCTaONGX9Gw`、
  思维碎片（遗物）大全 `tblCUdqSZFR8MpzF`、饮料（药水）及效果一览 `tblHnbpQJfQ3vGiw`
