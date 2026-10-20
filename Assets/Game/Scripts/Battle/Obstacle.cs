using UnityEngine;

/// <summary>
/// 障碍物基类——地图上的环境物体（柱子、炸药桶、抑制器等）
/// 挂在障碍物 GameObject 上，和 Unit 类似但没有主动行为
/// </summary>
public class Obstacle : MonoBehaviour
{
    [Header("配置数据")]
    [Tooltip("引用 ScriptableObjects/Terrain/ 下的障碍物数据资产")]
    public ObstacleData data;

    [Header("运行时状态")]
    [SerializeField] protected int currentHealth;     // 当前血量
    [SerializeField] protected HexCoord currentCoord; // 当前所在六边形坐标

    /// <summary>
    /// 初始化障碍物
    /// </summary>
    public virtual void Initialize(ObstacleData obstacleData, HexCoord coord)
    {
        data = obstacleData;
        currentHealth = obstacleData.maxHealth;
        currentCoord = coord;

        // 把自己注册到格子上
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(coord);
            if (cell != null)
                cell.obstacle = this;
        }
    }

    /// <summary>
    /// 受到伤害
    /// </summary>
    public virtual void TakeDamage(int amount)
    {
        // 无限血量的障碍物（如柱子）不掉血
        if (data.maxHealth <= -1)
            return;

        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            OnDestroyed();
        }
    }

    /// <summary>
    /// 被摧毁时触发特殊效果
    /// 子类重写实现不同效果（爆炸、生成鼠鼠等）
    /// </summary>
    protected virtual void OnDestroyed()
    {
        // 从格子上移除
        if (HexGrid.Instance != null)
        {
            HexCell cell = HexGrid.Instance.GetCell(currentCoord);
            if (cell != null)
                cell.obstacle = null;
        }

        // 触发事件
        EventManager.Trigger("ObstacleDestroyed", this);

        // 销毁物体
        Destroy(gameObject);
    }

    /// <summary>
    /// 获取当前坐标
    /// </summary>
    public HexCoord GetCoord() => currentCoord;

    /// <summary>
    /// 获取当前血量
    /// </summary>
    public int GetCurrentHealth() => currentHealth;

    /// <summary>
    /// 是否阻挡移动（默认阻挡）
    /// </summary>
    public virtual bool BlocksMovement()
    {
        return true;
    }

    /// <summary>
    /// 是否阻挡视线（默认"高大"障碍物阻挡）
    /// </summary>
    public virtual bool BlocksSight()
    {
        // 如果有"高大"标签就阻挡视线
        return data != null && data.tags.Contains(ObstacleTag.高大);
    }
}
