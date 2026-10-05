using System;                     // Action（事件）
using System.Collections.Generic; // List<>
using UnityEngine;

// ============ 单人战绩（纯数据类，不是组件）============
// 结算表格里一行的数据来源
public class CombatantStats
{
    public string displayName;   // 显示名："你" / "A2" / "B5"
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
// 挂载：map1 场景里挂 GameManager 脚本的那个物体上（和 GameManager / MatchRules 同物体）
public class MatchState : MonoBehaviour
{
    // ---------- 比分 ----------
    public int TeamAScore { get; private set; }
    public int TeamBScore { get; private set; }
    public bool IsOver { get; private set; }
    public int WinnerTeamId { get; private set; } = -1;

    // ---------- 【新增】所有人的战绩（结算表格的数据源）----------
    private readonly List<CombatantStats> allStats = new List<CombatantStats>();
    public List<CombatantStats> AllStats => allStats;   // 只读出口，结算面板遍历它填表

    // 事件：UI 只订阅，不轮询（铁律1）
    public event Action OnScoreChanged;
    public event Action<int> OnMatchOver;

    // 【新增】登记一个参战者：创建它的战绩对象并返回（MatchRules 保存引用，死亡时给它记账）
    public CombatantStats AddCombatant(string displayName, int teamId)
    {
        CombatantStats stats = new CombatantStats(displayName, teamId);
        allStats.Add(stats);
        return stats;
    }

    // 给某一队加 1 分（由 MatchRules 调用）
    public void AddScore(int teamId)
    {
        if (IsOver) return;
        if (teamId == 0) TeamAScore++;
        else TeamBScore++;
        OnScoreChanged?.Invoke();
    }

    // 结束对局，记录赢家
    public void EndMatch(int winnerTeamId)
    {
        if (IsOver) return;
        IsOver = true;
        WinnerTeamId = winnerTeamId;
        OnMatchOver?.Invoke(winnerTeamId);
    }

    // 读某一队当前比分
    public int GetScore(int teamId)
    {
        return (teamId == 0) ? TeamAScore : TeamBScore;
    }
}