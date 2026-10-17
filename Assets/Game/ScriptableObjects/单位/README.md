# 单位数据（UnitData）

**这里只放敌人。** 玩家单位的数据放在同级的 `ScriptableObjects/玩家/` 下。

原因：编辑器菜单 `Game/Generate Battle Prefabs` 会把这个文件夹下的**每一个** UnitData
都当成敌人，生成一个 `Enemy_<名字>` 预制体，并把该 UnitData 的 `prefab` 字段指向它。
玩家数据如果放在这里，会被覆盖成敌人预制体（缺 PlayerUnit 组件），导致游戏里玩家刷不出来。
