using System.Collections.Generic;   // Dictionary
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
            Debug.Log("[对局] " + ((scoringTeam == 0) ? "A队" : "B队")
                + " 先到 " + targetKills + " 杀，获胜！");
        }
    }
}