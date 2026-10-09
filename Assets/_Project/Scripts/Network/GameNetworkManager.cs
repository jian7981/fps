using System.Collections.Generic;   // HashSet（出生点占用记录）
using UnityEngine;                  // Time / Debug / Transform
using Mirror;

// 【阶段2.1】游戏网络管理器
// 【阶段2.4-A】升级为 NetworkRoomManager：大厅即房间（Lobby=RoomScene，map1=GameplayScene）
// Transport（KCP）、Player Prefab、Online/Offline Scene、房间设置 全部在 Inspector 配置。
// 【阶段2.4-C】全员准备后由房主在房间面板点"开始游戏"；出生点按"队伍 + 座位号"分配并避让已占用点位。
public class GameNetworkManager : NetworkRoomManager
{
    // 【阶段2.4-C 扩展】本局已被占用的出生点：座位映射优先，避免两个玩家站同一个出生点；
    // 每次场景切换时清空（为下一局重新累计）
    private readonly HashSet<Transform> usedSpawnPoints = new HashSet<Transform>();

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
    // 【阶段2.4-C 扩展】顺带清空"出生点占用记录"（下一局生成玩家时会重新累计）
    public override void OnRoomServerSceneChanged(string sceneName)
    {
        usedSpawnPoints.Clear();
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
            Debug.Log($"[房间] {rp.displayName} 进入对局：{(rp.teamId == 0 ? "A" : "B")}队 {rp.seatIndex + 1} 号位");
        }
        return true;
    }

    // 【阶段2.4-B/C】按"队伍 + 座位号"分配出生点：
    // A队1号位 → A队出生点[0]、2号位 → [1] …；该点已被本局别人占用 → 顺延找本队下一个空的
    public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        RoomPlayer rp = roomPlayer.GetComponent<RoomPlayer>();
        int team = (rp != null) ? rp.teamId : 0;
        int seat = (rp != null) ? Mathf.Clamp(rp.seatIndex, 0, 4) : 0;

        // map1 刚加载完，找场景里的 GameManager 拿该队的出生点
        GameManager gm = FindObjectOfType<GameManager>();
        Transform[] list = (gm != null) ? ((team == 0) ? gm.teamASpawnPoints : gm.teamBSpawnPoints) : null;
        if (list == null || list.Length == 0) return null;   // 找不到就回退：基类默认逻辑（NetworkStartPosition 轮流）

        Transform spawn = PickSpawn(list, seat);
        usedSpawnPoints.Add(spawn);                          // 记为本局已占用

        GameObject gamePlayer = Instantiate(playerPrefab, spawn.position, spawn.rotation);
        // 【扩展】把他的"专属出生点"记在 Health 上 → 之后死亡复活会优先回到这个点（被占则顺延）
        Health h = gamePlayer.GetComponentInChildren<Health>();
        if (h != null) h.SetRespawnPoint(spawn);

        Debug.Log($"[房间] {(team == 0 ? "A" : "B")}队 {seat + 1} 号位出生点：{spawn.name}");
        return gamePlayer;   // 返回给基类去替换 roomPlayer
    }

    // 选出生点：优先"座位号 % 长度"对应的那个；被占用了就顺延找下一个空的；全被占了就返回座位对应的
    private Transform PickSpawn(Transform[] list, int seat)
    {
        int startIndex = seat % list.Length;
        for (int i = 0; i < list.Length; i++)
        {
            Transform candidate = list[(startIndex + i) % list.Length];
            if (candidate != null && !usedSpawnPoints.Contains(candidate)) return candidate;
        }
        return list[startIndex];
    }
}