﻿using UnityEngine;
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
    [SyncVar] public bool isRoomOwner;          // 【阶段3】房主身份：第一个进房的人（逻辑标记，不依赖"谁跑服务器"→ 云服务器上也有房主）
    [SyncVar] public string roomCode = "";      // 【阶段3-房间码】服务器向登记服务注册成功后下发；房间面板显示给玩家分享
    [SyncVar] public int joinOrder;             // 【阶段3-玩家名】进房顺序（1 = 房主）；名字与队内排序都用它
    [SyncVar] public int avatarId;              // 【阶段4-G】头像编号（0 = 默认）；房间玩家框靠它显示各人头像

    // 【阶段4】账号令牌：**只留在服务器内存里**，绝不做 SyncVar —— 那是账号凭证，不该下发给任何客户端。
    // 用途只有一个：对局结束后服务器替这名玩家上报战绩（客户端自己拿令牌是调不通那个接口的）。
    [System.NonSerialized] public string accountToken = "";

    // 【阶段4】把账号身份报给服务器。服务器只拿它做两件事：
    //   ① 把 displayName 换成真昵称（SyncVar → 房间界面与对局战绩表都看到真名）
    //   ② 存下令牌备"对局结束上报战绩"
    //
    // 【Bug 修复】原来写在 OnStartLocalPlayer 里 —— 实测房间制下这个钩子没按预期触发，
    // 昵称一直没送到服务器（房间面板始终"玩家 1/2"）。改成在 Update 里做**一次性**上报：
    // 本机玩家在房间里的第一次 Update 一定跑得到，不依赖任何钩子的触发时机。
    private bool accountAttempted;

    private void Update()
    {
        if (accountAttempted || !isLocalPlayer) return;
        accountAttempted = true;              // 只试一次：没登录也不用每帧重试

        AuthService.Init();                   // 先把本地存档里的令牌读出来（幂等）
        if (!AuthService.IsLoggedIn)
        {
            Debug.Log("[房间] 本机玩家未登录 → 保持\"玩家 N\"（对局结束不写战绩）");
            return;
        }

        CmdSetAccount(AuthService.CurrentNickname, AuthService.CurrentToken, AuthService.CurrentAvatarId);
    }

    [Command]
    private void CmdSetAccount(string nickname, string token, int newAvatarId)
    {
        // 昵称走 SyncVar → 房间界面与对局战绩表都会显示真昵称
        if (!string.IsNullOrWhiteSpace(nickname)) displayName = nickname.Trim();
        accountToken = token ?? "";

        // 【阶段4-G】头像编号由**客户端自报**，服务器只做范围校验。
        // 为什么不做签名/查库校验：头像不参与任何战斗判定，被伪造也只是改到自己头上；
        // 为它加一层验证的收益远不如复杂度。越界的一律回落到 0（默认头像）。
        avatarId = (newAvatarId >= 0 && newAvatarId <= AvatarLibrary.MaxAvatarId) ? newAvatarId : 0;

        Debug.Log($"[房间] {displayName} 已绑定账号（头像 #{avatarId}，对局结束会写入战绩）");
    }

    // 服务器：房间玩家生成时——分配名字、队伍、座位
    public override void OnStartServer()
    {
        // 【修复】原来用 connectionId 的奇偶分队 —— 但 Mirror 的 connectionId 是随机大数（不是 0/1/2 递增），
        // 实际分队是随机的（日志实测：每个客户端每次进来随机落 A 或 B），和"轮流分"的意图不符。
        // 改用 NetworkRoomManager 的 clientIndex（每来一个玩家 +1，稳定递增）→ 真正按加入顺序轮流分。
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        joinOrder = (room != null && room.clientIndex > 0) ? room.clientIndex : (index + 1);   // 房主 = 1、第二个 = 2…
        teamId = (joinOrder - 1) % 2;           // 房主 A、第二个 B、第三个 A……（进房后可以自己换位/换队）
        seatIndex = FindFreeSeat(teamId);       // 落在本队第一个空位

        // 【阶段3-玩家名】名字也用"进房顺序"，不再用 connectionId —— Mirror 的 connectionId 是随机大数，
        // 之前会显示成"玩家 23456789"，朋友看到不明所以。现在房主是"玩家 1"、第二个进来的是"玩家 2"。
        displayName = "玩家 " + joinOrder;

        // 【阶段3】房主 = 第一个进房的人：场上还没有任何房主 → 就是自己
        // （roomSlots 在 Start() 里才会把自己加进去，所以这里遍历到的是"之前进房的人"）
        isRoomOwner = true;
        if (room != null)
        {
            foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
            {
                RoomPlayer rp = basePlayer as RoomPlayer;
                if (rp != null && rp.isRoomOwner) { isRoomOwner = false; break; }
            }
        }

        Debug.Log($"[房间] {displayName} 进房：{(teamId == 0 ? "A" : "B")}队 {seatIndex + 1} 号位{(isRoomOwner ? "（房主）" : "")}");
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

    // 【阶段3】房主请求开始对局：云服务器模式下房主是普通玩家（不是服务器本机），
    // 不能直接切场景 → 发请求让服务器校验（房主身份 + 全员准备）后执行切换。
    [Command]
    public void CmdRequestStartGame()
    {
        if (!isRoomOwner) return;                              // 只有房主
        if (NetworkManager.loadingSceneAsync != null) return;  // 已有场景切换在进行 → 忽略
        GameNetworkManager room = NetworkManager.singleton as GameNetworkManager;
        if (room == null || !room.allPlayersReady) return;     // 必须全员准备
        Debug.Log("[房间] 房主请求开始 → 切换场景");
        room.ServerChangeScene(room.GameplayScene);
    }
}