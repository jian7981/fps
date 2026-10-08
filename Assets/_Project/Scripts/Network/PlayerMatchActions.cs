using Mirror;
using UnityEngine;

// 【阶段2.4-C】玩家对局操作（联机命令入口）
// 目前只有一条：请求返回房间 —— 结算面板的"返回房间"按钮调用。
// 规则（每人自己管自己）：点了 = 自己表态"要再来一局"；
//   服务器检查所有人是否都表了态，全部表态后才切回房间（一个人点不会把别人带走）。
// 挂载：Player.prefab（根物体，和 Health 等组件放一起）
public class PlayerMatchActions : NetworkBehaviour
{
    // 我是否已选择"返回房间"（服务器改，各端可见——结算按钮靠它显示"等待其他玩家"）
    [SyncVar] public bool wantsNextRound;

    [Command]
    public void CmdRequestReturnToRoom()
    {
        if (NetworkManager.loadingSceneAsync != null) return;   // 已经有场景切换在进行 → 忽略
        wantsNextRound = true;                                  // 服务器记录我的表态
        Debug.Log("[房间] 有玩家选择了返回房间，等待其他人…");
        CheckAllWantNextRound();
    }

    // 服务器：所有人都表态了 → 一起回房间（连接保持、准备状态自动重置）
    [Server]
    private void CheckAllWantNextRound()
    {
        foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
        {
            if (conn == null || conn.identity == null) continue;
            PlayerMatchActions a = conn.identity.GetComponent<PlayerMatchActions>();
            if (a == null || !a.wantsNextRound) return;   // 还有人没点 → 继续等
        }

        GameNetworkManager room = NetworkManager.singleton as GameNetworkManager;
        if (room == null) return;
        Debug.Log("[房间] 全员选择返回房间，切回房间场景");
        room.ServerChangeScene(room.RoomScene);
    }
}