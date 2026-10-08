using UnityEngine;
using Mirror;

// 【阶段2.4-B】房间玩家（Lobby 房间里"这一位玩家"）
// 职责：显示名 + 队伍分配（只有服务器在玩家进房时决定）；
//       进图时由 GameNetworkManager 把这些数据带到游戏玩家身上（Health.SetTeamId）。
// 挂载：RoomPlayer.prefab（替换掉原来直接挂的 Mirror 原生 NetworkRoomPlayer 组件）
public class RoomPlayer : NetworkRoomPlayer
{
    [SyncVar] public string displayName = "";   // 房间/结算显示的名字（暂用"玩家 N"，以后接昵称）
    [SyncVar] public int teamId;                // 0=A队，1=B队（服务器按连接顺序轮流分）

    // 服务器：房间玩家生成时——分配名字和队伍
    // 注意：不依赖 Mirror 的 index（调试 UI 里实测它没生效），直接用连接的 connectionId
    public override void OnStartServer()
    {
        int connId = (connectionToClient != null) ? connectionToClient.connectionId : index;
        displayName = "玩家 " + (connId + 1);
        teamId = connId % 2;                    // 轮流分 A/B：房主(0)→A，第2人(1)→B，第3人(2)→A …
        Debug.Log($"[房间] {displayName} 进房，队伍 {(teamId == 0 ? "A" : "B")}");
    }
}