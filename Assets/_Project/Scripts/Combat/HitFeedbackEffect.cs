using UnityEngine;

// 【新功能】命中敌人反馈特效（参考"和平精英"：命中处冒一小团烟）
// 订阅 WeaponController.OnHitTarget（只在本机自己的射击反馈链路上触发）→ 在敌人被命中的位置生成烟雾
// 手感原则：明显但不挡视线 —— 小尺寸、短寿命（默认 0.8 秒）、无碰撞
// 挂载：挂 Camera 上（和 WeaponImpact 同物体）
public class HitFeedbackEffect : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private GameObject hitSmokePrefab;      // 命中敌人的烟雾特效预制体

    [Header("表现")]
    [SerializeField] private float effectLifetime = 0.8f;    // 特效存活秒数（到点自动销毁）
    [SerializeField] private float spawnOffset = 0.05f;      // 从命中点沿表面法线偏一点，避免埋进模型里

    private WeaponController weaponController;

    private void Awake()
    {
        weaponController = GetComponentInParent<WeaponController>();
    }

    private void OnEnable()
    {
        if (weaponController == null) return;
        weaponController.OnHitTarget += SpawnHitSmoke;   // 命中敌人 → 冒烟
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnHitTarget -= SpawnHitSmoke;   // 配对取消订阅
    }

    // 命中敌人：在命中点生成烟雾（法线朝射手一侧，稍微抬出来一点）
    private void SpawnHitSmoke(RaycastHit hit, Health target)
    {
        if (hitSmokePrefab == null) return;

        Vector3 pos = hit.point + hit.normal * spawnOffset;
        GameObject fx = Instantiate(hitSmokePrefab, pos, Quaternion.identity);
        Destroy(fx, effectLifetime);   // 到点自动销毁（不需要额外的清理脚本）
    }
}