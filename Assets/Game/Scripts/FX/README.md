# 特效脚本（FX）

这里放"表现层"脚本：只做视觉反馈，不参与伤害/回合结算。所有特效都挂在**单位预制体的根物体**上
（和 `Unit` 同一个物体），由 `Battle/LevelManager.cs` 生成单位时自动补上（组件已存在则不重复加）。

| 目录 | 脚本 | 触发时机 | 表现 | 调参入口 |
| --- | --- | --- | --- | --- |
| `Hit/` | `HitFlash.cs` | 单位受伤（事件 `UnitDamaged`） | 贴图闪红 | 组件上的 `Flash Color / Flash In / Out Duration`，材质见 `Assets/Game/Shader/Materials/DamageFlash/DamageFlash.mat` |
| `Hit/` | `StandeeFlip.cs` | 单次伤害 > 半血且 ≥3 点，按超出点数掷概率 | 像纸立牌一样压成竖线再镜像展开，0.3 秒转 4 周 | 组件上的 `Health Threshold Ratio / Min Damage / Chance Per Extra Point / Duration / Spins` |
| `Buff/` | `BuffRingSweep.cs` | 单位被施加 buff（事件 `UnitBuffApplied`） | 负面＝红环自上而下，正面＝蓝环自下而上，扫过 0.4 秒 | 组件上的 `Debuff/Buff Color / Sweep Duration / Diameter Scale / Same Buff Cooldown` |
| `Projectile/` | `ProjectileSpawner.cs` | 卡牌结算广播 `CardResolved` / `CardResolvedOnObstacle`，攻击卡且 `maxRange > 1` | —— | 组件上的 `Ranged Max Range Threshold / Exclude Card Names / Speed / Apex Ratio / 颜色` |
| `Projectile/` | `ProjectileVFX.cs` | 由生成器调用 | 正方形"粒子源"沿抛物线飞向目标，周围跟着一群灰色方块粒子（Local 空间球体内随机），落地爆散一次 | 组件上的 `Speed / Apex Ratio / Spin Speed / Cluster … / Impact …` |

### 远程攻击（Projectile）

- 判定：**攻击卡且 `maxRange > 1`**（近战不飞）；要排除某张卡，把卡名填进 `Exclude Card Names`
- 玩家和敌人共用 `CardData.Resolve`，所以敌人的远程技能（矢驰 / 抛射…）也会自动飞投射物
- 表现组成：核心方块（可换成 Shader Graph 材质）＋ 跟随的灰色方块粒子（`Simulation Space = Local`，所以是"围着粒子源聚"而不是拖在身后）＋ 命中爆散
- 资源与场景接线：菜单 `Game/特效/投射物：准备资源`（贴图导入设置、粒子材质、把 `ProjectileSpawner` 挂到 Managers）
- 错位处理（方案 C）：伤害照旧瞬时结算，但会调用目标的 `HitFlash.DelayNextFlash(飞行时长)`，让闪红等到方块落地才播
- Shader Graph 约定（核心方块材质）：Shader 名 `Shader Graphs/ProjectileBlock`，属性名 `_Glow`（发光强度，脚本在飞行中驱动它做明暗呼吸）、`_GlowColor`（发光颜色，给描边用）；连好后重跑一次上面的菜单即可自动生成 `ProjectileCore.mat`

## 编辑器工具

放在 `Assets/Game/Editor/FX/`：

- `HitFlashSetup.cs`：菜单 `Game/受击闪红/…`，生成闪红材质并给所有预制体挂 `HitFlash`
- `BuffRingTest.cs`：菜单 `Game/测试 buff 套环`，进 Play 施加正/负面 buff 并检查套环有没有正常生成（也可批处理运行）

## 事件约定

特效全部通过 `Core/EventManager.cs` 的事件总线挂接，不直接改伤害逻辑：

| 事件 | 参数 | 抛出位置 |
| --- | --- | --- |
| `UnitDamaged` | `Unit` | `Units/Unit.cs` 的 `TakeDamage` |
| `UnitDamagedDetail` | `Unit, int 减伤后伤害, int 真正扣血` | 同上的下一行（立牌翻转按它算概率） |
| `UnitBuffApplied` | `Unit, BuffData` | `Units/Unit.cs` 的 `ApplyBuff` |

> 注意：固定持续伤害（中毒/灼烧/流血）走的是 `TakeDamageInternal`，不抛受击事件，
> 所以闪红和立牌翻转都不会响应它——这是沿用原有约定，需要的话再单独接。
