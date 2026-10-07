using UnityEngine;   // Time / Debug
using Mirror;

// 【阶段2.1】游戏网络管理器
// Transport（用 KCP）、playerPrefab、onlineScene / offlineScene
// 全部在 Inspector 里配置（配置步骤见《阶段2-规划.md》步骤 2.1）。
// 路线：2.1 跑通移动同步 → 2.4 升级为 NetworkRoomManager（大厅即房间）。
public class GameNetworkManager : NetworkManager
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
}