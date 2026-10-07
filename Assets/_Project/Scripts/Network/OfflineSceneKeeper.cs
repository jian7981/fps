using UnityEngine;
using Mirror;

// 【阶段2.3 修复】"单机没有相机"的原因与对策：
// Mirror 的编辑器钩子（Assets/Mirror/Editor/NetworkScenePostProcess.cs）会在进入 Play 时，
// 把"带 NetworkIdentity 的场景物体"强制失活（等服务器 Spawn 时才重新激活）。
// 纯单机没有服务器 → 没人激活它 → map1 的 GameManager 物体醒不来 → 不生成玩家 → 没有相机。
//
// 本脚本挂在 map1 的 MatchCanvas 上，只做一件事：
// 没有网络会话（纯单机）时，发现 GameManager 物体被失活就立刻拉回来；
// 联机中什么都不做（交给 Mirror 的 Spawn 流程）。
//
// 【重要】为什么用"每帧兜底检查"而不是一次性的 Awake 检查：
// Mirror 的失活时机不确定（实测晚于本组件的 Awake）——一次性检查时它还没被失活，
// 条件不成立，之后失活就再没人管了。改成每帧检查后，无论什么时候被失活都能拉回来。
public class OfflineSceneKeeper : MonoBehaviour
{
    [Tooltip("要保活的场景物体（留空 = 自动找 GameManager，含未激活对象）")]
    [SerializeField] private GameObject targetObject;

    private float nextScan;   // 还没找到 GameManager 时，下次重新搜索的时间（避免每帧全场景查找）

    private void Start()
    {
        // 无条件日志：先确认脚本确实在跑（排查问题时不用猜）
        Debug.Log("[单机保活] OfflineSceneKeeper 已启动（挂在 " + gameObject.name + " 上）");
        CheckAndActivate();
    }

    private void Update()
    {
        // 联机中（Host / Client）：完全不插手，交给 Mirror 服务器 Spawn 时激活
        if (NetworkServer.active || NetworkClient.active) return;

        // 还没拿到 GameManager：每 0.5 秒找一次（找到过之后不再重复全场景搜索）
        if (targetObject == null)
        {
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.5f;
        }

        CheckAndActivate();
    }

    // 检查并保活（找不到就顺手找一次）
    private void CheckAndActivate()
    {
        if (NetworkServer.active || NetworkClient.active) return;   // 联机不插手
        ResolveTarget();
        if (targetObject == null) return;

        if (!targetObject.activeSelf)
        {
            targetObject.SetActive(true);   // 激活瞬间，它身上的 Awake/Start 会正常补跑
            Debug.Log("[单机保活] 已重新激活场景物体：" + targetObject.name);
        }
    }

    // 找到 GameManager 物体（它可能正处于"被失活"状态，必须 includeInactive = true）
    private void ResolveTarget()
    {
        if (targetObject != null) return;
        GameManager gm = FindObjectOfType<GameManager>(true);
        if (gm != null) targetObject = gm.gameObject;
    }
}