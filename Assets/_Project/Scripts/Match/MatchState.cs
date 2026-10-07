using System;                     // Action（事件）
using System.Collections;         // IEnumerator（协程）
using System.Collections.Generic; // List<>
using UnityEngine;
using Mirror;                     // 【阶段2.3】NetworkBehaviour / SyncVar / ClientRpc

// ============ 单人战绩（纯数据类，不是组件）============
// 结算表格里一行的数据来源
public class CombatantStats
{
    public string displayName;   // 显示名："你" / "A2" / "玩家 1"
    public int teamId;           // 0 = A队，1 = B队
    public int kills;            // 击杀
    public int deaths;           // 死亡
    public int assists;          // 助攻

    // 小构造函数：登记时直接填好名字和队伍
    public CombatantStats(string displayName, int teamId)
    {
        this.displayName = displayName;
        this.teamId = teamId;
    }
}

// ============ 对局状态（只存数据，不做规则）============
// 铁律4：对局数据放组件实例字段，不用 static —— 重开一局=场景重载=数据天然清零
// 【阶段2.3-B1】网络化：比分改 SyncVar（服务器记账 → 各端自动收到变化 → hook 触发原有事件，UI 零改动）
// 【阶段2.3-B2】结束改由 RpcMatchOver 广播：把"最终战绩表"一起发给各端，
//              客户端重建本地战绩后再触发 OnMatchOver —— 结算面板读到的就是完整数据
// 挂载：map1 场景里挂 GameManager 脚本的那个物体上（和 GameManager / MatchRules 同物体）
//       ★ 该物体需要在编辑器里补一个 NetworkIdentity 组件（联机的"身份证"）
public class MatchState : NetworkBehaviour   // 【阶段2.3】MonoBehaviour → NetworkBehaviour
{
    // ---------- 比分 / 结束状态（SyncVar：只有服务器能改，客户端只读）----------
    [SyncVar(hook = nameof(HookScoreChanged))] private int teamAScore;
    [SyncVar(hook = nameof(HookScoreChanged))] private int teamBScore;
    // 赢家：-1 = 还没结束；0/1 = 对应队获胜（IsOver 直接看它，不用再单独同步一个 bool）
    // 【B2 改动】去掉 hook：结束广播统一走 RpcMatchOver（要带战绩数据），避免两个来源触发两次
    [SyncVar] private int winnerTeamId = -1;

    // ---------- 对外只读出口（接口和单机版完全一致，UI / 规则零改动）----------
    public int TeamAScore => teamAScore;
    public int TeamBScore => teamBScore;
    public bool IsOver => winnerTeamId >= 0;
    public int WinnerTeamId => winnerTeamId;

    // ---------- 所有人的战绩（结算表格的数据源）----------
    // 服务器上由 MatchRules 记账；对局结束时随 RpcMatchOver 发给各端重建
    private readonly List<CombatantStats> allStats = new List<CombatantStats>();
    public List<CombatantStats> AllStats => allStats;   // 只读出口，结算面板遍历它填表

    // 事件：UI 只订阅，不轮询（铁律1）
    public event Action OnScoreChanged;
    public event Action<int> OnMatchOver;

    // ---------- SyncVar 变化时触发原有事件（各端表现层零改动）----------
    private void HookScoreChanged(int oldValue, int newValue) => OnScoreChanged?.Invoke();

    // 登记一个参战者：创建它的战绩对象并返回（MatchRules 保存引用，死亡时给它记账）
    public CombatantStats AddCombatant(string displayName, int teamId)
    {
        CombatantStats stats = new CombatantStats(displayName, teamId);
        allStats.Add(stats);
        return stats;
    }

    // 给某一队加 1 分（由 MatchRules 调用）
    // 【阶段2.3】联机中只有服务器能记账（客户端调用直接忽略）
    public void AddScore(int teamId)
    {
        if (!NetUtil.IsServerSide()) return;   // 纯单机恒为 true，行为不变
        if (IsOver) return;

        if (teamId == 0) teamAScore++;
        else teamBScore++;

        // hook 只覆盖"客户端收到同步"；权威侧显式补一次（host 模式可能重复一次，订阅方都是幂等操作，无影响）
        OnScoreChanged?.Invoke();
    }

    // 结束对局，记录赢家
    // 【阶段2.3-B2】联机：延迟 0.1 秒再广播（等"最终比分"的 SyncVar 先到各端）；
    //    纯单机：本地直接触发（行为与阶段1一致）
    public void EndMatch(int winnerTeamId)
    {
        if (!NetUtil.IsServerSide()) return;
        if (IsOver) return;

        this.winnerTeamId = winnerTeamId;   // SyncVar：各端 IsOver 状态同步（无 hook）

        if (NetworkServer.active)
            StartCoroutine(SendMatchOverRpcRoutine(winnerTeamId));   // 联机（服务器 / 主机）
        else
            OnMatchOver?.Invoke(winnerTeamId);                       // 纯单机
    }

    // 【阶段2.3-B2】稍后广播对局结束：RPC 与 SyncVar 走同一条可靠通道，先发的先到。
    // 这里等一小会儿，保证"最后一分"的 SyncVar 已经先到各端，再来触发结算
    // （此时 timeScale 可能已被冻结为 0，所以必须用 Realtime 等待）
    private IEnumerator SendMatchOverRpcRoutine(int winner)
    {
        yield return new WaitForSecondsRealtime(0.1f);

        // 组装最终战绩（纯基础类型数组，Mirror 可以直接序列化）
        int n = allStats.Count;
        string[] names = new string[n];
        int[] teams = new int[n];
        int[] kills = new int[n];
        int[] deaths = new int[n];
        int[] assists = new int[n];
        for (int i = 0; i < n; i++)
        {
            names[i] = allStats[i].displayName;
            teams[i] = allStats[i].teamId;
            kills[i] = allStats[i].kills;
            deaths[i] = allStats[i].deaths;
            assists[i] = allStats[i].assists;
        }
        RpcMatchOver(winner, names, teams, kills, deaths, assists);
    }

    // 【阶段2.3-B2】对局结束广播（服务器 → 所有客户端，主机自己也会收到一份）
    // 客户端：用服务器发来的最终战绩重建本地表 → 再触发 OnMatchOver（结算 UI 读表就是完整数据）
    [ClientRpc]
    private void RpcMatchOver(int winner, string[] names, int[] teams, int[] kills, int[] deaths, int[] assists)
    {
        allStats.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            CombatantStats s = new CombatantStats(names[i], teams[i]);
            s.kills = kills[i];
            s.deaths = deaths[i];
            s.assists = assists[i];
            allStats.Add(s);
        }
        OnMatchOver?.Invoke(winner);
    }

    // 读某一队当前比分
    public int GetScore(int teamId)
    {
        return (teamId == 0) ? teamAScore : teamBScore;
    }
}