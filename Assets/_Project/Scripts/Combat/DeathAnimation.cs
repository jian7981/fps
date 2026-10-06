using UnityEngine;   // MonoBehaviour / Animator

// ============ 死亡 / 复活动画触发器 ============
// 订阅 Health.OnDied：把 Animator 的 isDead 参数置 true → 状态机切到 Death 状态播死亡动画
// 订阅 Health.OnRespawned：置回 false → 从倒地姿势过渡回正常动作
// 玩家和 Bot 通用（挂法完全相同）
// 挂载：挂在角色根物体上（有 Health 的那个）；Player.prefab 和 Bot.prefab 都要挂
[RequireComponent(typeof(Health))]   // 没 Health 会提前提示，防止挂错物体
public class DeathAnimation : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Animator animator;                // 留空 = 自动往子物体找（和 PlayerMotor 一样的拿法）
    [SerializeField] private string deadParameter = "isDead";  // Animator 参数名，要和 playerA.controller 里一致

    private Health health;   // 自己的血量组件（死亡/复活事件从这里来）

    private void Awake()
    {
        health = GetComponent<Health>();                                        // 根物体上的 Health
        if (animator == null) animator = GetComponentInChildren<Animator>();    // 模型上的 Animator
    }

    private void OnEnable()
    {
        if (health == null) return;
        health.OnDied += HandleDied;            // 订阅：死亡
        health.OnRespawned += HandleRespawned;  // 订阅：复活
    }

    private void OnDisable()
    {
        if (health == null) return;
        health.OnDied -= HandleDied;            // 配对取消订阅
        health.OnRespawned -= HandleRespawned;
    }

    // 死亡：标记"已死亡"（参数保持 true → 尸体一直保持倒地，不会被打断）
    // 参数 killer 是击杀者，这里用不到，但要和事件签名对齐
    private void HandleDied(GameObject killer)
    {
        if (animator == null) return;
        animator.SetBool(deadParameter, true);
    }

    // 复活：清掉标记 → 状态机从 Death 状态过渡回移动/站立
    private void HandleRespawned()
    {
        if (animator == null) return;
        animator.SetBool(deadParameter, false);
    }
}