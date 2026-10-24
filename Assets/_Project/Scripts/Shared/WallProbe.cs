using UnityEngine;

// 【阶段4-C】贴墙探测：从某点朝"正前 / 右前 45° / 左前 45°"三条短射线，返回最贴近墙的程度（0~1）
//
// 抽取动机（这是本次重构唯一"消重复"的地方）：
//   第一人称从相机探测（WeaponController.UpdateWallLower）、第三人称从手部骨骼探测（ThirdPersonWeapon.UpdateWallAmount），
//   两边原本**各写了一套一模一样的三向射线 + "命中角色不算墙"判定**。
//   抽到一处后：以后调贴墙手感（起始距离 / 完全贴住距离 / 方向组合）只有这一个地方要改，不会只改到一半。
//
// 用法：
//   第一人称：WallProbe.Probe(相机位置, 相机前, 相机右, wallCheckDistance, 0)
//             —— 等价于原来的 Clamp01(1 - 距离 / wallCheckDistance)
//   第三人称：WallProbe.Probe(手部骨骼位置, 角色前, 角色右, wallCheckDistance, fullLowerDistance)
//             —— 等价于原来的 Mathf.InverseLerp(wallCheckDistance, fullLowerDistance, 距离)
public static class WallProbe
{
    // origin: 探测起点（第一人称=相机，第三人称=手部骨骼）
    // forward / right: 水平朝向与右方向（用来推 ±45°）
    // startDistance: 这个距离以外算"没贴墙"（返回 0）
    // fullDistance: 缩到这个距离算"完全贴上"（返回 1）；传 0 就是"越近越接近 1"
    public static float Probe(Vector3 origin, Vector3 forward, Vector3 right,
                              float startDistance, float fullDistance)
    {
        float front = Cast(origin, forward, startDistance, fullDistance);
        float rightFront = Cast(origin, (forward + right).normalized, startDistance, fullDistance);
        float leftFront = Cast(origin, (forward - right).normalized, startDistance, fullDistance);
        return Mathf.Max(front, Mathf.Max(rightFront, leftFront));   // 哪个方向更贴近用哪个
    }

    // 单条射线：命中墙返回贴近程度；没东西 / 命中的是角色 → 0
    private static float Cast(Vector3 origin, Vector3 direction, float startDistance, float fullDistance)
    {
        // 触发器一律 Ignore：角色身上的部位碰撞体勾了 Is Trigger，不能让它被当成"墙"
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, startDistance, ~0, QueryTriggerInteraction.Ignore))
            return 0f;
        if (hit.collider.GetComponentInParent<Health>() != null)
            return 0f;    // 命中的是角色（自己或敌人，含其移动胶囊 / 部位）→ 不算墙

        return Mathf.InverseLerp(startDistance, fullDistance, hit.distance);   // 越近越接近 1
    }
}
