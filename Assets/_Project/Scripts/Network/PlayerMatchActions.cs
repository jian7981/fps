using System;                       // Action
using System.Collections.Generic;   // List<>
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

    // 【HUD 10 人记分板】客户端请求快照 → 服务器回包 → HUD 订阅
    public event Action<List<ScoreboardEntry>> OnScoreboardReceived;
    private readonly List<ScoreboardEntry> scoreboard = new List<ScoreboardEntry>();
    public IReadOnlyList<ScoreboardEntry> Scoreboard => scoreboard;

    [Command]
    public void CmdRequestReturnToRoom()
    {
        if (NetworkManager.loadingSceneAsync != null) return;   // 已经有场景切换在进行 → 忽略
        wantsNextRound = true;                                  // 服务器记录我的表态
        Debug.Log("[房间] 有玩家选择了返回房间，等待其他人…");
        CheckAllWantNextRound();
    }

    // 【HUD 10 人记分板】按 Tab 时由 HUDManager 调用；服务器生成快照后 TargetRpc 回给本人
    [Command]
    public void CmdRequestScoreboard()
    {
        MatchRules rules = FindObjectOfType<MatchRules>();
        if (rules == null) return;

        List<ScoreboardEntry> entries = rules.BuildScoreboard(connectionToClient);
        int n = entries.Count;
        string[] names = new string[n];
        int[] teams = new int[n];
        int[] kills = new int[n];
        int[] deaths = new int[n];
        int[] assists = new int[n];
        bool[] locals = new bool[n];

        for (int i = 0; i < n; i++)
        {
            ScoreboardEntry e = entries[i];
            names[i] = e.displayName;
            teams[i] = e.teamId;
            kills[i] = e.kills;
            deaths[i] = e.deaths;
            assists[i] = e.assists;
            locals[i] = e.isLocal;
        }

        TargetScoreboard(connectionToClient, names, teams, kills, deaths, assists, locals);
    }

    [TargetRpc]
    private void TargetScoreboard(NetworkConnectionToClient target,
        string[] names, int[] teams, int[] kills, int[] deaths, int[] assists, bool[] locals)
    {
        scoreboard.Clear();
        int n = names.Length;
        if (teams.Length < n) n = teams.Length;
        if (kills.Length < n) n = kills.Length;
        if (deaths.Length < n) n = deaths.Length;
        if (assists.Length < n) n = assists.Length;
        if (locals.Length < n) n = locals.Length;
        for (int i = 0; i < n; i++)
        {
            ScoreboardEntry e = new ScoreboardEntry();
            e.displayName = names[i];
            e.teamId = teams[i];
            e.kills = kills[i];
            e.deaths = deaths[i];
            e.assists = assists[i];
            e.isLocal = locals[i];
            scoreboard.Add(e);
        }

        OnScoreboardReceived?.Invoke(scoreboard);
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