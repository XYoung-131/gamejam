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
