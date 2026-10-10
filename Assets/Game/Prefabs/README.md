# 战斗预制体

这些预制体使用临时图形，位于 `Board/`、`Units/`、`Obstacles/`。需要重新生成时，可在 Unity 菜单选择 **Game > Generate Battle Prefabs**。

| 目录 | 预制体 | 用途 |
| --- | --- | --- |
| Board | `HexCell` | 六边形格子，含 SpriteRenderer、PolygonCollider2D 和 HexCell；由 BattleGrid 实例化 |
| Board | `BattleGrid` | 六边形地图管理器，已引用 HexCell，默认半径 4、平顶六边形 |
| Board | `ConceptMap` | 按 `ConceptArena.png` 画稿制作的战场，进入游戏时生成 63 个可交互格子 |
| Board | `ConceptMapCell` | 透明格子，仅在高亮时显色，避免遮挡原画 |
| Units | `Player_Template` | 玩家模板，含 PlayerUnit、SpriteRenderer 和 CircleCollider2D；尚无玩家 UnitData |
| Units | `Enemy_*`（7 个） | 每种敌人一个，已引用对应 UnitData |
| Obstacles | `Obstacle_*`（7 个） | 每种障碍物一个，已引用对应 ObstacleData |

## 使用前仍需连接的逻辑

1. 场景放入 `BattleGrid`，调用 `HexGrid.GenerateGrid()` 创建格子。
2. 实例化玩家、敌人、障碍物后，分别调用 `Initialize(data, coord)`，完成血量和格子占位初始化；玩家模板还需提供玩家 UnitData。
3. 把玩家输入和回合事件连接到现有方法。当前预制体不负责生成单位、处理点击或自动开始敌人行动。

`UnitData.prefab`、`ObstacleData.prefab` 和缺省图标已经指向生成的预制体与临时图形。替换正式美术时可直接修改 SpriteRenderer 和数据资产中的 icon。

## 画稿地图

打开 `Assets/Game/Scenes/ConceptMap.unity` 即可在 Scene/Game 视图看到画稿。运行场景时，`ConceptMap` 会按蓝色边框生成 63 个透明的 `HexCell`，供寻路、占格与高亮使用。画稿源文件位于 `Assets/Game/Art/Maps/ConceptArena.png`。可在 Unity 菜单 **Game > Build Concept Map** 重新生成场景与地图预制体。

地图目前只包含画稿、格子和相机；玩家、敌人、障碍物的摆放和回合输入需另行接入。
