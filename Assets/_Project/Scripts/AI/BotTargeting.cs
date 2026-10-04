using UnityEngine;

// ============ Bot 的"眼睛"：找最近的敌人（步骤1.4）============
// 敌我判定：TeamId 不一样、还活着、在视野范围内 → 候选敌人，取最近的那个
// 性能考虑：不是每帧全场景搜索，而是每 scanInterval 秒扫一次，结果缓存起来
// 挂载：挂在 Bot 预制体根物体上
[RequireComponent(typeof(Health))]
public class BotTargeting : MonoBehaviour
{
    [SerializeField] private float scanInterval = 0.3f;   // 多久重新扫一次全场（秒）
    [SerializeField] private float sightRange = 45f;      // 视野距离：这个范围内才会被锁定

    private Health selfHealth;      // 自己的 Health（拿 TeamId 做敌我判断）
    private float nextScanTime;     // 下一次允许扫描的时间点
    private Health currentTarget;   // 缓存的目标（可能随时死亡/跑远，用之前要再验一次）

    public Health CurrentTarget => currentTarget;   // 只读出口，给 BotController 用

    private void Awake()
    {
        selfHealth = GetComponent<Health>();   // 就在同一个物体上，直接拿
    }

    // 扫描全场找最近的敌人（内部节流：scanInterval 秒才真正扫一次）
    public void Scan()
    {
        if (Time.time < nextScanTime) return;      // 还没到下一次扫描时间 → 继续用缓存
        nextScanTime = Time.time + scanInterval;

        Health nearest = null;                     // 目前找到的最近敌人
        float nearestDistance = float.MaxValue;    // 目前最近的距离

        Health[] allHealths = FindObjectsOfType<Health>();   // 找出场景里所有 Health
        for (int i = 0; i < allHealths.Length; i++)
        {
            Health other = allHealths[i];
            if (other == selfHealth) continue;                 // 跳过自己
            if (other.IsDead) continue;                        // 跳过尸体
            if (other.TeamId == selfHealth.TeamId) continue;   // 跳过队友（同队打不掉血）

            // 在视野范围内、又比当前最近的还近 → 换它当目标
            float distance = Vector3.Distance(transform.position, other.transform.position);
            if (distance <= sightRange && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = other;
            }
        }

        currentTarget = nearest;   // 扫到就锁定，没扫到就清空
    }

    // 目标还值不值得追：存在、没死、还没跑出视野范围
    public bool IsTargetValid()
    {
        if (currentTarget == null) return false;
        if (currentTarget.IsDead) return false;
        if (Vector3.Distance(transform.position, currentTarget.transform.position) > sightRange) return false;
        return true;
    }

    // 清空目标（回巡逻 / 复活时调用）
    public void ClearTarget()
    {
        currentTarget = null;
    }
}