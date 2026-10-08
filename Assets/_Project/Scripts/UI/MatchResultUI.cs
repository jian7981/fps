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
    public TMP_Text scoreText;       // 最终比分
    public Button returnButton;      // 【返回大厅】
    public Button returnRoomButton;          // 【阶段2.4-C】返回房间（联机时显示；每人自己点）
    public TMP_Text returnRoomButtonLabel;   // 返回房间按钮上的文字（可选）

    [Header("战绩表（左右两栏）")]
    public Transform leftColumn;     // A队 行容器（挂 Vertical Layout Group）
    public Transform rightColumn;    // B队 行容器
    public ScoreRowUI rowPrefab;     // 行预制体（ScoreRow.prefab，挂 ScoreRowUI 脚本）

    // 运行时生成的行（每边固定 5 行 = Constants.TeamSize）
    private readonly List<ScoreRowUI> leftRows = new List<ScoreRowUI>();
    private readonly List<ScoreRowUI> rightRows = new List<ScoreRowUI>();

    private void Start()
    {
        if (!CheckRefs()) return;

        resultPanel.SetActive(false);                      // 开局先藏起来
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
        if (scoreText == null) { Debug.LogError("MatchResultUI：scoreText 未赋值"); return false; }
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
    }

    // 对局结束：填面板并显示
    private void ShowResult(int winnerTeamId)
    {
        // 1. 胜负标题：你固定是 A 队第 1 格 → A 队赢就是"你赢了"
        titleText.text = (winnerTeamId == 0) ? "胜利！" : "失败…";

        // 2. 最终比分
        scoreText.text = "A队 " + matchState.TeamAScore + " : " + matchState.TeamBScore + " B队";

        // 3. 双方战绩表
        FillTeam(0, leftRows);
        FillTeam(1, rightRows);

        // 4. 显示面板 + 解锁鼠标（不解锁的话按钮点不了）
        resultPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
            }
            else
            {
                rows[i].SetEmpty();
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