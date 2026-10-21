# gamejam

这是聚光灯gamejam的项目库  
请提交文件后在这里说明所提交的文件是什么作用

![4331bf29dbf016e466a3636b5cd90311](https://github.com/user-attachments/assets/422e5914-8e27-4dba-af94-da4fc8086378)

## 小羊：  

这是我们项目所使用的坐标系，Axial坐标系的示意图，这个坐标系的逻辑我已经上传到github库，就是HexCoord.cs文件

## 小羊：  

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

## 小羊： 

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

## 月下：新增

- TurnManager.cs --- 回合总编排（回合开始预告 → 玩家行动 → Buff结算 → 敌方行动 → 回合结束）
- PlayerCardSystem.cs — 槽位、6选3灌注、窃取敌方技能、出牌

  “修改
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

## 小羊：药水 / 遗物 类型文件

新增

- `PotionData.cs`（Assets/Game/Scripts/Progression/）—— 饮料（药水）配置表，ScriptableObject。  
  字段：名称、描述、图标、稀有度（普通/特殊/稀有）、每场战斗使用上限、效果列表  
  （回复耐力/回复思绪/治疗/护盾/免费使用技能/击退相邻目标/施加Buff/清除负面/恢复技能使用次数）、  
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

## Buff 配置表重写

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

## 小羊：攻击结算接入 Buff（CardData / Unit / PlayerCardSystem / EnemyUnit）

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


##  小羊：修复：倍率叠加、死亡判定、攻击加成读取（Unit / BuffData / CardData）

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
  

## 小羊：复查：三个已知问题的修复状态 + buff 结算需求落地（Unit / PlayerUnit / EnemyUnit / BuffData）

### 一、修复鱼丸提出的三个buff系统相关问题：已确认修复

1. **CardData.Resolve 不读"攻击时"加成** —— 已修。  
   `Resolve` 第 1 步走 `GetFinalDamage(caster)`，查询不限触发时机；打障碍物复用同一公式。  
   即使策划把 `trigger` 填成"攻击时"也照样算数，不会再静默失效。
2. **倍率被当增量相加** —— 已修。  
   `GetTotalEffectValue` 两个重载都按 `BuffData.IsMultiplierType` 分流：  
   倍率型 `1 + Σ(value-1)×层数`（易伤 2 层 = 1.5），加成型直接累加。  
   5 个倍率型效果（耐力回复/思绪回复/移动耐力消耗/攻击耐力消耗/受到伤害）全部登记在册。
3. **缺死亡判定** —— 已修，但本次又补齐了 6 处漏网点（见下）。


### 二、本次补充修复

修复
- `Unit.cs` —— **死亡判定漏网**（`Destroy` 延迟到帧末，`isDead` 之外还有入口没拦）：
  - `Heal` / `AddShield` 原先无判活 → 已死单位可被治疗/加盾，产生
    "isDead = true 但 currentHealth > 0"的僵尸状态。两处都已加 `if (isDead || amount <= 0) return;`。
  - `ApplyDisplacement` / `TeleportTo` / `PushBack` / `StepTo` 加判活。
  - `OnTurnStart` / `OnTurnEnd` 加判活（TurnManager 已过滤，这里再兜一道）。
  - `PlayerUnit.TryMoveTo` / `TryAttack` / `UsePotion`、`EnemyUnit` 同步加判活。
- `Unit.cs` / `PlayerUnit.cs` / `EnemyUnit.cs` —— **子类覆写 `Die()` 会绕过基类的防重复判断**。
  `Die()` 顶部的 `if (isDead) return;` 只在基类方法体内生效，子类覆写后的代码仍会被走两遍
  （重复判胜负、`LootSystem` 重复掉落的风险）。
  改为引入扩展点 `protected virtual void OnDeath()`：`Die()` 只在首次死亡时调用它，
  子类一律覆写 `OnDeath()`，不再碰 `Die()`。PlayerUnit / EnemyUnit 已同步改造。
- `Unit.cs` —— **持续伤害不吃减伤/易伤，与 `DamageType.Buff` 的定义矛盾**。
  该枚举注释写明"中毒/灼烧/流血…也受减伤/易伤影响"，但 `TakeDamageInternal` 原先
  "跳过 buff 计算"，导致中了易伤时持续伤害并不增加，且"无敌（免疫伤害）"挡不住持续伤害。
  抽出 `CalcIncomingDamage(amount)`（免疫 → 固定减免 → 倍率增减，只算数值不扣血，不会递归），
  `TakeDamage` 与 `TakeDamageInternal` 共用。两者的差异只剩"是否触发受到伤害时事件 / 是否广播 UnitDamaged"。
- `Unit.cs` —— buff 遍历加空值防护（`HasEffect` / `TriggerBuffEffects` /
  `RemoveAllNegativeBuffs` / `RemoveAllPositiveBuffs` / `TickBuffDuration`），脏数据不再 NRE。

新增
- `Unit.cs` —— `BuffInstance.turnsSinceLastStack` 字段，支撑"中毒：3 回合没叠层就清空"。
- `Unit.cs` —— `protected void RemoveBuffInstanceAt(int index, string eventName)`，
  统一"层数归零移除"与"持续时间到期移除"的清理与事件广播。

修改（让 BuffData 的声明真正跑到运行时）
- `Unit.ApplyBuff(BuffData, int stacks = 1)` —— 接上两个原先零调用的方法：
  - `CanApplyTo(this)` → **力竭 / 混乱的"对敌人无效"（onlyForPlayerUnit）现在真的生效**；
  - `CalcStacksOnApply(this, stacks)` → **残废 / 无力对精英怪和 BOSS 的层数减半现在真的生效**。
  另：重新叠层时把 `turnsSinceLastStack` 归零。
- `Unit.TickBuffDuration()` —— 接上层数衰减线（原先只减 `duration`，`stackDecay` 系列字段全是死的）：
  - `回合结束减1`（灼烧 / 临时力量 / 临时敏捷）、`回合结束减半`（流血，1 层即清零）、
    `回合结束消失`（亲密）、`不减少`（正面常驻）；
  - `未叠加则清空`（中毒）走 `ShouldClearForNoStack`；
  - 层数与持续时间任一归零 → 移除该 buff。
- `PlayerUnit.OnTurnEnd()` —— 回合资源回复接上 **`力竭` 耐力回复倍率 0.5 / `混乱` 思绪回复倍率 0.5**。
- `PlayerUnit.TryMoveTo()` —— 移动消耗接上 **`沉重` 移动耐力消耗倍率 1.25**（向上取整）。
- `BuffData.CalcStacksOnApply()` —— 关闭 BOSS 的 TODO（`UnitGrade.BOSS` 已由 PR#3 补上）。

### 三、"层数 = 存活回合数"这条规则（做 asset 前必须知道）

负面 buff 的层数在回合结束按 `stackDecay` 衰减，归零即消失。所以：
- **负面 buff 的 `duration` 一般留 -1**，由层数走完生命周期
  （这也解释了为什么"残废/无力对精英怪层数减半"等价于持续时间减半）；
- **正面 buff 反之**：层数不减少，靠 `duration` 计命（-1 = 永久）。
- 反例：给负面 buff 同时填 `duration = 2` 和默认衰减，它会在**第一个回合结束就消失**
  （层数 1 → 0），`duration` 根本轮不到生效。
- 同理，地形绒毯的"力量"是靠 `for (i < strengthStacks) ApplyBuff(...)` 叠上去的，
  **该 buff 资产必须勾 `canStack` 并把 `maxStack` 调够**，否则 `strengthStacks` 白填。

### 四、仍有待确认

- `RelicData` 的"技能耐力消耗倍率"用**增量**填法（滑板鞋 -0.25），
  `BuffData` 的"攻击耐力消耗倍率"用**绝对乘数**（虚弱 1.25）。同一件事两种填法，建议统一（倾向增量）。
- 护盾清空现在"回合开始 + 回合结束"两处并存，净行为 = 回合结束清零（`ClearShield()` 空转），
  待定唯一权威时机。
- 力竭/混乱的倍率目前只作用于**回合资源回复**，没有作用于药水/遗物的即时回复
  （策划文案写的是"回合开始时耐力回复减半"，按字面范围实现）。

---

## 小羊：创建各个药水逻辑文件

| 文件 | 资产 | 稀有度 | 效果 |
|---|---|---|---|
| `Potion_耐力饮料.cs` | 药水/耐力饮料 | 普通 | 回复 20 点耐力 |
| `Potion_提神饮料.cs` | 药水/提神饮料 | 普通 | 回复 20 点思绪 |
| `Potion_混合饮料.cs` | 药水/混合饮料 | 普通 | 回复 10 耐力 + 10 思绪 |
| `Potion_劣质混合饮料.cs` | 药水/劣质混合饮料 | 特殊 | 回复 8+8（**副作用隐藏**）：第 3 瓶扣光资源 + 眩晕 + 战后送遗物【饮料成瘾】 |
| `Potion_快走饮料.cs` | 药水/快走饮料 | 普通 | 免费使用 1 次「快走」 |
| `Potion_打嗝饮料.cs` | 药水/打嗝饮料 | 普通 | 将相邻目标推远 1 格 |
| `Potion_青柠饮料.cs` | 药水/青柠饮料 | 稀有 | 恢复所有技能全部使用次数 |
| `Potion_蓝莓饮料.cs` | 药水/蓝莓饮料 | 稀有 | 获得 3 层开悟（`buffStacks = 3`） |
| `Potion_橘子饮料.cs` | 药水/橘子饮料 | 稀有 | 治疗 1 点 + 移除自身所有负面状态 |
| `Potion_桃子饮料.cs` | 药水/桃子饮料 | 稀有 | 获得 3 层轻盈（`buffStacks = 3`） |
| `Potion_硬化饮料.cs` | 药水/硬化饮料 | 普通 | 获得 6 点护盾 |


### 本次新增 / 改写的能力文件

- **`Battle/TerrainData.cs`（重写）** —— 原先只有 5 种地形、用 `List<string> grantedUnitNames`
  存受益者，缺 `刀刃地板`。现按下方规格对齐：
  - `TerrainType` 枚举补齐为 6 项，**新成员一律追加在末尾**（Unity 按序号序列化，插队会串号）；
  - 受益者改为 `List<UnitData> grantedUnits`（类型安全，编辑器可直接拖单位资产）；
  - 新增 `BuffData bleedBuff`，与 `burnBuff` / `poisonBuff` 并列。
  - 字段全集：`terrainName` / `terrainType` / `description` / `grantedUnits` / `strengthStacks` /
    `strengthBuff` / `stacksPerTurnEnd` / `burnBuff` / `poisonBuff` / `bleedBuff` /
    `smokeInterval` / `trueDamagePerTurnEnd`。
- **`Battle/TerrainSystem.cs`（改写）** —— `IsEligible` 改判 `terrain.grantedUnits.Contains(unit.data)`；
  伤害/施加类地板抽成 `ApplyBuffFloor(TerrainType, Func<TerrainData, BuffData>)`，
  现统一覆盖 灼热（灼烧）/ 剧毒（中毒）/ **刀刃（流血）** / 吸血（真实伤害）。
- **`Progression/PotionData.cs`** —— 新增 `PotionEffectType.恢复技能使用次数`（**追加在枚举末尾**），
  对应 `RestoreSkillUses()` 虚方法，供"青柠饮料"实现。
- **`Cards/PlayerCardSystem.cs`** —— 新增 `RestoreAllSkillUses()`，遍历槽位把有限次技能
  `remainingUses` 回满，并广播 `CardUsesRestored`。

### 特殊地形规格

> 克勒妮绒毯：神庙鼠、克勒妮之子、克勒妮之盾 位于上面时，获得 1 点力量
> 灼热地板：回合结束，对上面的所有单位施加 1 层灼烧
> 剧毒地板：回合结束，对上面的所有单位施加 1 层中毒
> 刀刃地板：回合结束，对上面的所有单位施加 1 层流血
> 烟雾地板：（初始状态随机）每隔 1 回合喷出烟雾，位于烟雾中的单位无法被选为远程攻击的目标
> 吸血地板：回合结束，对上面的所有单位造成 1 点真实伤害


---

## 小羊：技能卡按策划表重建 + 美术资源接入 + demo 输入层

### 一、脚本改动（Assets/Game/Scripts/）

| 文件 | 作用 |
| --- | --- |
| `Core/HexCell.cs` | 格子通行判定改为 `CanEnter(unit)` / `CanStopOn()` / `IsBlockedByObstacle()` / `GetNeighborCells()`，把障碍物纳入判定，不再只判"有没有单位" |
| `Core/HexPathfinding.cs` | 整文件重写：A* 的 hCost 改为可采纳（原写法会算出次优路径）、节点变优后重新入队、路径绕开障碍物；新增 `FindPathToRange`（敌人"走到能打到"）、`GetPathCost`、`TryFindPath`；`FindNextStep` 处理终点被占的退化情况；`GetReachableCells` 不再返回 null |
| `Core/HexGrid.cs` | `IsLineBlocked` 改用 `Obstacle.BlocksSight()`（"高大"标签挡视线）；新增 `GetNeighborCells` |
| `Units/Unit.cs` | 护盾清空的权威时机统一到"回合开始"（原先回合开始与回合结束各清一次）；`TakeDamage` 增加伤害类型与来源参数并广播 `UnitDamagedBy`；持续伤害改走 `CalcIncomingDamage`，与普通伤害共用同一套减伤/易伤；新增 `GetHealthPercent` / `IsFullHealth` / `HasShield`；buff 层数衰减接上 `BuffStackDecay` |
| `Units/UnitData.cs` | 新增 `attack` 字段（普通攻击力，原先 `GetAttack` 读的是卡牌伤害） |
| `Units/PlayerUnit.cs` | 移动耐力消耗改用 `HexPathfinding.GetPathCost(path)`，与实际走的格数一致 |
| `Units/EnemyUnit.cs` | **新增 `ApproachTarget`**：技能射程不够时先走到能打到的位置（每回合最多 3 步），避免"射程 1 的怪永远站桩" |
| `Debug/BattleDebugDriver.cs` | **新增**。没有正式 UI 时的操作层（详见第三节）。正式 UI 做好后可直接删除 |

### 二、技能卡资产（Assets/Game/ScriptableObjects/技能/）

按飞书「攻击技能」「防御技能」两张表重建全部 **71 张卡**（42 攻击 + 29 防御），字段与 `CardData.cs` 一一对应
（`cardName / description / thoughtCost / actionCost / maxUses / minRange / maxRange / damage / damageType /
targetTypes / causesDisplacement / displacementType / displacementDistance / displaceSelf / defenseEffectType /
defenseValue / defenseTargetTypes / library / rarity / tags / buffs / devNotes`）。

- **GUID 全部保留**，场景里的牌库引用不会断；
- 两张卡改名但 GUID 不变：`回旋劈击 → 回旋斩击`、`神圣打击 → 裁决`；
- 多段效果的层数用"在 `buffs` 里重复引用同一 `BuffData`"表达；
- 表里 `次数` / `爆炸` 两个词条 `CardTag` 枚举装不下、`临时无力` / `临时残废` 没有对应 BuffData 资产，
  以及多段/AOE/召唤/换位/斩杀等尚未实现的条件逻辑，全部写进各卡的 `devNotes`，方便后续补。

### 三、demo 输入层（Scripts/Debug/BattleDebugDriver.cs）

`RuntimeInitializeOnLoadMethod(AfterSceneLoad)` 自举，检测到场景里有 `LevelManager` 才创建，**不改 `Battle.unity`**。

| 操作 | 效果 |
| --- | --- |
| 左键点格子 | 移动（自动寻路，按路径扣耐力） |
| Q/W/E/R/T 选槽位 → 左键点敌人/自己/障碍物 | 出牌 |
| 没选槽位时点敌人 / 按 3 | 普通攻击（按 3 自动打最近的敌人） |
| 1 / 2 | 抽攻击牌 / 抽防御牌；出现 3 张待选后 1/2/3 灌注、0 放弃 |
| 空格 | 结束回合（走完整 buff 结算 → 敌方行动） |
| G / F1 / F2 / R | 补满资源 / 操作说明 / 开关面板 / 战后重开 |

面板用 IMGUI 叠加，不依赖 Canvas 与 TextMeshPro：回合阶段、血/护盾/耐力/思绪、5 个槽位、
每个敌人的血量与本回合预告、按处境给的操作提示、胜负结算。

### 四、美术资源（Assets/Game/Art/）

按飞书「美术任务」表的「成品」字段下载并归类 **96 张**（表内 100 条记录，7 条成品为空）：

| 目录 | 内容 |
| --- | --- |
| `Characters/主角` | 主角立牌 + 缩放版 |
| `Characters/敌人` | 25 只敌对角色的立牌 |
| `Characters/立绘` | 主角 / 克勒妮玛塔 / 林德沃姆 立绘（jpg） |
| `Obstacles` | 柱子、尖刺柱子、炸药桶、大炸药桶、一桶鼠鼠、石堆、折翼 |
| `Potions` | 11 瓶饮料，与 `PotionData` 资产同名 |
| `Relics` | 22 件思维碎片，与 `RelicData` 资产同名 |
| `UI` | 血量/耐力/思绪槽、空技能卡面、技能插槽、抽牌按键、位移/攻击选取格、开始界面元件 |
| `Backgrounds` | 两张战斗背景 |

导入设置统一为 Sprite / Single，**角色与障碍物 `spritePixelsToUnits = 1400`**（原图 2340px 画布 → 1.67 世界单位，
恰好填满 1.73 高的格子，且保留美术彼此的比例），其余（UI/药水/遗物/背景）保持 180，`maxTextureSize` 提到 4096。

4 张 RGB 白底图（旱魁、滑板鞋、精力充沛、肌肉强化）做了**边界泛洪挖白**：只清除与图像边缘连通的近白像素，
角色内部的白色不受影响，否则进游戏会是一块白色矩形。

### 五、美术接线（资产与预制体）

| 对象 | 结果 |
| --- | --- |
| `UnitData.icon` | 24 / 28（仲裁人、别西卜、大天使、路西法表内无成品） |
| `ObstacleData.icon` + `prefab` | 9 / 10（祝福器无成品） |
| `PotionData.icon` | 11 / 11 |
| `RelicData.icon` | 22 / 22 |
| `玩家.asset` | icon + `Player_Template.prefab` 换精灵，用 `主角.png` |

**新增 26 个预制体**（25 个单位 + 2 个障碍物，减掉已有的）：原先只有 7 个怪 + 1 个玩家模板，
其余单位没有 prefab，`LevelManager` 会直接跳过、图配了也显示不出来。按 `Enemy_神庙鼠.prefab` 克隆，
只改 `m_Name` / `m_Sprite` / `data` 三行。现在 28 个单位、9 个障碍物都能被关卡摆出来。

### 六、场景与其他

- `Scenes/Battle.unity` —— 修复 `ProjectileSpawner` 的 `speed / minDuration / maxDuration` 缩进错误
  （原文件这三个字段比父级多一级，是非法 YAML）；
- `.gitignore` —— 补上美术表导出的临时文件 `_art.ndjson` / `_art.manifest.json`；
- `ProjectSettings/EditorBuildSettings.asset` —— 场景构建顺序调整为 `Battle.unity` 放在**索引 0** 并参与构建，
  `SampleScene`（0 个脚本的空场景）保留但设为 `enabled: 0`。打包或直接 Build 现在直接进战斗场景，
  不再先加载空场景；`BattleDebugDriver` 的「R 重开」走 `LoadScene(active.buildIndex)`，也因此拿到了合法索引。

### 七、还没接的（下一版）

- `CardData` 没有 `icon` 字段，`BuffData.icon` 也没有图 → 71 张技能卡与 23 个 buff 目前没有图标；
- 22 件遗物、11 瓶药水只有数据与图，`RelicController` / `LootSystem` 还没挂进场景，运行时拿不到；
- `Battle.unity` 的牌库还是 37 张旧 GUID 列表，34 张重建后的卡没进池。

## 小羊：战斗界面改造（背景 / 高亮贴图 / 战斗 HUD）

新增

- `Battle/BattleHUD.cs` —— 战斗界面 UI（挂 `Battle.unity` 的 Managers 上，全部运行时生成，无预制体）。
  按飞书《游戏系统》例图布局：左上角红圈血量 + 耐力/思绪小字；左下角 攻击/位移/技能/休息 四个按钮；
  右下角"下一回合"按钮（进入选择状态自动变"取消"）。
  **攻击 / 位移做成两个常驻可选技能**：点击进入瞄准模式，可交互格显示 `UI/攻击选取格.png`（红圈）/ `UI/位移选取格.png`（绿圈），
  左键点格子执行（位移走寻路扣耐力、攻击限相邻 1 格），右键/Esc 取消。
  "技能"按钮 = 抽卡流程 UI：牌堆选择（攻击/防御，5 思绪）→ 3 张待选卡 → 点击灌注，与 `PlayerCardSystem` 的
  `BeginDraw / ConfirmInfuse / AbandonInfuse` 对接。选择技能或卡牌期间所有小怪图像半透明化（例图要求）。

修改

- `Core/HexCell.cs` —— 战斗高亮从"格子染色"改为"贴图覆盖层"：
  新增 `moveHighlightSprite` / `attackHighlightSprite` 两个字段（已配在 `ConceptMapCell.prefab` 上），
  `SetMoveRangeHighlight / SetAttackRangeHighlight / SetTargetHighlight / SetAimHighlight` 改为在格子正上方
  （排序 5，格子之上、棋子之下）懒创建子物体 `HighlightOverlay` 显示对应选取格图形，自动缩放到与格子贴图同大；
  有目标格 = 红圈加深、瞄准格 = 红圈染黄。未配置贴图时退回旧染色方案。
- `Units/PlayerUnit.cs` —— 新增 `GetMoveRangeForUI()`（耐力 + 敏捷/残废修正后的移动距离上限，供 HUD 高亮用）。
- `Prefabs/Board/ConceptMap.prefab` —— 战斗背景替换：根物体的旧 SpriteRenderer（ConceptArena.png）移除，
  改为子物体 `Background` 挂 `Backgrounds/蓝黑色战斗背景.png`，旋转 -90° 转横版（17.8×13.3 世界单位），
  **排序 -100 = 全界面最低**；格子 / 高亮 / 棋子 / 相机全部不受影响。
- `Prefabs/Board/ConceptMapCell.prefab` —— 格子预制体补上两张选取格贴图引用。
- `Scenes/Battle.unity` —— Managers 物体追加 `BattleHUD` 组件。

## 小羊：战斗界面第二轮（技能插槽 / 抽牌流程 / 推跳 / 敌方技能栏 / 悬停信息）

新增

- `Battle/BattleHUD.cs`（大幅扩充）—— 按例图重排战斗界面，全部运行时生成：
  - **底部三张技能插槽**：低一层铺 `UI/空技能卡面.png`（空白区填技能名黑字 + 剩余次数），
    上面叠 `UI/技能插槽.png` 横条；整张卡面可点 → 释放该槽技能，点击进入瞄准后点格子结算。
  - **抽牌按钮**（`UI/抽牌按键.png`，在"下一回合"正下方）：点击先弹攻击 / 防御两个大型牌堆按钮（中轴对称），
    再全屏遮罩上出三张居中卡牌，选中即可灌注。
  - **左侧敌方技能栏**：竖排三张空技能卡面，显示"上回合怪物准备释放并已执行的技能"；
    上下各一个 `UI/淡蓝色箭头_1.png` 翻页箭头（下箭头旋转 180°，悬停换 `淡蓝色箭头_2.png`），
    怪物多于 3 个时可翻页；点卡面弹"是否花费思绪灌注"确认框（槽满提示 / 取消无事）；第 1 回合为"空"。
  - **悬停敌人信息**：鼠标停在怪物立绘上时，脚下显示红色血条（含"当前/上限"数字），
    头顶显示本回合准备释放的技能；瞄准 / 抽牌 / 敌方回合 / 弹窗期间自动隐藏。浮层整体比其他 UI 大 1.2 倍便于阅读。
  - **右下角按钮组**：下一回合按钮换 `UI/淡蓝色按键_1.png`，悬停 / 按下换 `淡蓝色按键_2.png`
    （SpriteSwap 过渡）；按钮右缘距屏幕右边界 10px，插槽组右缘再往左 20px，全部锚定右下角。
  - **顶部药水栏**：深蓝长条中轴右侧 420px 起，横排三只 `UI/空饮料槽位.png`（0.7 倍大小，间隔 5px），
    作为药水槽占位；引用存在 `potionSlotImages[3]`，接药水系统时替换贴图与数量即可。

修改

- `Cards/PlayerCardSystem.cs` —— 新增 `InfuseResult` 枚举与 `TryInfuseCardDirectly`（左侧敌方技能一键灌注）、
  `UseBasicAttack` / `UseBasicDefense`（推 / 跳不占槽直接结算，照常扣耐力，共用原有六道校验）。
- `Core/CameraController.cs` —— 新增 `startZoom`（默认 1.2）：开局按倍数拉近视野，
  背景 / 格子 / 棋子 / 障碍等比放大，格线与画稿的对齐不会错位；滚轮缩放仍可用。
- `Battle/LevelManager.cs` —— 编辑器下牌库为空时按目录自动装载 `ScriptableObjects/技能/{攻击技能,防御技能}`，
  避免 Battle.unity 没配牌库导致抽卡链路失效。
- `Debug/BattleDebugDriver.cs` —— 删除屏幕左侧 QWERT 技能弹窗、右下角敌人信息面板与技能槽位弹窗（已由 HUD 接管）。
- `ScriptableObjects/技能/攻击技能/推.asset` —— `targetTypes` 补 `伤害/击退`（原来是空的，选不中目标）；
  `ScriptableObjects/技能/防御技能/跳.asset` —— `defenseTargetTypes` 补 `传送`。
- `Art/UI/*.png` —— `技能插槽 / 抽牌按键 / 空技能卡面 / 淡蓝色箭头_1,2 / 淡蓝色按键_1,2` 按按钮实际可点击区域裁剪，
  去掉四周大片透明边（guid 不变，引用无需重配）。
- `Scenes/Battle.unity` —— BattleHUD 补齐上述素材引用，相机 `startZoom` 序列化为 1.2。

## 小羊：药水/遗物接入 HUD + 技能介绍 + 抽卡卡面化 + 战斗结算奖励图标

新增

- `Battle/BattleHUD.cs`（扩充）：
  - **顶部药水栏启用**：三个饮料槽改成可点按钮，持有药水时换成药水立绘（`PotionData.icon`，
    最多 3 瓶，`PlayerUnit.maxPotions=3`）；鼠标停在瓶子上浮层显示药水名 + 效果说明 + "点击使用"，
    点击直接 `UsePotion`（非玩家行动阶段 / 本场已喝完会弹提示），喝完自动换回空瓶素材；
    跟随 `PotionChanged` 事件刷新。
  - **遗物栏**：血量槽红圆右缘 +10px 起横排遗物图标（64×64，间隔 5px，`preserveAspect`），
    跟随 `RelicChanged` 事件刷新，跨关由 `PlayerRunState` 回放后自动补上。
  - **技能介绍浮层**：鼠标停在底部三个技能槽卡面（浮层在卡面上方）或左侧怪物技能卡（浮层在卡面右侧）
    时，显示【卡名】+ 思绪/耐力消耗 + `CardData.description`；浮层不挡点击。
  - **抽卡三张卡改成空技能卡面**：底图用 `UI/空技能卡面.png`（320×418 等比），浅灰文字区写
    卡名 + 思绪/耐力消耗 + 技能介绍（自动换行，放不下截断），点击整张卡灌注。

修改

- `Battle/BattleEndPanel.cs` —— 结算奖励按钮左侧加美术图标：技能选择用空卡面小图、
  药水用 `PotionData.icon`、思维碎片用候选遗物图标（三件候选在面板打开时一次抽好，点开与图标一致）；
  文字整体右移让位，`preserveAspect` 防拉伸。
- `Art/Potions/*.png`、`Art/Relics/*.png`（33 张）—— 原地裁掉四周透明边（guid 不变），
  药水立绘裁剪后与空饮料槽素材等大，放进槽位不会显得偏小。

## 小羊：飞书美术任务表新素材归类入库（2026-10-19）

从多维表格「美术任务」表比对 `Assets/Game/Art`，补入 11 张已交付但尚未入库的素材：

- `Art/Characters/敌人/`：别西卜、大天使、仲裁人、路西法（1920×1080 原稿裁内容后 ×2 贴上 2048×2340 标准棋子画布，与现有棋子同画布同量级）
- `Art/Obstacles/`：狂躁抑制器、行动抑制器（同上画布约定）
- `Art/UI/`：技能.png / 攻击.png（"两个牌堆"的绿/红两本书）、攻击牌.png / 防御牌.png（600×800 空牌面模板，两张内容完全相同）
- `Art/Backgrounds/`：开始界面背景.jpg（1920×1080 "拾忆"主视觉）

说明：`.meta` 由 Unity 下次打开工程时自动生成；未入库的还有 希尔芙立绘 / 漫摄徽章（表中尚无成品）、"普兰 黛亚"（已完成但无成品附件）。

## 小羊：卡面分类换装 + 悬停 buff 说明浮层（2026-10-19）

用户手动补齐美术资源后，把各处卡面统一改为按技能类型切换攻击牌/防御牌：

素材

- `Art/UI/攻击牌.png`、`Art/UI/防御牌.png` —— 原地裁掉四周透明边：600×800 → 379×495，
  与 `空技能卡面.png` 完全同尺寸（红角标=攻击 / 绿角标=防御），所有既有卡面文字锚点直接复用；
  600×800 原图备份在本地 `补丁/art_backup/`（不提交）。

修改

- `Battle/BattleHUD.cs`：
  - 新增 `attackCardFaceSprite` / `defenseCardFaceSprite` 引用（编辑器下按 Art/UI 路径兜底加载；
    两张图尚无 .meta，场景引用等 Unity 生成 guid 后再配）。
  - **卡面按类型切换**：底部三张技能槽卡面（空槽保持"空"卡面）、左侧敌方技能三张卡面、
    抽卡三张待选卡，全部按 `CardData.IsAttack` 换成攻击牌/防御牌（随灌注/滚动实时刷新）。
  - **推 / 跳 卡面化**：左下角两个灰色按钮改成对应种类技能卡面（推=攻击牌、跳=防御牌，
    150×196 与左侧怪物卡同宽），黑字卡名，点击进瞄准模式不变。
  - **buff 说明浮层**：悬停任意卡牌时，若 `CardData.buffs` 非空，技能介绍浮层旁再弹一个
    360 宽浮层逐条列出【buff 名】+ 描述；无 buff 自动隐藏；屏幕右侧放不下自动改贴左侧。
    覆盖底部插槽卡 / 左侧怪物卡 / 推·跳 / 抽卡待选卡（待选卡浮层在卡面右侧）。
- `Battle/BattleEndPanel.cs`：
  - 结算"选择 1 个技能加入卡池"的三张卡同样按类型换攻击牌/防御牌卡面；
  - 新增悬停浮层（技能介绍 + 附带 buff 说明，挂在结算 Canvas 上盖住遮罩，
    卡面右侧优先、放不下改左侧），浮层不挡卡牌点击。

## 小羊：新建开始界面（2026-10-20）

程序启动后的第一个界面（新场景 `Scenes/Start.unity`，已加入 Build Settings 首位）：

新增

- `Scenes/Start.unity` —— 最小场景：Main Camera（深色底）+ StartMenu 物体（挂 StartMenuUI）。
- `Core/StartMenuUI.cs` —— 开始界面逻辑，全部 UI 运行时生成：
  - 全屏背景：`Backgrounds/开始界面背景.jpg`；
  - 左下角一列四个菜单按钮（`开始界面UI_1~4`，从上到下），左缘距屏幕左 50px、
    纵向间隔 20px、最下面一个底边距屏幕下 20px，显示高 180px（宽按比例 ≈257 不拉伸）；
  - UI_1（开始游戏）：悬停/按下换成 `开始界面UI_5`（SpriteSwap），点击 `LoadScene("Battle")`；
  - UI_2~4 暂为纯显示，后续接 继续/设置/退出 等功能。
- `ProjectSettings/EditorBuildSettings.asset` —— Start.unity 置于场景列表第一位（enabled），
  Battle.unity 第二位，程序启动即进开始界面。

素材处理

- `Art/UI/开始界面UI_1~5.png`：原图 3600×2400 带透明边，五张统一裁到并集 bbox
  （2244×1570，悬停换图与纵向堆叠完全对位）；原图备份在本地 `补丁/art_backup/`（不提交）。

## 小羊：事件界面模版 EventPanel（2026-10-20）

游戏内多种事件共用同一份版式，新建 `Battle/EventPanel.cs`（+ .meta，未接入场景，运行时 `EventPanel.Show()` 打开）：

- 顶部：与战斗 / 战斗结束界面相同的深蓝色长条（105 高，随之前 ×0.7 调整）。
- 屏幕正中：`一个UI框_2` 顺时针旋转 90°、放大 1.5 倍（战斗结束框 550×760 → 转置 → 1140×825）；
  素材本身是"黑边 + 灰底"，按"内部全部涂黑、仅保留最外一圈白色边框"用两层色块实现——
  白底铺满框体、黑色内层四周内缩 20px 露出白圈。
- UI 框最左侧、紧贴框上侧：一个 `淡蓝色按键_1` 按钮（100 高、宽按 1247×308 比例 ≈405，
  悬停/按下换 淡蓝色按键_2，行为留 TODO 由具体事件接）。
- UI 框内右下角：三个 `耐力槽_2`（300×52 贴合素材 400×69 比例），最右槽右缘距框右 50px、
  距框下 10px、间隔 5px。
- 模版预留 `EventContent` 挂点（黑框内留白边的空白区），具体事件往里填内容；`Close()` 关闭。
  Canvas sortingOrder=550：盖住战斗 HUD（500）、让位战斗结束界面（600）。

## 小羊：事件「实验开始」（2026-10-20）

读取飞书「游戏事件」表（tblrDPCwXMak8Jxn）中 record recvycXnhEb4Ip，给 EventPanel 模版接入具体事件：

- `Battle/EventPanel.cs` 扩展：`Show(名称, 描述, 选项[], 点击回调[])`：
  - 事件名称写进 UI 框上方的淡蓝色按键（黑字 30 号）；按钮改为底边贴框顶、整体悬在框上方，
    避免与框内描述文本重叠。
  - 描述文本写在框内：左缘对齐三槽位最左侧（框左起 180px）、顶部距框上侧 20px、
    30 号黑字自动换行。
  - 选项 1~3 放入右下角三个耐力槽（最右槽右缘距框右 50、距下 10、间隔 5、选项 1 在左）；
    槽宽按选项文本自适应（26 号字 + 48px 留白，钳制 180~420），选项不满三个不创建空槽。
  - 空模版 `Show()` 行为保留（三固定槽占位）。
- `Battle/EventPanel_ExperimentStart.cs`（新建）：本事件入口 `EventPanel_ExperimentStart.Show()`，
  名称「实验开始」、描述按表原文、仅 1 个选项「迎接第一场战斗」（点击暂为关闭界面，战斗接入留 TODO）。

## 小羊：事件命名规范化 + 事件测试面板（2026-10-20）

- **命名约定**：具体事件文件/类名固定为 `EventPanel_xxx`，xxx = 飞书「游戏事件」表「名称」字段原文
  （支持中文类名）。`EventPanel_ExperimentStart.cs` 已重命名为 `EventPanel_实验开始.cs`
  （类 `EventPanel_实验开始`，.meta 同步改名、guid 不变）。
- **`Debug/EventDebugDriver.cs`（新建，调试用）**：事件测试面板，IMGUI 自举（场景存在 LevelManager
  时自动创建，不进场景 YAML）。屏幕右上角面板列出 EVENTS 注册表里的全部事件，点「打开」即调
  `EventPanel_实验开始.Show()` 查看实际效果；底部按钮「关闭当前事件界面」= `EventPanel.Close()`；
  F7 开关面板。新接事件只需在 `EVENTS` 数组登记一行。

## 小羊：开始界面 → 实验开始 → 战斗 主流程接通（2026-10-20）

- `Core/StartMenuUI.cs`：「开始游戏」（UI_1）点击改为 `EventPanel_实验开始.Show()` 打开开场事件，
  不再直接进战斗场景。
- `Battle/EventPanel_实验开始.cs`：选项 1「迎接第一场战斗」点击 → 关闭事件界面 +
  `LoadScene("Battle")` 进入战斗（原 TODO 落地）。
- 删除上一轮的调试文件 `Debug/EventDebugDriver.cs`（事件测试面板，含 .meta），改由真实流程验证。

## 小羊：事件界面重新设计（2026-10-20）

`Battle/EventPanel.cs` 版式重做：

- 顶部：与战斗界面相同的深蓝长条（105 高），其上保留三个空饮料槽位（0.7 倍、中轴右 420、
  间隔 5）与左上角血量槽红圆（132×132、距边 24）；耐力/思绪槽不再绘制。
- 深蓝长条以下整屏（1920×975）：`一个UI框_1` 拉伸铺满（原图 1800×2400 带大透明边，
  已原地裁到内容区 1182×1632，备份在本地 `补丁/art_backup/`）；底图 raycastTarget=true，
  挡住下层战斗 HUD 的耐力/思绪槽。
- 屏幕正中生成纯黑长方形（1140×825）作为事件内容区（替代旧版"白圈+黑底"）。
- 黑框最左侧、底边贴框顶：淡蓝色按键_1（黑字写事件名称，悬停换 _2）。
- 黑框内右下角三个 耐力槽_2：距右 50 / 距下 10 / 间隔 5；选项槽宽按文本自适应、
  不满三个不建空槽（沿用）。
- 描述文本改黑底白字（旧版黑字在黑底上不可见，一并修复）。

### 事件界面微调（同日）
- 中央黑框高度 ×0.6：825 → 495（宽度不变，仍屏幕居中）。
- 三个选项槽改为**竖直排列从上到下**（选项 1 在最上）：右缘距黑框右 20px、最下槽距框下 10px、
  间隔 5px；槽宽仍按选项文本自适应，选项不满三个时从下往上依次不建空槽。

## 小羊：全部游戏事件按模版创建（2026-10-20）

读取飞书「游戏事件」表（tblrDPCwXMak8Jxn）全部 14 条记录（其中 4 条空行），有名称的事件共 10 个，
按 EventPanel 模版逐个创建（`实验开始` 此前已建，本次新增 9 个），文件均在 `Battle/` 下：

| 文件 | 选项数 | record |
|---|---|---|
| EventPanel_奇怪的药剂.cs | 2 | reczz28NeaP01KHq |
| EventPanel_垃圾清理.cs | 3 | reczz28NeaP00i0a |
| EventPanel_重启.cs | 2 | reczz28NeaP01JJz |
| EventPanel_次元饮料.cs | 2 | reczz28NeaP02PxG |
| EventPanel_温泉.cs | 3 | reczz28NeaP01zle |
| EventPanel_小助手.cs | 3 | recvycWTrtG0PZ |
| EventPanel_洗礼.cs | 2 | recvycXwrqOteb |
| EventPanel_抉择.cs | 3 | reczz28OlzQDUx4p |
| EventPanel_自助售卖机.cs | 2 | reczz28OmZGJIdtN |

- 每个文件 = 静态类 + `Show()`，把表里的名称/描述/选项 1~3 原文传入 `EventPanel.Show(...)`；
  「选修3」字段作为第 3 个选项（表里为空的事件自然只有 1~2 个槽）。
- 选项点击回调统一为 `EventPanel.Close()`，TODO 注释标明后续要接的效果（获得药水/遗物、
  属性增减、额外选卡等）——待对应系统（药水/遗物发放、PlayerRunState 属性接口）就绪后替换。
- 「自助售卖机」的"拍一拍连拍/选项变化"机制写在文件头策划备注里，实现时需重开面板刷新选项。
- 各文件 .meta 已生成（guid 唯一）。事件触发点未接（除实验开始 → 开始界面），后续按流程挂载。

## 小羊：耐力 / 思绪不足改为弹窗提示（2026-10-20）

原先"耐力不足""思绪不足"只在 Console 打日志，玩家看不到。现在统一在 UI 层预判并弹窗：

- **释放技能耐力不足** → 弹窗「耐力不足，无法释放技能」
  - `BattleHUD.EnterSlotMode`：点技能插槽时先按 `CardData.GetFinalActionCost(玩家)` 判定，
    不足则不进入瞄准模式（含"防御卡只对自己"的直接释放分支）
  - `BattleHUD.EnterMode`：推 / 跳进入瞄准前同样按 `pushCard` / `jumpCard` 的消耗判定
  - `BattleHUD.ExecuteSkillCell` 与 `ExecuteCell` 的推/跳分支：执行前再查一次（兜底瞄准期间被扣）
- **抽取 / 灌注思绪不足** → 弹窗「思绪不足，无法灌注当前技能」
  - `BattleHUD.DrawFromDeck`：点牌堆按钮先看 `player.CurrentThought < drawThoughtCost`，不足则关牌堆面板 + 弹窗（不再走 `BeginDraw` 只打 Console）
  - `BattleHUD.TryInfuse`：点抽卡待选卡前看 `card.thoughtCost`，不足则弹窗并留在选牌界面
  - `OnConfirmInfuseClicked` 的 `InfuseResult.思绪不足` 文案统一为「思绪不足，无法灌注当前技能」
- 弹窗复用已有的 `MessagePanel`（BuildMessagePanel 最后创建，层级压在所有面板之上）。

## 小羊：事件界面遗物栏 + 物品名特殊显示（2026-10-20）

`Battle/EventPanel.cs` 两项：

1. **遗物栏**（深蓝色长条内、生命槽红圆右侧 10px）
   - 数据源：优先 `PlayerRunState.Relics`（本局已获得的遗物），其次场上 `PlayerUnit.Relics`
   - 图标 64×64、preserveAspect、间隔 5px，y 取生命槽圆心（-90）
   - 鼠标悬停弹浮层：遗物名（金黄）+ 效果描述，跟随鼠标、自动避让屏幕边缘
   - 提供 `EventPanel.RefreshRelics()` 静态入口：事件选项发放遗物后调用即可立即刷新
   - 战斗结算界面领取的遗物会写进 `PlayerRunState.Relics`，下次打开事件界面自动显示
2. **选项里的遗物 / 药水名特殊显示**
   - 选项文字改为**分段渲染**：普通文字黑字；能匹配到遗物/药水资产名的片段显示为
     `<u>[名称]</u>` + 金黄 + 黑色细描边（浅灰槽底上保证可读），鼠标悬停弹该物品的效果浮层
   - 名称表由编辑器扫描 `ScriptableObjects/遗物`（RelicData）与 `ScriptableObjects/药水`（PotionData）
     得到，按名称长度降序匹配；对飞书笔误注册别名（`次元抹除 → 次元抹杀`）
   - 选项槽宽度估算改用"加 [] 后的显示文本"，避免长名被截
   - 名称文字会拦射线，已补 PointerClick 转发到选项按钮（点名字也能选中该选项）

## 小羊：事件界面两项对齐调整 + 自助售卖机三档连拍（2026-10-20）

### 1. 遗物栏与药水槽对齐

- `Battle/EventPanel.cs` 的 `BuildRelicRow()`：遗物图标的 **y 从"生命槽圆心"(-90) 改到"药水槽中心"(-52.5)**，
  横向仍从生命槽红圆右缘 +10px 起（x = 24+132+10）。现在新获得的遗物图标与深蓝长条右侧空饮料槽同高。

### 2. 事件选项槽统一尺寸 + 文本左端对齐

- 新增常量 `slotFixedW = 840f`（所有事件统一的槽宽）、`slotTextPad = 20f`（文本左边距）。
- `BuildOptions()` 里 `ws[i] = slotFixedW`，**不再按选项字数自适应**；占位空槽同样用固定宽度。
- `BuildRichOptionLabel()` 的起始 x 改为 `-slotFixedW/2 + slotTextPad`，于是所有选项的
  第一行文字左端都落在同一个竖直位置上（一条直线），不再因为槽宽不同而错开。

### 3. `Battle/EventPanel_自助售卖机.cs` 重写为三档状态机

按飞书 record `reczz28OmZGJIdtN` 的「补充描述」实现连拍：

| 第几次拍 | 最大耐力上限 | 获得遗物 | 拍完后的界面 |
|---|---|---|---|
| 1 | -3 | 药水腰带 | 选项文案变成 5 点 / 草席卷 |
| 2 | -5 | 草席卷 | 选项文案变成 7 点 / 黑鹅项圈 |
| 3 | -7 | 黑鹅项圈 | 事件结束，正常推进流程 |

- 选项 2「捡个饮料——获得1瓶随机饮料」在任意一次拍之前都能点：拿 1 瓶随机饮料后**直接离开事件**。
- 换档不结束事件：用新增的 `EventPanel.CloseWithoutCompleting()`（只销毁界面、不调
  `RunFlowController.CompleteEvent()`）关掉旧面板，立刻按下一档文案重新 `Show()`。
  为此 `CloseCurrent()` 补了 `SetActive(false)`——`Destroy` 要到帧末才生效，同帧重开时
  旧面板会和新的叠一帧（两层黑框闪一下）。
- 静态字段 `已拍次数` 记录本局进度，`Show()` 时归零。

### 4. 新增 `Progression/EventItemLibrary.cs`（事件按名字找物品）

事件文案来自飞书表，只有中文名（"获得 药水腰带"）。这个工具类把名字换成真正的资产：

- `FindRelic(名)` / `FindPotion(名)` / `RandomPotion()`；
- 数据来源 = 编辑器 `AssetDatabase` 扫 `t:RelicData` / `t:PotionData`，再并上
  `LootSystem` 的 3 个遗物池 + 2 个药水池（去重）——打包后没有 AssetDatabase 也能退回掉落池；
- `CreatePlaceholderRelic(名, 描述)`：**资产还没建时的占位遗物**（一次性、只在内存里），
  让事件不至于白扣属性；真资产建到 `ScriptableObjects/遗物/` 下并保持同名即自动接管。

> ⚠️ 目前工程里 **没有** `药水腰带 / 草席卷 / 黑鹅项圈` 这三件遗物资产（22 件遗物里都不含它们），
> 所以现在走的是占位遗物：图标空缺（金色方块）、悬停显示"效果待策划补充"。
> 需要策划给出这三件的效果后建资产。

### 5. `Battle/PlayerRunState.cs` 增加"事件改动玩家"的入口

事件是在上一场战斗的场景里弹的，下一关会重新生成玩家并走 `ApplyTo` 回放快照，
**所以事件改完玩家必须立刻 `Capture()` 一次，否则改动活不过场景重载**。新增：

- `ApplyMaxStaminaDelta(玩家, delta)` / `ApplyMaxThoughtDelta(玩家, delta)`：改上限 + 立刻刷新快照；
  没有场上玩家（开局的"实验开始"）时改快照值 / 待回放增量；
- `GrantRelic(玩家, 遗物)`：有玩家就当场获得（永久 buff 立刻生效），再刷新快照；
- `GrantPotion(玩家, 药水)`：返回是否拿到（背包上限 3 瓶），再刷新快照。

### 6. `EventPanel.RegisterItemName(名, 描述)`

占位遗物不在资产表里，选项文案里的名字本来不会变黄加下划线。现在事件可以运行时补登物品名，
效果与真实资产一致（黄字 + `[]` + 下划线 + 悬停弹效果浮层）。

## 小羊：按飞书「思维碎片（遗物）大全」补齐 6 件遗物（2026-10-20）

读取飞书表 `tblCUdqSZFR8MpzF`（29 条记录，其中 1 条空行 → 28 件遗物），
与工程里已有的 22 件逐个比对，**缺 6 件**，本次全部补上（含逻辑与事件适配）：

| 遗物 | 类型 | 效果（表内原文） | 新资产 guid（…后 2 位） | 掉落池 |
|---|---|---|---|---|
| 焦黑十字架 | 事件掉落 | 关卡开始时，对自身施加神圣 | `…17` | 事件专属（洗礼） |
| 黑鹅项圈 | 事件掉落 | 回合开始额外回 7 耐力 + 7 思绪，但每回合最多行动 5 次 | `…18` | 事件专属（自助售卖机） |
| 残缺护符 | 精英怪掉落 | 下次从卡牌获得的护盾翻倍，2 回合冷却 | `…19` | 精英池 |
| 药水腰带 | 事件掉落 | 药水携带上限 +2 | `…1a` | 事件专属（自助售卖机） |
| 草席卷 | 事件掉落 | 在休息处额外恢复 1 点生命值 | `…1b` | 事件专属（自助售卖机） |
| 灵甲 | BOSS 掉落 | 回合结束若没有护盾，则获得 3 点护盾 | `…1c` | BOSS 池 |

- 资产在 `Assets/Game/ScriptableObjects/遗物/`，模板与老遗物完全一致（`RelicData`）。
  guid 沿用项目里遗物专用的 `200000000000000000000000000000XX` 序列（老资产到 `…16`，新资产 `…17~…1c`）。
- **活动专属的 4 件（十字架/项圈/腰带/席卷）没有进 `LootSystem.generalPool`**：
  它们只从对应事件获得，避免随机掉落 + 事件重复给、导致事件白扣属性。
  残缺护符 → `elitePool`，灵甲 → `bossPool`（Battle.unity 已加）。

### `RelicEffectType` 追加 5 个成员（一律追加在末尾，不能插队）

`药水携带上限加成=22` / `行动次数上限=23` / `护盾获取倍率=24` / `获得护盾=25` / `休息恢复加成=26`；
`RelicCondition` 追加 `持有者没有护盾=4`（灵甲用）；`RelicEffect` 追加 `cooldownTurns`（残缺护符 = 2）。

### 各条逻辑落在哪

| 遗物 | 实现位置 | 做法 |
|---|---|---|
| 焦黑十字架 | `RelicData.ApplyEffect` 的 `施加Buff` 分支（已有） | `触发时机=关卡开始` + `scope=自身` + `buff=Buff/神圣`。关卡开始由 `GameManager.StartLevel` 广播，`LevelManager` 先 `PlayerRunState.ApplyTo` 回放遗物再开战，顺序正确 |
| 黑鹅项圈 +7/+7 | `回复耐力` / `回复思绪` + `触发时机=回合开始` | 由 `RelicController` 在 `PlayerTurnStarted` 驱动 |
| 黑鹅项圈 行动次数 | `PlayerUnit`：`actionsThisTurn` / `MaxActionsPerTurn` / `CheckActionBudget()` | 计数点 = `TrySpendStamina`（出牌 / 推 / 跳）+ `TryMoveTo` + `TryAttack`；药水、抽卡、灌注都不经过这里，正好对上"这些不算行动"。超额时广播 `PlayerActionLimitReached`，`BattleHUD` 监听后弹窗「本回合行动次数已用完」 |
| 残缺护符 | `PlayerUnit.AddShield` 重写 + `relicCooldowns` 字典 | 获得护盾时若冷却为 0 则 ×2 并置冷却 2；冷却在 `OnTurnStart` 每回合 -1。**注意：所有护盾来源（药水/遗物/卡牌）都会触发**，策划原文只写"从卡牌中获取" |
| 药水腰带 | `PlayerUnit.ApplyRelicPassiveAttributes` | `AddRelic` / `RestoreOwnedRelic` 时 `maxPotions += 2`；`BattleHUD` 的饮料槽改成可增长（`EnsurePotionSlotCount`），上限变 5 就补出 5 个槽，否则第 4、5 瓶看不到 |
| 草席卷 | `RunFlowController.ChooseRest` | 任何休息选项结束后按 `GetTotalValue(休息恢复加成)` 额外回血 |
| 灵甲 | `RelicData.ApplyEffect` 新增 `获得护盾` 分支 + `ConditionPasses()` | `触发时机=回合结束` + `条件=持有者没有护盾` + `value=3`。护盾在"回合开始"才清，所以这 3 点盾正好挡敌人回合 |

### 事件适配

- **洗礼**（`EventPanel_洗礼.cs`）：选项 1 → 当前生命 -2（最少留 1）+ 获得「焦黑十字架」+ 刷新长条遗物栏；
  选项 2 → 回复 10% 最大生命。两个分支都走 `PlayerRunState` 的新入口并立刻 `Capture()`。
- **自助售卖机**：三档连拍原本发的是"占位遗物"，现在三件资产齐了，`EventItemLibrary.FindRelic` 直接命中，
  自动变成真遗物（耐力上限 -3/-5/-7，遗物栏与悬停说明都会正常显示）。占位逻辑保留作兜底。
- `PlayerRunState` 新增 `ApplyHealthDelta()` / `HealPercent()`（事件改血量后立刻刷新快照）。

### 还缺的

- 这 6 件遗物**没有美术图标**（`Assets/Game/Art/Relics/` 里只有老 22 张），`icon` 留空：
  战斗 HUD 与事件长条会画浅色方块占位，悬停仍能看到名称与效果。美术出了图直接拖到 `icon` 即可。

## 小羊：修复悬停介绍浮层"进了瞄准模式就不消失"（2026-10-20）

### 现象

战斗界面鼠标停在技能卡上会弹出介绍浮层；点进技能瞄准（选范围）后浮层留在屏幕上不消失，
必须重新把鼠标移到那张卡上刷新一次、再移开才会关闭。

### 根因

UGUI 的 `PointerExit` 只在**指针真的移出**某个物体时由 `EventSystem` 派发。这些情况下收不到：

1. **物体被 `SetActive(false)`**——进瞄准 / 打牌堆 / 选卡时，`RefreshHud()` 会把左下角
   `skillGroup` 与左侧 `enemySkillGroup` 整体隐藏（这正是"选范围时浮层不消失"的主因），
   失活不会补发 PointerExit；
2. **物体被销毁重建**——出牌后卡面 / 待选卡重建，销毁同样不补发 PointerExit；
3. 指针瞬间移出窗口、或被后来居上的全屏层盖住。

`HideSkillTooltip()` 原来只挂在 PointerExit 上，所以上面三种情况一发生，浮层就"卡住"了。

### 修法（`Battle/BattleHUD.cs`）

- 新增 `tooltipHoverOwner`：`AddHoverTrigger` 在 PointerEnter 时记下来源按钮，PointerExit 时清掉
  （`AddHoverTrigger` 顺带从 `static` 改成实例方法，因为要写实例字段）。
- 新增 `GuardTooltips()`，在 `Update()` 最前面每帧调用（浮层没显示时直接 return，零开销）：
  - 来源已隐藏 / 已销毁 → 立刻关；
  - 指针已不在来源按钮矩形内（`RectTransformUtility.RectangleContainsScreenPoint`）→ 立刻关。
- 新增 `HideAllTooltips()`（关技能浮层 + buff 浮层 + 药水浮层，并清来源）。
- `RefreshHud()` 里隐藏 `skillGroup` / `enemySkillGroup` 的那一刻显式 `HideAllTooltips()`，
  不等兜底判定，同帧就收掉。
- `ShowMessage()` / `ShowInfuseConfirm()` 打开模态框时也先 `HideAllTooltips()`，
  避免"提示框 + 技能浮层"两个框叠着。

## 小羊：修复战斗结算选卡后介绍浮层残留（2026-10-20）

### 现象

战斗结束界面点"进行 1 次技能选择"→ 悬停三张候选卡会弹技能介绍 + buff 说明；
**选中一张后（按钮变成"技能已加入卡池"），两个浮层仍留在屏幕上不走**。

### 根因（与战斗界面那次同一类）

`CloseSubPanel()` 里 `Destroy(subPanel)` 把整个子选择层连同被悬停的卡面一起销毁，
而 **UGUI 在物体被销毁时不会补发 PointerExit**，挂在 PointerExit 上的 `HideCardTooltip()`
永远收不到调用，浮层就留在 Canvas 上。

### 修法（`Battle/BattleEndPanel.cs`）

- `CloseSubPanel()` 里**先 `HideCardTooltip()` 再 `Destroy(subPanel)`**（选卡完成、点"返回奖励列表"都走这条路）。
- 新增 `tooltipHoverOwner` + `AddHoverTrigger` 改为实例方法记录悬停来源；
  `HideCardTooltip()` 同时清空来源。
- 新增 `GuardCardTooltips()` 并在 `Update()` 每帧调用：浮层可见时检查
  ①来源是否还在（被销毁/隐藏 → 关）、②指针是否还在来源矩形内（`RectangleContainsScreenPoint`），
  任一不满足立刻关。这样即便以后又出现"浮层随卡片一起被销毁"的路径，也不会再残留。

## 小羊：事件里物品名去掉下划线（2026-10-20）

需求：特殊文本只要"黄色 + `[名称]`"，不要下划线；悬停仍显示对应物品介绍。

- `Battle/EventPanel.cs` · `BuildRichOptionLabel()`：物品名从 `<u>[名]</u>` 改为纯文本 `[名]`，
  颜色（金黄 `(1, 0.78, 0.16)`）、黑描边、悬停浮层（`ShowItemTip` / `HideItemTip`）、
  点击转发给选项按钮全部保留。
- 相关注释同步更新（不再提"下划线"）。

## 小羊：事件选项里物品名的黄色文本改单条富文本 + 透明热区（2026-10-20）

### 两个现象

1. 鼠标停在黄色的 `[次元抹除]` 上不弹物品介绍；
2. 黄字和整句的黑字对不齐（看起来偏大、偏高）。

### 根因

原来是"每个文字段一个 Text，横向拼起来，宽度靠 `preferredWidth` 累加"：

- `NewSegmentText()` 里 `sizeDelta = (0, 40)`，**rect 宽度是 0**，文字是靠 `HorizontalWrapMode.Overflow`
  画出去的。而 UGUI 的射线检测 `Graphic.Raycast` 是**按 rect 判定**的，0 宽的 rect 永远命中不了 →
  `EventTrigger` 的 PointerEnter 根本不会触发，悬停自然没反应（点击转发同理失效）。
- 黄字段额外挂了 `Outline` 描边、颜色也不同，几个独立 Text 各自居中，视觉上会跟黑字差 1~2px，
  叠上描边就显得"又大又高"。

### 修法（`Battle/EventPanel.cs` · `BuildRichOptionLabel`）

- **整句只用一个 Text 渲染**，物品名用富文本标黄：`<color=#D99A00>[名称]</color>`。
  字形、字号、基线天然一致，不会再错位（`Outline` 描边一并去掉，改用深一点的金黄保证浅灰底上的对比度，
  颜色常量是 `ItemColorHex`，想换色调只改这一个字符串）。
- 物品名上再叠一块**全透明 Image 热区**（`color.a = 0`，`raycastTarget = true`）接 PointerEnter/Exit；
  热区位置用同一个 Text 量前缀宽度得到（`t.preferredWidth` 量 `plain[0..start]` 与 `plain[0..end]` 之差），
  与排版用的是同一套字宽，避免错位。热区同时转发点击给选项槽按钮。
- 删掉不再使用的 `BuildDisplayText()` / `EstimateTextWidth()`。

## 小羊：事件物品名悬停全覆盖 + 战斗长条遗物栏对齐 + 技能释放逻辑审计（2026-10-20）

### 1. 事件里所有「特殊文本」都能弹效果说明

原来物品名表只在 `#if UNITY_EDITOR` 里用 `AssetDatabase` 建，出包后整张表是空的
（名字不标黄、也弹不出说明）。现在改成统一走 `EventItemLibrary`：

- 编辑器：扫工程内全部 `t:RelicData` / `t:PotionData`；
- 打包：退回 `LootSystem` 的三遗物池 + 两药水池（去重）；
- 描述为空时兜底「效果待策划补充」，不会弹出空白框；
- `EventPanel.RebuildItemTable()` 在每次 `Awake` 调一次：策划新加 / 改名遗物药水后
  不用退出播放模式就生效。运行时补登的名字存在 `extraItems` 里，表重建后自动补回
  （否则"先登记、后面板才 Awake"的顺序会把占位遗物名冲掉）。

逐条核对过现有 10 个事件的选项文案，物品名全部命中：
`解压垃圾 / 垃圾毁灭者 / X药 / 次元抹除（别名→次元抹杀）/ 焦黑十字架 / 回归本质`，
自助售卖机的 `药水腰带 / 草席卷 / 黑鹅项圈` 由事件自己预登记。

### 2. 战斗界面长条里的遗物栏

- `BattleHUD.BuildRelicRow()` 的 y 从 `-90`（血槽圆心）改 `-52.5`（**空饮料槽中心**），
  横排仍从生命槽右缘 +10 起；图标 pivot 改 `(0, 0.5)`，整排左缘贴位不再偏半个图标。
- **悬停终于能触发了**：`MakeImage` 默认 `raycastTarget = false`（装饰图不挡格子点击），
  遗物图标是"要接 PointerEnter 的装饰图"，必须单独打开射线，否则永远收不到悬停。
- 新增 `relicTooltip`（360 宽，挂在长条下沿之外），`ShowRelicTooltip(i)` 显示
  `【遗物名】+ 效果`；已并入 `HideAllTooltips()` 与每帧兜底的 `GuardTooltips()`，
  进瞄准 / 弹模态框 / 图标隐藏时都会立刻收掉。
- 悬停回调只在**建图标时挂一次**（刷新只改图和位置）：原来每次刷新都往同一个
  `EventTrigger` 里塞一对 entry，刷新几次就会叠几个浮层。
- `EventPanel.RefreshRelicIcons()` 同步改成"按序号实时取遗物"，避免闭包抓到旧资产。

### 3. 技能释放逻辑审计：4 处已修

| 问题 | 影响 | 修法 |
|---|---|---|
| 悬停预检用 `GetFinalActionCost`，实际扣费是 `GetPlayActionCost` / `DefenseState.GetStaminaCost` | 「连续拳」折扣后、 「自动屏障」首用 -3 时，付得起却被判"耐力不足"，进不了瞄准 | `BattleHUD` 新增 `StaminaCostOf()`，四处预检（推/跳/槽位/瞄准兜底）统一用它 |
| 免费技能入口不判选格规则 | 药水发的范围 / 地块 / 全场技能会被当成单体，只结算一个目标 | `PlayerCardSystem.UseFreeCard()` 遇到 `attackTargetRule != 单位` 直接拒绝并打日志 |
| 「愤怒之拳」25% 保留次数无条件判定 | 血量健康时也白送使用次数，强于策划文案 | 追加 `lowHealth` 前置条件（与 `+2` 伤害同一判断） |
| 自身类防御卡一律"点插槽直接释放" | 「神圣祝福」自身带神圣时必须选一只小天使，拿空选择去放 100% 失败 | 快捷释放的判据改成 `DefenseSkillResolver.CanResolve(card, player, 空)` |

### 4. 审计发现但**没动**的地方（需要策划/你拍板）

| 问题 | 位置 | 说明 |
|---|---|---|
| 敌人远程技能不查视线 | `EnemyUnit.ExecutePlannedSkills` | 玩家远程要过 `IsLineBlocked` + 烟雾格，敌人只查射程与 `CanSelectAttackCell` → 能隔柱子 / 隔着烟雾打玩家 |
| 结算失败退耐力但不退行动次数 | `TryUseDefenseCardOnCells` / `UseBasicDefense` | `TrySpendStamina` 已经给 `actionsThisTurn` +1，失败只 `RestoreStamina`；带黑鹅项圈（5 次/回合）时一次失败也吃掉一次额度 |
| 范围技能 0 命中仍扣耐力与次数 | `UseSlotCardOnCells` | 地块 / 穿透类合理（空格也是目标）；`自身范围 / 全场` 在周围没目标时也照扣 |
| 攻击卡的「传送」位移分支被跳过 | `CardData.ResolveWithOutcome` | `causesDisplacement && displacementType != 传送`；当前 0 张攻击卡用传送，属潜在坑 |
| 青柠饮料救不回"已用完腾空"的技能 | `PotionData.RestoreSkillUses` | 只恢复 `slot` 里还在的卡；次数耗尽时 `slot.card = null`，那张牌已经不在槽位里了 |
| 部分 `devNotes` 已过时 | 技能资产 | 狙杀 / 飞身刺击 / 愤怒之拳 的 devNotes 仍写"未实现"，实际逻辑早已接上 |

> 另外核对过：42 张攻击卡的 `targetTypes`（`00000000` = 一个元素「敌方单位」）、
> `defenseTargetTypes`（`01000000` = 「自身」）解出来的都是预期值，不是空列表；
> 起始牌组的 2 张固定技能就是「推」「跳」，正好对应左下角两个常驻按钮，槽位 0/1 不参与三槽 UI 是对的。

## 小羊：敌人视线对齐 / 失败退行动次数 / 空放拦截 / 失败原因弹窗（2026-10-20）

### 1. 敌人远程技能也要过视线（`Units/EnemyUnit.cs`）

新增 `HasLineOfSight(card, target)`，规则与玩家侧 `PlayerCardSystem.IsTargetReachable` **完全一致**：

- 只有带 `远程` 标签且没勾"无视遮挡"的技能才受限制；
- 施法者与目标之间有活着的单位或挡视线的障碍物 → 看不见；
- 目标站在烟雾格 → 锁不住。

`ExecutePlannedSkills` 在"射程够 + 可选中"之后补一道视线校验，被挡住就 `continue` 跳过本回合这个技能
（不打日志式的"绕路"——`ApproachTarget` 在射程内会立刻返回，走不动）。
这样敌人不再能隔柱子 / 隔着别的单位 / 隔着烟雾打玩家。

### 2. 结算失败要退"行动次数"，不只是退耐力

`TrySpendStamina` 在扣耐力的同时会让 `actionsThisTurn +1`。原来 `DefenseSkillResolver.Resolve` 返回 false 时
只调 `RestoreStamina`，所以带「黑鹅项圈」（每回合最多 5 次行动）时，一次失败的释放会白吃一次额度。

- `PlayerUnit` 新增 **`RefundAction(int stamina)`**：退耐力 + `actionsThisTurn--`，并广播 `PlayerResourceChanged`；
- `TryUseDefenseCardOnCells` / `UseBasicDefense` 的结算失败分支改调 `RefundAction`。

### 3. 「自身范围 / 全场 / 全场随机」空放不再扣耐力与次数（`Cards/PlayerCardSystem.cs`）

新增 `AreaHasAnyTarget(card, area)`：范围里必须有**施法者以外的活着的单位**（或这张卡能打障碍物时的障碍物）才算有效。
在 `UseSlotCardOnCells` 里、**扣费之前**拦截，没有目标就返回"范围内没有任何目标"。

- 不适用于 `地块 / 穿透 / 固定地图图案`：它们打的是"格子"本身，空放是有效操作（礼炮轰炸可以砸空地）。
- 注意 `自身范围` 的范围本身包含施法者自己的格子，所以判定里必须把施法者排掉，否则永远通过。
- 典型例子：「回旋斩击」0 伤害、只给相邻目标挂 2 层流血 —— 身边没人时以前会白扣耐力和次数。

### 4. 技能释放失败 → 弹窗说明原因

新增 `Cards/SkillFailure.cs`（静态小信箱：`Set / Clear / Consume`）：

- `PlayerCardSystem.Fail(原因)` 接管了原来所有"`Debug.Log` + `return false`"的校验点：
  阶段、槽位空、次数用完、禁止行动、射程、视线、烟雾、亲密、目标类型、选格规则……
- `PlayerUnit` 的耐力不足 / 行动次数上限也写入原因；
- `DefenseSkillResolver.CanResolve` 新增 `LastFailureReason`（步行步数、换位条件、神圣祝福、投掷织毯未配地形…）。

`BattleHUD` 新增 `RunCardAction(action, fallback)`：先 `SkillFailure.Clear()`，失败时 `Consume()` 一次取走原因，
用现有 `ShowMessage` 模态框弹出来（没有具体原因才退回通用提示）。全部出牌入口都改走它：

| 入口 | 覆盖场景 |
|---|---|
| 槽位卡点插槽直接释放 | 自身类防御卡 |
| 瞄准点格 → 防御卡 | 治疗/护盾/位移的落点不合法 |
| 瞄准点格 → 单体攻击 | 射程/视线/烟雾/亲密/次数 |
| 瞄准点格 → 多目标、选格类、全场类 | 同上 + 空放拦截 |
| 左下角 推 / 跳 | 落点不合法、目标不合法 |

顺带把几处"点错格子"的静默提示也改成弹窗：点空白处放单攻、跳的落点不可站立、推的目标格没有敌人。

---

## 事件选项效果接通（2026-10-20）

此前除「洗礼」「自助售卖机」外，其余事件的选项回调都是 `TODO` 空壳——**选了也不发物品、不改数值**。
本次把「游戏事件」表里所有选项效果全部接上。

### 新增 `Battle/EventEffects.cs`

事件选项效果的统一入口（静态工具类），避免 7 个事件各写一遍重复逻辑：

| 方法 | 作用 |
|---|---|
| `GrantRelic(名称)` | 按中文名找 `RelicData` 并发给玩家，成功后刷新长条遗物栏；找不到资产 / 已持有返回 false 并打日志 |
| `GrantPotion(名称)` | 按名发一瓶药水（背包满返回 false） |
| `GrantRandomPotions(n)` | 随机发 n 瓶，返回实际到手瓶数（背包满即停） |
| `HealthDelta(n)` / `HealPercent(r)` | 当前生命增减（最少留 1 点）/ 按最大生命比例回血 |
| `MaxHealthDelta(n)` / `MaxStaminaDelta(n)` / `MaxThoughtDelta(n)` | 三项上限增减 |
| `QueueExtraCardChoices(n)` | 记下"额外 N 次技能选择"，下一次战斗结算界面消费 |

所有方法都走 `PlayerRunState` 的事件入口（内部 `Capture` 快照），改动才能带进下一关。

### 各事件接通情况

| 事件 | 选项效果 |
|---|---|
| 垃圾清理 | ①获得遗物「解压垃圾」 ②获得遗物「垃圾毁灭者」 ③回复 20% 最大生命 |
| 奇怪的药剂 | ①获得遗物「X药」 ②获得 3 瓶随机饮料 |
| 重启 | ①额外 2 次技能选择（见下） ②获得遗物「回归本质」 |
| 次元饮料 | ①当前生命 -2 + 获得遗物「次元抹杀」（文案写作"次元抹除"） ②最大思绪 +10 |
| 小助手 | ①最大思绪 +10 ②最大耐力 +10 ③回复 20% 最大生命 |
| 温泉 | ①回复 20% 最大生命 ②当前生命 -3、最大生命 +2 ③当前生命 -6、最大生命 +4 |
| 抉择 | ①最大生命 -2 + 耐力/思绪上限各 +5 ②最大耐力 -5、当前生命 +2 ③最大思绪 -5、当前生命 +2 |
| 洗礼 | （原有）扣血 2 换遗物「焦黑十字架」 / 回复 10% 最大生命 |
| 自助售卖机 | （原有）三档连拍换三件遗物 / 捡 1 瓶随机饮料 |

### 「额外进行 2 次新技能选择」的实现

事件里没有卡牌界面，所以分两步：

1. `PlayerRunState.PendingExtraCardChoices` 记下待进行的次数（`QueueExtraCardChoices`，战斗失败 `Reset` 时清零）；
2. `BattleEndPanel.BuildOptions()` 发现该值 > 0 时，多出一个「额外的记忆：进行 1 次技能选择（剩 N 次）」
   按钮——复用现有三选一子层，每选 1 次减 1，`BuildOptions()` 重建选项列，次数用完按钮自动消失。

### `PlayerRunState` 新增

- `ApplyMaxHealthDelta(player, delta)`：「温泉」「抉择」改最大生命上限；增加上限不补当前血，
  减少上限时当前血压到新上限（不出现 30/28 这种数值）。

---

## 跨关继承核查 + 事件逻辑修复（2026-10-20）

### 一、血量 / 耐力上限 / 思绪上限确实是整局继承的

调用链（不需要改架构，只补了坑）：

| 时机 | 代码 | 作用 |
|---|---|---|
| 每场战斗结束点"跳过奖励" | `BattleEndPanel.NextLevel` → `RunFlowController.ContinueAfterRewards` → `PlayerRunState.Capture(player)` | 把**当前血量**、最大生命、耐力/思绪上限、每回合回复量、牌池、遗物、药水写进静态快照 |
| 休息处三选一 | `RunFlowController.ChooseRest` → 改数值 → `Capture` | 同上 |
| 事件改数值（发遗物/扣血/加上限） | `EventEffects.*` → `PlayerRunState.*`（内部 `Capture`） | 事件发生在**上一场战斗的场景**里，玩家对象还活着，必须改完立刻存，否则活不过场景切换 |
| 下一关生成玩家 | `LevelManager.SpawnPlayer` → `Player.Initialize`（先按 `UnitData` 回满）→ `PlayerRunState.ApplyTo(Player)` | 用快照覆盖血量与三项上限 |

所以：**血量是打完上一场剩下的值，不会每关回满；耐力/思绪上限是累计后的值**。
每场开局只有"当前耐力/思绪"会重置回 `startStamina / startThought`（20），这是策划文档规定的。

`savedAttack/savedDefense` 只在真的存到过牌池时才回放（`hasPoolSnapshot`），
避免"没卡系统时的空列表"把开局牌库清空。

### 二、本轮修掉的四个坑

| 问题 | 后果 | 修复 |
|---|---|---|
| `PlayerRunState.Capture` 在玩家没有 `PlayerCardSystem` 时整段 `return` | 血量/上限/遗物/药水全部**静默不保存**，表现就是"下一关血量被重置" | 卡池只是快照的一部分：血量和物品照存，只有牌池需要卡系统 |
| `PlayerRunState.Reset()` 不清 `Health/MaxHealth/MaxStamina/MaxThought/回复量` | 上一局数值残留，事件在没有场上玩家时会直接改写这些脏值 | `Reset` 一并清零 |
| 事件下调上限（自助售卖机 -3/-5/-7、抉择 -5）不夹当前值 | HUD 出现"20/8"，而且本场真的还能按 20 点花（`TrySpendStamina` 只比当前值） | 新增 `PlayerUnit.ClampResourcesToMax()`，改上限后 + `ApplyTo` 回放后各夹一次 |
| `ApplyTo` 无快照分支写 `if (BonusStamina > 0)` | 负数增量（开局前减上限）被静默吞掉 | 改判 `!= 0` 并夹到 ≥1 / 回复量 ≥0 |

顺手把 `LevelManager` 的布阵日志与 `ApplyTo` 的回放日志补全（血量 x/y、耐力上限、思绪上限、遗物/药水件数），
进 Play 后看 Console 就能确认跨关继承有没有生效。

### 三、事件选择逻辑核查结果

九个事件的**选项文案与飞书「游戏事件」表逐条一致**（含 `选修3` 这一列的三选项事件），
效果全部接通。核查中发现并修掉：

- **「自助售卖机」拍一拍**：这件遗物可能已经先从战斗结算的"思维碎片"拿到过（见下），
  以前照样扣 3/5/7 点最大耐力上限 → 白扣。现在先判 `已持有`，已持有就跳过这一档、不扣上限。
- **「次元饮料」选项 1**：遗物已持有时以前仍然扣 2 点血 → 纯亏选项。现在扣血跟着发放结果走。

核查中确认的**设计层缺口（未改，等策划拍板）**：

1. 5 件事件遗物（X药 / 回归本质 / 垃圾毁灭者 / 次元抹杀 / 解压垃圾）`pool = 通用`，
   而 `LootSystem.RollRelicCandidates` 每次都会把 `generalPool` 混进去 —— 所以它们在战斗结算的
   三选一里就能抽到，"事件专属"并不专属。已经被抽走后，对应事件选项会**空转**（垃圾清理 / 重启）。
2. 「奇怪的药剂」选项 2 承诺 3 瓶随机饮料，但药水背包上限是 3 —— 事件多发生在战斗后（背包常有存货），
   实际可能一瓶也塞不进去，目前只有 Console 警告。

---

## 克勒妮·玛塔对话（2026-10-21，合并队友 BossDialoguePanel 后）

队友 PR 已实现通用首领对话系统（`Story/BossDialoguePanel.cs` + `Story/BossDialogueData.cs`），
布局与需求 PSD《对话界面_1》一致：全屏白底、顶部深蓝长条 105 + 血量槽 132（不含耐力/思绪槽）、
底部 一个UI框_1（71%）、左上 淡蓝色按键_1（82%）写说话人、左右立绘底贴屏幕、打字机 + 跳过按钮；
素材走 `Resources/BattleUIAssetCatalog`（打包可用），三段式对话（第一次遇见 / 第二次及以后 / 胜利），
"第一次"用 PlayerPrefs 记录，之后自动播"第二次及以后"。

因此本轮自建的 `DialogPanel_克` 独立面板已删除（功能重复），改为给这套系统补配置资产：

**新增 `Assets/Game/Resources/BossDialogues/克勒妮·玛塔.asset`**（+meta，guid 38731a3b…）：

- `unitName = 克勒妮·玛塔`——与 `ScriptableObjects/单位/克勒妮·玛塔.asset` 的 unitName 完全一致，
  `LevelData.Spawn.unit` 按这个名字匹配，关卡 `Level_1_克勒妮玛塔精英`（已在 RunRoute 精英池）即触发；
- `leftName = 勒忒` / 左立绘 `主角_立绘.png`；`rightName = 克勒妮·玛塔` / 右立绘 `立绘/克勒妮玛塔.jpg`，
  双方显示高 960、底贴屏幕（与 PSD 1255/1417 ≈ 88.6% 屏高一致）；
- 台词全部取自飞书《对话文档》克勒妮·玛塔章节：第一次遇见 11 句、第二次及以后 6 句、胜利 2 句，
  名牌显示「克勒妮·玛塔」/「勒忒」（2026-10-21 应需求由缩写「克」改为全名，
  `rightName` 与台词 speaker 同步改，保证高亮判定依然命中）。

### 触发链（无需改代码）

| 时机 | 代码 |
|---|---|
| 布阵完成后开战前 | `LevelManager` → `BossDialoguePanel.TryPlayBeforeBattle(level, StartBattleNow)`，播完回调才真正开战 |
| 击败该单位后、开结算前 | `BattleEndPanel.HandleUnitDied` 记下 `defeatedDialogueUnit` → `OpenAfterDialogue` 播 victory → 回调开结算 |

### 给其他角色加对话

复制 `Resources/BossDialogues/` 下任一 `.asset`（Create → Game → 剧情 → 首领对话），
改 `unitName` / 台词 / 立绘即可，运行时 `Resources.LoadAll` 自动扫描，不用写任何代码。

---

## 黛亚&普兰对话（2026-10-21）

**新增 `Assets/Game/Resources/BossDialogues/黛亚&普兰.asset`**（+meta，guid abad45f9…），
完全复用队友的 `BossDialoguePanel` 系统，零代码改动：

- `unitName = 黛亚`——`Level_1_黛亚&普兰BOSS` 的 spawns 里有黛亚（大抱抱、变色龙无对话配置不受影响）；
  普兰不在任何关卡 spawns 里（战斗内由黛亚携带登场），所以匹配点选黛亚；
- `leftName = 勒忒` / 左立绘 `主角_立绘.png`；`rightName = 黛亚` / 右立绘 `立绘/普兰 黛亚.jpg`
  （两人合照：黛亚与普兰同框，一张立绘同时代表两人）；
- 台词取自飞书《对话文档》「黛亚&普兰」章节，缩写按需求替换：
  「黛」→「黛亚」、「普」→「普兰」，主角名牌统一为「勒忒」（与林德沃姆那份一致）：
  第一次遇见 13 句、第二次及以后 8 句、胜利 3 句；
- 高亮规则沿用队友实现：speaker 等于 `leftName`/`rightName` 时另一方压暗；
  普兰的台词（舞台指示，如"（轻微地摇晃）"）两边名字都匹配不上 → 双方保持原色，符合合照同框的表现。
