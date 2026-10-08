using UnityEngine;   // Time / Debug
using Mirror;

// 【阶段2.1】游戏网络管理器
// 【阶段2.4-A】升级为 NetworkRoomManager：大厅即房间（Lobby=RoomScene，map1=GameplayScene）
// Transport（KCP）、Player Prefab、Online/Offline Scene、房间设置 全部在 Inspector 配置。
// 流程：建房(StartHost) → 玩家进房成为 RoomPlayer → 全员准备 → 自动切 map1 → 对局。
public class GameNetworkManager : NetworkRoomManager
{
    // 【阶段2.3-B2】客户端断开时的收尾（主动停服 / 被动掉线都会走这里）：
    // 1) 恢复时间：对局结束时全场 timeScale=0 冻结，这里"解冻"，避免回大厅后菜单点不动
    // 2) "回大厅"不用写：Mirror 内置机制会在断开后自动加载 offlineScene（= Lobby），各端各自完成
    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Time.timeScale = 1f;
        Debug.Log("[网络] 与服务器断开，Mirror 将自动返回大厅（offlineScene）");
    }


    // 【阶段2.4-C】全员准备后不自动开局 —— 改为由房主在房间面板点"开始游戏"
    // （覆写后"故意不调用 base"：基类实现会立刻 ServerChangeScene(GameplayScene)）
    public override void OnRoomServerPlayersReady()
    {
        Debug.Log("[房间] 全员已准备，等待房主点开始…");
    }

    // 【阶段2.4-B2】回到房间场景时解冻：对局结束时全场 timeScale=0，
    // 若只靠点按钮的那台机器恢复，客机回房间后菜单会点不动 —— 所以在场景切换回调里各端统一恢复
    public override void OnRoomServerSceneChanged(string sceneName)
    {
        if (Utils.IsSceneActive(RoomScene)) Time.timeScale = 1f;
    }

    public override void OnRoomClientSceneChanged()
    {
        if (Utils.IsSceneActive(RoomScene)) Time.timeScale = 1f;
    }


    // 【阶段2.4-B】玩家进图时：把房间玩家的数据（队伍）带到游戏玩家身上
    // （这个回调发生在"生成之后、替换玩家对象之前"，队伍在这里写好，会随初始同步包发给各端）
    public override bool OnRoomServerSceneLoadedForPlayer(NetworkConnectionToClient conn, GameObject roomPlayer, GameObject gamePlayer)
    {
        RoomPlayer rp = roomPlayer.GetComponent<RoomPlayer>();
        Health h = gamePlayer.GetComponentInChildren<Health>();
        if (rp != null && h != null)
        {
            h.SetTeamId(rp.teamId);
            Debug.Log($"[房间] {rp.displayName} 进入对局：队伍 {(rp.teamId == 0 ? "A" : "B")}");
        }
        return true;
    }

    // 【阶段2.4-B】按队伍选出生点（默认实现是所有点位轮流，两队会混在一起）
    public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        RoomPlayer rp = roomPlayer.GetComponent<RoomPlayer>();
        int team = (rp != null) ? rp.teamId : 0;

        // map1 刚加载完，找场景里的 GameManager 拿该队的出生点
        GameManager gm = FindObjectOfType<GameManager>();
        Transform[] list = (gm != null) ? ((team == 0) ? gm.teamASpawnPoints : gm.teamBSpawnPoints) : null;
        if (list == null || list.Length == 0) return null;   // 找不到就回退：基类默认逻辑（NetworkStartPosition 轮流）

        Transform spawn = list[Random.Range(0, list.Length)];
        return Instantiate(playerPrefab, spawn.position, spawn.rotation);   // 返回给基类去替换 roomPlayer
    }

}