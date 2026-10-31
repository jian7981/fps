using System.Collections;           // IEnumerator（经验结算超时等待）
using System.Collections.Generic;   // List<>
using UnityEngine;
using UnityEngine.UI;               // Button
using UnityEngine.SceneManagement;  // LoadScene
using TMPro;                        // TMP_Text
using Shared;                       // Constants（每队行数 = Constants.TeamSize）
using Mirror;                       // 【阶段2.3-B2】NetworkServer / NetworkClient / NetworkManager（返回大厅用）

// ============ 结算面板（表现层）============
// 订阅 MatchState.OnMatchOver：弹面板、填胜负/比分/双方战绩表、解锁鼠标
// 挂载：map1 的 MatchCanvas 上（不要挂在 ResultPanel 自己身上——它会被隐藏，脚本也会跟着停）
public class MatchResultUI : MonoBehaviour
{
    [Header("基础引用")]
    public MatchState matchState;    // 拖 GameManager 物体
    public GameObject resultPanel;   // 结算面板根物体
    public TMP_Text titleText;       // 胜利！/ 失败…
    public GameObject victoryRoot;   // 【7.4】胜利装饰（burst / win tag），可留空
    public GameObject defeatRoot;    // 【7.4】失败装饰（defeat card），可留空
    public TMP_Text scoreText;       // 旧版合并比分（可留空）
    public TMP_Text scoreAText;      // 【7.4】A 队比分
    public TMP_Text scoreBText;      // 【7.4】B 队比分
    public Button returnButton;      // 【返回大厅】
    public Button returnRoomButton;          // 【阶段2.4-C】返回房间（联机时显示；每人自己点）
    public TMP_Text returnRoomButtonLabel;   // 返回房间按钮上的文字（可选）

    [Header("战绩表（左右两栏）")]
    public Transform leftColumn;     // A队 行容器（挂 Vertical Layout Group）
    public Transform rightColumn;    // B队 行容器
    public ScoreRowUI rowPrefab;     // 行预制体（ScoreRow.prefab，挂 ScoreRowUI 脚本）

    [Header("【阶段4-H】经验结算（可留空）")]
    [Tooltip("多行文本：\"+152 EXP\" / 分项明细 / 升级提示。建议在 ResultPanel 里加一个 TMP 文本")]
    public TMP_Text rewardText;
    [Tooltip("小字状态：\"经验结算中…\" / \"本局不计经验…\" / 失败原因")]
    public TMP_Text rewardStatusText;
    [Tooltip("【7.4】奖励卡里的经验条（Image Type = Filled / Horizontal / Left）")]
    public Image rewardExpFill;
    [Tooltip("等这么久还没收到经验结果 → 判为本局不计经验（人机局 / 未登录）")]
    public float rewardTimeout = 6f;

    // 运行时生成的行（每边固定 5 行 = Constants.TeamSize）
    private readonly List<ScoreRowUI> leftRows = new List<ScoreRowUI>();
    private readonly List<ScoreRowUI> rightRows = new List<ScoreRowUI>();

    // 【阶段4-H】本机玩家（订阅经验结算用）+ 超时协程
    private Health localHealth;
    private Coroutine rewardTimeoutRoutine;

    // 【7.4】本机在战绩表里的显示名：登录昵称，或未登录时的 "玩家 N"
    private string localDisplayName;

    private void Start()
    {
        if (!CheckRefs()) return;

        resultPanel.SetActive(false);                      // 开局先藏起来
        SetRewardVisible(false);                           // 【修复】经验文本在 MatchCanvas 下、不在 ResultPanel 里 → 必须单独藏
        if (victoryRoot != null) victoryRoot.SetActive(false);
        if (defeatRoot != null) defeatRoot.SetActive(false);
        if (rewardExpFill != null) rewardExpFill.fillAmount = 0f;
        returnButton.onClick.AddListener(ReturnToLobby);   // 绑定按钮（代码绑，OnClick 留空）

        // 【阶段2.4-C】返回房间：只有联机时显示（单机没有"房间"概念）
        if (returnRoomButton != null)
        {
            returnRoomButton.onClick.AddListener(ReturnToRoom);
            returnRoomButton.gameObject.SetActive(NetworkServer.active || NetworkClient.active);
        }

        CreateRows(leftColumn, leftRows);                  // 预生成左栏 5 行
        CreateRows(rightColumn, rightRows);                // 预生成右栏 5 行
    }

    private bool CheckRefs()
    {
        if (matchState == null) { Debug.LogError("MatchResultUI：matchState 未赋值"); return false; }
        if (resultPanel == null) { Debug.LogError("MatchResultUI：resultPanel 未赋值"); return false; }
        if (titleText == null) { Debug.LogError("MatchResultUI：titleText 未赋值"); return false; }
        if (scoreText == null && scoreAText == null && scoreBText == null)
        { Debug.LogError("MatchResultUI：scoreText / scoreAText / scoreBText 至少赋值一个"); return false; }
        if (returnButton == null) { Debug.LogError("MatchResultUI：returnButton 未赋值"); return false; }
        if (leftColumn == null) { Debug.LogError("MatchResultUI：leftColumn 未赋值"); return false; }
        if (rightColumn == null) { Debug.LogError("MatchResultUI：rightColumn 未赋值"); return false; }
        if (rowPrefab == null) { Debug.LogError("MatchResultUI：rowPrefab 未赋值（拖 Project 里的 ScoreRow.prefab）"); return false; }
        return true;
    }

    // 生成一栏的行（先全部填"空位"，对局结束再填真数据）
    private void CreateRows(Transform parent, List<ScoreRowUI> rows)
    {
        for (int i = 0; i < Constants.TeamSize; i++)
        {
            ScoreRowUI row = Instantiate(rowPrefab, parent);   // 复制一份行塞进容器（布局组自动排版）
            row.gameObject.name = "Row_" + i;
            row.SetEmpty();
            rows.Add(row);
        }
    }

    private void OnEnable()
    {
        if (matchState != null) matchState.OnMatchOver += ShowResult;
    }

    private void OnDisable()
    {
        if (matchState != null) matchState.OnMatchOver -= ShowResult;   // 配对取消订阅

        // 【阶段4-H】经验结算的订阅/延时也要收回（场景卸载时事件源跟着一起销毁，留着无害但不对称）
        if (localHealth != null)
        {
            localHealth.OnMatchReward -= HandleReward;
            localHealth.OnMatchRewardFailed -= HandleRewardFailed;
        }
        StopRewardTimeout();
    }

    // 对局结束：填面板并显示
    private void ShowResult(int winnerTeamId)
    {
        // 1. 胜负标题：【阶段2.5 修正】按"你自己的队伍"判定（联机分队后不再是写死的 A 队）
        //    联机：本机玩家对象；单机：GameManager 记的本地玩家
        GameObject myPlayer = null;
        if (NetworkClient.localPlayer != null) myPlayer = NetworkClient.localPlayer.gameObject;
        else
        {
            GameManager gm = FindObjectOfType<GameManager>();
            if (gm != null) myPlayer = gm.CurrentPlayer;
        }

        int myTeam = 0;
        Health myHealth = null;
        if (myPlayer != null)
        {
            myHealth = myPlayer.GetComponentInChildren<Health>();
            if (myHealth != null) myTeam = myHealth.TeamId;
        }

        bool won = (winnerTeamId == myTeam);
        titleText.text = won ? "胜利" : "失败";
        if (victoryRoot != null) victoryRoot.SetActive(won);
        if (defeatRoot != null) defeatRoot.SetActive(!won);

        // 【7.4】本机战绩行高亮：登录昵称，或未登录时的 "玩家 N"
        localDisplayName = !string.IsNullOrEmpty(AuthService.CurrentNickname)
            ? AuthService.CurrentNickname
            : (myHealth != null && myHealth.DisplayIndex > 0 ? "玩家 " + myHealth.DisplayIndex : null);

        // 【阶段4-H】经验结算：结果由服务器稍后 TargetRpc 发过来（这里先把状态挂好）
        BeginReward(myHealth);

        // 2. 最终比分
        if (scoreAText != null) scoreAText.text = matchState.TeamAScore.ToString();
        if (scoreBText != null) scoreBText.text = matchState.TeamBScore.ToString();
        if (scoreText != null)
            scoreText.text = "A队 " + matchState.TeamAScore + " : " + matchState.TeamBScore + " B队";

        // 3. 双方战绩表
        FillTeam(0, leftRows);
        FillTeam(1, rightRows);

        // 4. 显示面板 + 解锁鼠标（不解锁的话按钮点不了）
        resultPanel.SetActive(true);
        SetRewardVisible(true);   // 经验文本跟着结算面板一起显示（它们不在 ResultPanel 层级下）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 【修复】经验文本显隐
    // 场景里 RewardText / RewardStatusText 是 **MatchCanvas 的直接子物体**（和 ResultPanel 平级），
    // 所以 Start 里只关 resultPanel 不会关掉它们 —— 实测表现为对局中一直挂着两行文本。
    // 这里跟着结算面板单独开关；只控制 SetActive，不动层级与布局。
    private void SetRewardVisible(bool visible)
    {
        if (rewardText != null) rewardText.gameObject.SetActive(visible);
        if (rewardStatusText != null) rewardStatusText.gameObject.SetActive(visible);
    }

    // ==================== 【阶段4-H】经验结算 ====================
    // 数据不是本地算的：服务器替每个玩家上报后端 → 后端算经验/升级 → TargetRpc 发回本人。
    // 所以面板要先进入"结算中…"，结果到了再替换（HTTP 往返通常几十~几百毫秒）。
    private void BeginReward(Health health)
    {
        localHealth = health;
        if (rewardText != null) rewardText.text = "";

        if (health == null)
        {
            SetRewardStatus("");
            return;
        }

        // 先退订再订阅：ShowResult 正常只来一次，但重复调用也不该叠加两份回调
        health.OnMatchReward -= HandleReward;
        health.OnMatchReward += HandleReward;
        health.OnMatchRewardFailed -= HandleRewardFailed;
        health.OnMatchRewardFailed += HandleRewardFailed;

        // 结果可能比结算面板更早到（例如网络极快）→ 直接显示现成的
        if (health.LastMatchReward != null)
        {
            HandleReward(health.LastMatchReward);
            return;
        }

        // 没登录 / 没配账号服务：根本不会有上报，别让玩家等一个永远不会来的结果
        if (!AuthService.HasService || !AuthService.IsLoggedIn)
        {
            SetRewardStatus("未登录，本局不计经验");
            return;
        }

        SetRewardStatus("经验结算中…");

        // ⚠ 不能用 Invoke：对局结束时 GameManager 把 Time.timeScale 冻成 0，
        //   而 Invoke 走的是**缩放时间**，永远等不到。这里用 realtime 协程。
        StopRewardTimeout();
        rewardTimeoutRoutine = StartCoroutine(RewardTimeoutRoutine());
    }

    private IEnumerator RewardTimeoutRoutine()
    {
        yield return new WaitForSecondsRealtime(rewardTimeout);
        rewardTimeoutRoutine = null;

        // 常见原因：人机局（真人 < 2，服务器整局不上报）或未登录
        SetRewardStatus("本局不计经验（人机局或未登录）");
    }

    private void StopRewardTimeout()
    {
        if (rewardTimeoutRoutine != null)
        {
            StopCoroutine(rewardTimeoutRoutine);
            rewardTimeoutRoutine = null;
        }
    }

    private void HandleReward(MatchReward reward)
    {
        if (reward == null) return;
        StopRewardTimeout();
        SetRewardStatus("");

        if (rewardExpFill != null)
            rewardExpFill.fillAmount = (reward.expToNext > 0)
                ? Mathf.Clamp01((float)reward.exp / reward.expToNext)
                : 1f;

        if (rewardText == null) return;

        // 同一局重复上报（反复点返回、超时重发）→ 后端幂等拦下，这里明确告诉玩家没重复加
        if (reward.duplicated)
        {
            rewardText.text = "本局经验之前已结算过";
            return;
        }

        string detail = "+" + reward.expGain + " EXP";
        if (reward.capped) detail += "（已达单局上限）";

        // 分项只列非 0 的，避免一整排 "+0"
        string parts = "参与 +" + reward.baseExp;
        if (reward.killExp > 0) parts += "　击杀 +" + reward.killExp;
        if (reward.assistExp > 0) parts += "　助攻 +" + reward.assistExp;
        if (reward.winExp > 0) parts += "　胜利 +" + reward.winExp;
        if (reward.dailyExp > 0) parts += "　首胜 +" + reward.dailyExp;

        // 升级了就把"Lv.x → Lv.y"单独一行，比只显示"Lv.y"更醒目
        string levelLine = (reward.levelAfter > reward.levelBefore)
            ? "\n升级！Lv." + reward.levelBefore + " → Lv." + reward.levelAfter
            : "\nLv." + reward.levelAfter + "　" + reward.exp + " / " + reward.expToNext;

        rewardText.text = detail + "\n" + parts + levelLine;
    }

    private void HandleRewardFailed(string error)
    {
        StopRewardTimeout();
        SetRewardStatus(string.IsNullOrEmpty(error) ? "经验结算失败" : "经验结算失败：" + error);
    }

    private void SetRewardStatus(string msg)
    {
        if (rewardStatusText != null) rewardStatusText.text = msg;
    }

    // 把某一队的战绩填进对应的一栏（登记顺序 = 组队界面的格子顺序）
    private void FillTeam(int teamId, List<ScoreRowUI> rows)
    {
        // 先从对局状态里筛出这队的人
        List<CombatantStats> teamStats = new List<CombatantStats>();
        for (int i = 0; i < matchState.AllStats.Count; i++)
        {
            if (matchState.AllStats[i].teamId == teamId) teamStats.Add(matchState.AllStats[i]);
        }

        // 逐行填；这队没那么多人就显示"空位"
        for (int i = 0; i < rows.Count; i++)
        {
            if (i < teamStats.Count)
            {
                CombatantStats s = teamStats[i];
                rows[i].Set(s.displayName, s.kills, s.deaths, s.assists);
                rows[i].SetHighlight(!string.IsNullOrEmpty(localDisplayName) && s.displayName == localDisplayName);
            }
            else
            {
                rows[i].SetEmpty();
                rows[i].SetHighlight(false);
            }
        }
    }

    // 【返回大厅】按钮点击
    // 【阶段2.3-B2】联机适配（各端各自回大厅，靠 Mirror 的 offlineScene=Lobby 自动完成）：
    //   主机：StopHost —— 停掉会话；客机们会收到断开，Mirror 自动把它们送回 Lobby
    //   客机：StopClient —— 自己断开，同样自动回 Lobby
    //   纯单机：和原版一样直接 LoadScene（场景重载 → 比分/战绩自动清零）
    // 【返回大厅】按钮点击
    // 【阶段2.4-B2】房间流程：回房间 = 服务器切场景（所有客户端自动跟随、连接不断、准备状态自动重置）
    //   主机：ServerChangeScene(RoomScene) → 大家一起回 Lobby 房间，直接"再来一局"
    //   客机：自己切不了场景，等房主操作（这里给个提示）
    //   纯单机：和原版一样直接 LoadScene（场景重载 → 比分/战绩自动清零）
    // 【返回大厅】按钮点击（联机 = 离场：主机解散房间、客机退出房间；单机 = 重新加载大厅）
    private void Update()
    {
        // 【阶段2.4-C】自己选了"返回房间"之后：按钮变灰显示"等待其他玩家…"
        if (returnRoomButton == null || !returnRoomButton.gameObject.activeSelf || !returnRoomButton.interactable) return;
        PlayerMatchActions actions = (NetworkClient.localPlayer != null)
            ? NetworkClient.localPlayer.GetComponent<PlayerMatchActions>() : null;
        if (actions == null || !actions.wantsNextRound) return;

        returnRoomButton.interactable = false;
        if (returnRoomButtonLabel != null) returnRoomButtonLabel.text = "已选择，等待其他玩家…";
    }

    // 【返回大厅】按钮点击（联机 = 离场：主机解散房间、客机退出房间；单机 = 重新加载大厅）
    private void ReturnToLobby()
    {
        Time.timeScale = 1f;   // 【关键】对局结束时冻结了时间（=0），回来前必须恢复

        if (NetworkServer.active)          // 主机：解散房间
        {
            NetworkManager.singleton.StopHost();
        }
        else if (NetworkClient.active)     // 客机：退出房间（Mirror 自动回 offlineScene=Lobby）
        {
            Debug.Log("[房间] 已退出房间，返回大厅");
            NetworkManager.singleton.StopClient();
        }
        else                               // 纯单机
        {
            SceneManager.LoadScene(Constants.LobbySceneName);
        }
    }

    // 【阶段2.4-C】返回房间：每人自己点、自己表态；全员都点了服务器才把全队切回房间
    private void ReturnToRoom()
    {
        Time.timeScale = 1f;
        PlayerMatchActions actions = (NetworkClient.localPlayer != null)
            ? NetworkClient.localPlayer.GetComponent<PlayerMatchActions>() : null;
        if (actions != null) actions.CmdRequestReturnToRoom();
        else Debug.LogWarning("[房间] 找不到本机玩家，无法请求返回房间");
    }
}