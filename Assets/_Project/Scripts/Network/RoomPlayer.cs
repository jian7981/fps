using UnityEngine;
using Mirror;

// 【阶段2.4-B】房间玩家（Lobby 房间里"这一位玩家"）
// 职责：显示名 + 队伍 + 座位号（只有服务器在玩家进房时决定）；
//       进图时由 GameNetworkManager 把这些数据带到游戏玩家身上（Health.SetTeamId / 出生点）。
// 【阶段2.4-C 扩展】座位号 seatIndex：0~4 = 本队 1~5 号位；
//       支持跨队换位：点任意空位槽 = 把自己移过去，队伍跟着变（服务器校验空位后生效）。
// 挂载：RoomPlayer.prefab（替换掉原来直接挂的 Mirror 原生 NetworkRoomPlayer 组件）
public class RoomPlayer : NetworkRoomPlayer
{
    [SyncVar] public string displayName = "";   // 房间/结算显示的名字（暂用"玩家 N"，以后接昵称）
    [SyncVar] public int teamId;                // 0=A队，1=B队
    [SyncVar] public int seatIndex;             // 【扩展】本队内座位：0~4 = 1~5 号位

    // 服务器：房间玩家生成时——分配名字、队伍、座位
    // 注意：不依赖 Mirror 的 index（调试 UI 里实测它没生效），直接用连接的 connectionId
    public override void OnStartServer()
    {
        int connId = (connectionToClient != null) ? connectionToClient.connectionId : index;
        displayName = "玩家 " + (connId + 1);
        teamId = connId % 2;                    // 初始轮流分 A/B（进房后可以自己换位/换队）
        seatIndex = FindFreeSeat(teamId);       // 落在本队第一个空位
        Debug.Log($"[房间] {displayName} 进房：{(teamId == 0 ? "A" : "B")}队 {seatIndex + 1} 号位");
    }

    // 找本队第一个没被占的座位（自己已经在 roomSlots 里，跳过自己）
    // 注意：roomSlots 是 NetworkRoomManager 上的成员，NetworkManager.singleton 要先转成 NetworkRoomManager
    private int FindFreeSeat(int team)
    {
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null) return 0;

        bool[] used = new bool[5];
        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp == null || rp == this) continue;
            if (rp.teamId == team && rp.seatIndex >= 0 && rp.seatIndex < 5) used[rp.seatIndex] = true;
        }
        for (int i = 0; i < 5; i++)
            if (!used[i]) return i;
        return 0;   // 理论到不了（单队超过 5 人会被房间容量挡住）
    }

    // 【阶段2.4-C 扩展】换位（支持跨队）：slotIndex 0~9 —— 0~4 = A队1~5号位，5~9 = B队1~5号位
    // 服务器校验：下标合法、目标槽位为空；换到对方队的槽位 = 连队伍一起换
    [Command]
    public void CmdChangeSeat(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex > 9) return;

        int newTeam = (slotIndex < 5) ? 0 : 1;
        int newSeat = slotIndex % 5;
        if (newTeam == teamId && newSeat == seatIndex) return;   // 就是现在的位置，不处理

        // 目标必须为空（同样先转成 NetworkRoomManager 才能拿到 roomSlots）
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null) return;
        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp == null || rp == this) continue;
            if (rp.teamId == newTeam && rp.seatIndex == newSeat) return;   // 已被占 → 拒绝
        }

        teamId = newTeam;      // 走 SyncVar：各端房间界面自动刷新
        seatIndex = newSeat;
        Debug.Log($"[房间] {displayName} 换到 {(newTeam == 0 ? "A" : "B")}队 {newSeat + 1} 号位");
    }
}