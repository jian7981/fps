using UnityEngine;
using Mirror;

// 【阶段2.3 修复】"单机没有相机"的原因与对策：
// Mirror 的编辑器钩子（Assets/Mirror/Editor/NetworkScenePostProcess.cs）会在进入 Play 时，
// 把"带 NetworkIdentity 的场景物体"强制失活（等服务器 Spawn 时才重新激活）。
// 纯单机没有服务器 → 没人激活它 → map1 的 GameManager 物体醒不来 → 不生成玩家 → 没有相机。
//
// 本脚本挂在 map1 的 MatchCanvas 上，做两件事：
// 1) 单机保活：没有网络会话时，发现 GameManager 物体被失活就立刻拉回来（联机中不插手，交给 Mirror 的 Spawn 流程）。
// 2) 场景渲染兜底【新增】：每帧检查"场景里有没有相机在渲染"，没有就临时开一台"只清黑屏"的相机——
//    覆盖"房主点开始 → 服务端切场景 → 客机旧玩家已销毁、新玩家还没 Spawn"的空窗期
//    （表现就是客机 Game 视图闪一下 "No cameras rendering"）。有真相机出现后它立刻自动收回。
//
// 【重要】为什么用"每帧兜底检查"而不是一次性的 Awake 检查：
// Mirror 的失活时机不确定（实测晚于本组件的 Awake）——一次性检查时它还没被失活，
// 条件不成立，之后失活就再没人管了。改成每帧检查后，无论什么时候被失活都能拉回来。
public class OfflineSceneKeeper : MonoBehaviour
{
    [Tooltip("要保活的场景物体（留空 = 自动找 GameManager，含未激活对象）")]
    [SerializeField] private GameObject targetObject;

    private float nextScan;   // 还没找到 GameManager 时，下次重新搜索的时间（避免每帧全场景查找）

    private Camera fallbackCam;    // 【新增】渲染兜底相机（没相机可渲染时临时顶上的黑屏相机）
    private float noCameraTimer;   // 【新增】"已经连续多久没有任何相机在渲染"的计时

    [Tooltip("无相机空窗超过多少秒才启用黑屏兜底：" +
             "调小 → 主机那种极短空窗也会盖上黑屏；调大 → 只在明显卡顿时才遮。默认 0.1 秒")]
    [SerializeField] private float noCameraGrace = 0.1f;

    private void Start()
    {
        // 无条件日志：先确认脚本确实在跑（排查问题时不用猜）
        Debug.Log("[单机保活] OfflineSceneKeeper 已启动（挂在 " + gameObject.name + " 上）");
        CheckAndActivate();
    }

    private void Update()
    {
        // 【新增】渲染兜底相机：和网络状态无关（联机时也要跑），所以放在最前面
        UpdateFallbackCamera();

        // 联机中（Host / Client）：单机保活不插手，交给 Mirror 服务器 Spawn 时激活
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

    // 【新增】渲染兜底相机：
    // 双开时房主点"开始"→ 服务端切场景，客机在"旧玩家已销毁、新玩家还没 Spawn"的空窗期里
    // 场景没有任何相机在渲染 → Game 视图显示 "No cameras rendering"。
    // 兜底逻辑：连续 noCameraGrace 秒（默认 0.1，Inspector 可调）没有任何已启用相机时，
    // 临时开一台"只清黑屏"的相机顶着；一旦真的有相机（玩家相机）出现，立刻自动收回。
    // 全程自管，正常游玩中永远不出现。
    private void UpdateFallbackCamera()
    {
        // 有没有"别人的"已启用相机？（Camera.allCameras 只包含已启用的）
        bool hasOther = false;
        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            if (fallbackCam != null && all[i] == fallbackCam) continue;   // 兜底相机自己不算
            hasOther = true;
            break;
        }

        if (hasOther)
        {
            noCameraTimer = 0f;
            if (fallbackCam != null && fallbackCam.enabled) fallbackCam.enabled = false;   // 收回兜底
            return;
        }

        // 用 unscaled 计时：就算对局把 timeScale 冻结了，也照常兜底
        noCameraTimer += Time.unscaledDeltaTime;
        if (noCameraTimer < noCameraGrace) return;   // 空窗时长不够就不触发（避免正常启动瞬间闪黑）

        if (fallbackCam == null) fallbackCam = CreateFallbackCamera();
        if (!fallbackCam.enabled) fallbackCam.enabled = true;
    }

    // 造一台"只负责清屏"的兜底相机：纯黑、不渲染任何物体、深度拉到最低
    private Camera CreateFallbackCamera()
    {
        GameObject go = new GameObject("FallbackCamera(渲染兜底)");
        go.transform.SetParent(transform, false);   // 挂在常驻物体下（随场景卸载一起销毁）
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.cullingMask = 0;        // 什么都不渲染 → 纯黑屏
        cam.depth = -100f;
        cam.enabled = false;        // 开/关都交给 UpdateFallbackCamera 决定
        return cam;
    }
}