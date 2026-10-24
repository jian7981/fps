﻿using System.Collections.Generic;   // Dictionary
using UnityEngine;

// ============ 对局规则：先到目标击杀数获胜 + 全员战绩记账 ============
// 挂载：map1 场景里挂 GameManager 脚本的那个物体上（和 GameManager / MatchState 同物体）
public class MatchRules : MonoBehaviour
{
    [Header("规则配置")]
    [Tooltip("哪队先达到这个击杀数就获胜（测试时可以临时改小，比如 5）")]
    [SerializeField] private int targetKills = 30;
    [Tooltip("助攻判定窗口：在某人死前这么多秒内打过他的人，算助攻")]
    [SerializeField] private float assistWindow = 5f;

    public int TargetKills => targetKills;

    private MatchState state;

    // Health → 他在 MatchState 里的那条战绩（死亡/击杀/助攻都记到它头上）
    private readonly Dictionary<Health, CombatantStats> statsByHealth = new Dictionary<Health, CombatantStats>();

    // 【阶段4】本局的唯一 id：对局结束上报战绩时用，同一局所有人都用它。
    // 重开一局 = map1 场景重载 = 这个组件重新创建 → GUID 天然每局不同。
    // 配合后端 match_report_log 的 (match_id, user_id) 主键 → 重复上报天然幂等。
    private readonly string matchId = System.Guid.NewGuid().ToString();

    // 死者 → （打过他的人 → 最后一次打他的时间），用来判定助攻
    private readonly Dictionary<Health, Dictionary<Health, float>> recentAttackers
        = new Dictionary<Health, Dictionary<Health, float>>();

    private void Awake()
    {
        state = GetComponent<MatchState>();
        if (state == null)
            Debug.LogError("MatchRules：同一物体上缺少 MatchState 组件！");
    }

    // GameManager 每生成一个角色（玩家/任意 Bot）就调用一次：登记战绩 + 接上死亡/受伤事件
    public void RegisterCombatant(Health health, string displayName)
    {
        if (health == null || state == null) return;

        // 1. 在 MatchState 里建一条战绩，并记住"这个 Health 对应哪条"
        statsByHealth[health] = state.AddCombatant(displayName, health.TeamId);

        // 2. 死亡：记战绩 + 团队计分
        health.OnDied += killer => HandleCombatantDied(health, killer);

        // 3. 受伤：记录"谁打过他、最后一次是什么时候"，死亡时用来算助攻
        health.OnDamaged += (damage, attacker) => RecordDamage(health, attacker);
    }

    // 记录一次伤害来源（只留"最后一次"的时间点，够判定助攻了）
    private void RecordDamage(Health victim, GameObject attackerObj)
    {
        if (state == null || state.IsOver || attackerObj == null) return;

        // 攻击者可能传的是子物体，往上找带 Health 的根
        Health attacker = attackerObj.GetComponentInParent<Health>();
        if (attacker == null || attacker == victim) return;   // 环境伤害 / 自己打自己 → 不算

        if (!recentAttackers.TryGetValue(victim, out Dictionary<Health, float> map))
        {
            map = new Dictionary<Health, float>();
            recentAttackers[victim] = map;
        }
        map[attacker] = Time.time;   // 刷新"最后一次打他的时间"
    }

    // 有人死了：先记战绩，再走团队计分
    private void HandleCombatantDied(Health victim, GameObject killer)
    {
        if (state == null || state.IsOver) return;

        // ---------- 1. 死亡 +1 ----------
        if (statsByHealth.TryGetValue(victim, out CombatantStats victimStats))
            victimStats.deaths++;

        // ---------- 2. 击杀归属：killer 是谁 ----------
        Health killerHealth = (killer != null) ? killer.GetComponentInParent<Health>() : null;
        if (killerHealth != null && killerHealth != victim
            && statsByHealth.TryGetValue(killerHealth, out CombatantStats killerStats))
        {
            killerStats.kills++;   // 只有"别人打死的"才算击杀（自杀/环境死不计）
        }

        // ---------- 3. 助攻：死前 assistWindow 秒内打过他、又不是击杀者的人 ----------
        if (recentAttackers.TryGetValue(victim, out Dictionary<Health, float> attackers))
        {
            foreach (KeyValuePair<Health, float> pair in attackers)
            {
                Health helper = pair.Key;
                if (helper == null || helper == victim || helper == killerHealth) continue;  // 击杀者不算助攻
                if (Time.time - pair.Value > assistWindow) continue;                          // 太久之前打的，不算
                if (statsByHealth.TryGetValue(helper, out CombatantStats helperStats))
                    helperStats.assists++;
            }
            recentAttackers.Remove(victim);   // 伤害记录用完清掉（复活后重新累计）
        }

        // ---------- 4. 团队计分（原有逻辑）----------
        int scoringTeam = (victim.TeamId == 0) ? 1 : 0;
        state.AddScore(scoringTeam);

        Debug.Log("[对局] " + ((victim.TeamId == 0) ? "A队" : "B队") + " 阵亡 → "
            + ((scoringTeam == 0) ? "A队" : "B队") + " 得分，当前比分 A "
            + state.TeamAScore + " : " + state.TeamBScore + " B");

        if (state.GetScore(scoringTeam) >= targetKills)
        {
            state.EndMatch(scoringTeam);
            ReportStatsToAccountService(scoringTeam);   // 【阶段4】服务器替每个已登录玩家上报战绩
            Debug.Log("[对局] " + ((scoringTeam == 0) ? "A队" : "B队")
                + " 先到 " + targetKills + " 杀，获胜！");
        }
    }

    // ==================== 【阶段4】对局结束：把战绩写进账号 ====================
    // 由**服务器**替每个玩家上报（玩家客户端没有上报密钥，自己调那个接口是调不通的 —— 见后端 StatsEndpoints）。
    // 没配 -lobbyserver、或玩家没登录（单人练习 / 局域网 / Bot）→ 直接跳过，一切照旧。
    private void ReportStatsToAccountService(int winnerTeamId)
    {
        if (!NetUtil.IsServerSide()) return;     // 只由权威侧上报
        if (!LobbyService.HasService) return;    // 没配登记服务 → 整个账号功能是关闭的

        // ---------- 【阶段4-H】人机局不给经验 ----------
        // 判定用的是"这个角色的连接"而不是"有没有登录令牌"：
        //   · 真人玩家由 NetworkManager 生成，netIdentity.connectionToClient 非空（Host 也算）
        //   · Bot 是服务器 NetworkServer.Spawn 出来的，没有 owner → connectionToClient 为 null
        //   · 纯单机没有网络会话，所有角色都走不到联机生成流程 → 计数为 0
        // 少于 2 个真人 = 这局是"一个人带一群 Bot 刷分"，整局不上报（否则经验系统就成了刷分器）。
        int humans = 0;
        foreach (KeyValuePair<Health, CombatantStats> pair in statsByHealth)
            if (IsHumanPlayer(pair.Key)) humans++;

        if (humans < 2)
        {
            Debug.Log("[战绩] 本局真人玩家 " + humans + " 名（< 2）→ 按人机局处理，不上报、不发经验");
            return;
        }

        int reported = 0;
        foreach (KeyValuePair<Health, CombatantStats> pair in statsByHealth)
        {
            Health health = pair.Key;
            CombatantStats stats = pair.Value;
            if (health == null || stats == null) continue;
            if (string.IsNullOrEmpty(health.accountToken)) continue;   // 这名玩家没登录 → 不写他的战绩

            bool won = (stats.teamId == winnerTeamId);
            StartCoroutine(AuthService.ReportMatchStats(
                health.accountToken, matchId, stats.teamId,
                stats.kills, stats.deaths, stats.assists, won,
                result =>
                {
                    Debug.Log("[战绩] " + stats.displayName + " 上报成功：经验 +" + result.expGain
                        + (result.leveledUp ? "，升到 " + result.level + " 级" : "")
                        + (result.duplicated ? "（这一局之前已报过，未重复累加）" : ""));

                    // 【阶段4-H】把结算结果定向发给**这个玩家本人**
                    // 注意判 connectionToClient：Bot 没有连接，对 Bot 调 TargetRpc 会报 "no connection"
                    if (IsHumanPlayer(health))
                    {
                        health.TargetShowMatchReward(
                            result.expGain, result.duplicated, result.levelBefore, result.level,
                            result.exp, result.expToNext,
                            result.baseExp, result.killExp, result.assistExp, result.winExp, result.dailyExp,
                            result.capped);
                    }
                },
                err =>
                {
                    Debug.LogWarning("[战绩] " + stats.displayName + " 上报失败：" + err);
                    // 让玩家那边从"结算中…"变成明确的失败提示
                    if (IsHumanPlayer(health)) health.TargetShowMatchRewardFailed(err);
                }));
            reported++;
        }

        Debug.Log("[战绩] 本局 " + matchId + " 共 " + humans + " 名真人、上报 " + reported + " 名登录玩家");
    }

    // 真人玩家 = 有连接的玩家对象（Host 的本机玩家也有连接；Bot 没有）
    private static bool IsHumanPlayer(Health health)
    {
        return health != null && health.netIdentity != null && health.netIdentity.connectionToClient != null;
    }
}