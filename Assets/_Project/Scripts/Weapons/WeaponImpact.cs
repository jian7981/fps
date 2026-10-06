using System.Collections.Generic;   // List<>
using UnityEngine;

// ============ 弹孔生成器【新增，1.7d】============
// 订阅 WeaponController.OnHitSurface：在命中点生成一个弹孔贴片
// 生命周期管理：① 单个弹孔 holeLifetime 秒后自动销毁；② 场上最多 maxHoles 个，超了先删最旧的
// 挂载：挂 Camera 上（和 WeaponFeedback 同物体）
public class WeaponImpact : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private GameObject bulletHolePrefab;   // 弹孔贴片预制体（自制，见搭建步骤）

    [Header("生命周期")]
    [SerializeField] private float holeLifetime = 20f;      // 单个弹孔存活几秒
    [SerializeField] private int maxHoles = 40;             // 场上最多同时存在几个（防止无限堆积）

    private WeaponController weaponController;
    private readonly List<GameObject> activeHoles = new List<GameObject>();   // 已生成的弹孔（做数量上限）

    private void Awake()
    {
        weaponController = GetComponentInParent<WeaponController>();
    }

    private void OnEnable()
    {
        if (weaponController == null) return;
        weaponController.OnHitSurface += SpawnBulletHole;   // 命中墙面/地面 → 生成弹孔
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnHitSurface -= SpawnBulletHole;   // 配对取消订阅
    }

    // 命中表面：生成弹孔
    private void SpawnBulletHole(RaycastHit hit)
    {
        if (bulletHolePrefab == null) return;

        // 1. 位置：命中点沿法线"抬"出 0.01 米 —— 防止和墙面完全重叠（z-fighting 闪烁）
        Vector3 pos = hit.point + hit.normal * 0.01f;

        // 2. 朝向：贴片的正面朝向法线方向（材质设了双面，所以即使有偏差也看得见）
        Quaternion rot = Quaternion.LookRotation(hit.normal);

        // 3. 生成 + 随机自转（弹孔看起来不重复）
        GameObject hole = Instantiate(bulletHolePrefab, pos, rot);
        hole.transform.Rotate(0f, 0f, Random.Range(0f, 360f), Space.Self);

        // 4. 记录；超过上限就销毁最旧的一个（先进先出）
        activeHoles.Add(hole);
        while (activeHoles.Count > maxHoles)
        {
            GameObject oldest = activeHoles[0];
            activeHoles.RemoveAt(0);
            if (oldest != null) Destroy(oldest);
        }

        // 5. 生命周期：到时间自动销毁
        Destroy(hole, holeLifetime);
    }
}