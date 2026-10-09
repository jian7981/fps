using UnityEngine;
using Mirror;   // 【阶段2.1】NetworkServer / NetworkClient
using Shared;   // MatchConfig / MatchConfigTransfer / Constants / PlayerSlot

// ============ 对局管理器（map1 场景）============
// 职责：进场取大厅配置 → 生成玩家和 Bot → 登记计分 → 对局结束冻结全场
public class GameManager : MonoBehaviour
{
    [Header("玩家预制体")]
    public GameObject playerPrefab;

    [Header("Bot 预制体")]
    public GameObject botPrefab;

    [Header("出生点设置 5V5")]
    public Transform[] teamASpawnPoints; // 队伍A的出生点
    public Transform[] teamBSpawnPoints; // 队伍B的出生点

    private GameObject currentPlayer;    // 记住生成的玩家
    private MatchConfig matchConfig;     // 本局配置（大厅组队界面带进来的）

    // 【阶段2.5】给结算面板这类外部脚本用的只读出口（单机时怎么找到"本机玩家"）
    public GameObject CurrentPlayer => currentPlayer;

    // 【步骤1.5】对局流程的两个组件（挂在同一个物体上，Awake 里自动拿）
    private MatchState matchState;       // 比分 / 是否结束
    private MatchRules matchRules;       // 规则：先到 N 杀获胜

    private void Awake()
    {
        // 同一物体上直接拿组件，不需要在 Inspector 里拖
        matchState = GetComponent<MatchState>();
        matchRules = GetComponent<MatchRules>();
        if (matchState == null) Debug.LogError("GameManager：同物体上缺少 MatchState 组件！（比分不工作）");
        if (matchRules == null) Debug.LogError("GameManager：同物体上缺少 MatchRules 组件！（计分不工作）");
    }

    private void Start()
    {
        // 【阶段2.3】订阅"对局结束"：冻结全场（结算面板由 MatchResultUI 负责弹）
        // 这行原来放在单机流程末尾，联机分支提前 return 后走不到 → 联机结算不会冻结，现挪到最前
        if (matchState != null) matchState.OnMatchOver += HandleMatchOver;

        // 【阶段2.1】联机会话中：玩家由 NetworkManager 统一生成（map1 作为 onlineScene 自动加载），
        // 这里整体跳过本地生成，避免每台机器多冒出一个"本地单机玩家"
        // （Bot 的网络化补位留到 2.4 处理）
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.Log("[对局] 联机会话中：玩家由 NetworkManager 生成，GameManager 跳过本地生成");
            return;
        }

        // 1. 从"中转站"取大厅带过来的组队配置
        matchConfig = MatchConfigTransfer.Pending;
        if (matchConfig == null)
        {
            // 直接在编辑器里单独运行 map1 时会走到这里：给一份默认配置
            matchConfig = MatchConfig.CreateDefault(Constants.GameSceneName1);
            Debug.LogWarning("[对局] 没有收到大厅配置，改用默认配置（你 + 9 个 AI）");
        }

        // 2. 打印本局配置（人类/AI/空位 三项相加 = 5）
        Debug.Log("[对局配置] 地图：" + matchConfig.mapName
            + "｜A队：人类 " + matchConfig.CountHuman(0) + " + AI " + matchConfig.CountAI(0) + " + 空位 " + matchConfig.CountEmpty(0)
            + "｜B队：人类 " + matchConfig.CountHuman(1) + " + AI " + matchConfig.CountAI(1) + " + 空位 " + matchConfig.CountEmpty(1));

        // 3. 生成你自己（A队第 1 格）
        SpawnPlayerSingle();

        // 4. 按配置里的 AI 格子生成 Bot
        SpawnBotsFromConfig();

        // 5. 【步骤1.5】订阅"对局结束"：冻结全场（结算面板由 MatchResultUI 负责弹）
       // if (matchState != null) matchState.OnMatchOver += HandleMatchOver;
    }

    void SpawnPlayerSingle()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("玩家预制体未设置！");
            return;
        }
        if (teamASpawnPoints == null || teamASpawnPoints.Length == 0)
        {
            Debug.LogError("队伍A出生点未设置！");
            return;
        }
        Transform spawnPos = GetRandomSpawnPoint(0); // 用随机出生点
        currentPlayer = Instantiate(playerPrefab, spawnPos.position, spawnPos.rotation);
        SetupRespawn(currentPlayer);

        // 【步骤1.5】把玩家登记给规则：死亡计分 + 战绩表里名字显示"你"
        Health playerHealth = currentPlayer.GetComponentInChildren<Health>();
        if (playerHealth != null && matchRules != null) matchRules.RegisterCombatant(playerHealth, "你");
    }

    // 按对局配置里的 AI 格子生成 Bot
    void SpawnBotsFromConfig()
    {
        if (botPrefab == null)
        {
            Debug.LogError("Bot 预制体未设置！请在 GameManager 上拖入 Bot.prefab");
            return;
        }

        int botCount = 0;
        for (int i = 0; i < matchConfig.slots.Count; i++)
        {
            PlayerSlot slot = matchConfig.slots[i];
            if (!slot.isAI) continue;   // 只给"是AI"的格子生成；空位/你的位置跳过

            // 在该队伍的随机出生点附近落位（±2 米随机偏移，避免几个 Bot 叠在一起）
            Transform spawnPoint = GetRandomSpawnPoint(slot.teamId);
            Vector3 pos = spawnPoint.position + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
            GameObject bot = Instantiate(botPrefab, pos, spawnPoint.rotation);

            Health botHealth = bot.GetComponentInChildren<Health>();
            if (botHealth != null)
            {
                botHealth.SetTeamId(slot.teamId);   // 决定敌我 + 队服颜色

                // 【步骤1.5】战绩表里的 Bot 名字：沿用组队界面的编号（A2~A5 / B1~B5）
                string teamLetter = (slot.teamId == 0) ? "A" : "B";
                string botName = teamLetter + ((i % Constants.TeamSize) + 1);

                if (matchRules != null) matchRules.RegisterCombatant(botHealth, botName);
            }

            SetupRespawn(bot);   // 复用玩家那套复活逻辑：按队伍随机出生点
            botCount++;
        }

        Debug.Log("[对局] 按配置生成了 " + botCount + " 个 Bot");
    }

    // 给任何有 Health 的角色接上"按队伍随机出生点"的复活逻辑（玩家和 Bot 共用）
    void SetupRespawn(GameObject roleObj)
    {
        Health health = roleObj.GetComponentInChildren<Health>();
        if (health == null) return;
        // 第一次死亡要去的出生点
        health.SetRespawnPoint(GetRandomSpawnPoint(health.TeamId));
        // 每次复活后，先为"下一次死亡"抽一个新的随机出生点
        health.OnRespawned += () => health.SetRespawnPoint(GetRandomSpawnPoint(health.TeamId));
    }

    // 对局结束（MatchRules → MatchState 广播过来）
    // 对局结束（MatchRules → MatchState 广播过来）
    private void HandleMatchOver(int winnerTeamId)
    {
        Debug.Log("[对局] 结束！" + ((winnerTeamId == 0) ? "A队" : "B队") + " 获胜，冻结全场");
        Time.timeScale = 0f;   // 冻结全场：移动/动画/子弹全部停住（恢复在结算面板的返回按钮里）

        // 【修复】结算时自动收镜：最后一杀如果开着镜（尤其狙击），镜筒会盖住结算战绩
        WeaponController myWeapons = null;
        if (NetworkClient.localPlayer != null)
            myWeapons = NetworkClient.localPlayer.GetComponent<WeaponController>();   // 联机：本机自己的玩家
        else if (currentPlayer != null)
            myWeapons = currentPlayer.GetComponent<WeaponController>();               // 单机：GameManager 生成的玩家
        if (myWeapons != null) myWeapons.CloseAimForMatchOver();
    }

    public Transform GetRandomSpawnPoint(int teamId)
    {
        Transform[] targetSpawns = (teamId == 0) ? teamASpawnPoints : teamBSpawnPoints;
        if (targetSpawns == null || targetSpawns.Length == 0)
        {
            Debug.LogWarning("队伍" + teamId + "没有出生点，回退到队伍A");
            targetSpawns = teamASpawnPoints;
        }
        int randomIndex = Random.Range(0, targetSpawns.Length);
        return targetSpawns[randomIndex];
    }
}