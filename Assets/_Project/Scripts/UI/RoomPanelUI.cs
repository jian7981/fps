using System.Collections.Generic;   // List<>
using UnityEngine;
using UnityEngine.UI;               // Button
using TMPro;                        // TMP_Text
using Mirror;
using Shared;                       // Constants（人数上限显示用）

// 【阶段2.4-C】房间面板
// 显示：房间里的人（名字 + 队伍 + 准备状态）；提供 准备/取消准备、开始游戏（仅房主）、离开房间、邀请好友（占位）。
// 开局规则：全员准备后 —— 由房主点"开始游戏"（不再是自动开始）。
// 显示时机：处在网络会话里显示；断开后自动隐藏，回到大厅主菜单。
// 挂载：Lobby 场景 Canvas 下的 RoomPanel 物体上
public class RoomPanelUI : MonoBehaviour
{
    [Header("引用")]
    public GameObject roomPanel;      // 面板根物体（留空 = 就挂在本物体上）
    public TMP_Text playersText;      // 玩家列表 + 提示语（一个多行文本）
    public Button readyButton;        // 准备/取消准备
    public TMP_Text readyButtonLabel; // 按钮上的文字
    public Button startButton;        // 【仅房主】全员准备后出现，点它进入对局
    public Button leaveButton;        // 离开房间（主机=解散、客机=退出）
    public Button inviteButton;       // 邀请好友（阶段3上线，先禁用占位）
    public LobbyUI lobbyUI;           // 【阶段2.4-C】进/出房间时用它切换"大厅面板"的显隐

    private float nextRefresh;

    private void Start()
    {
        if (roomPanel == null) roomPanel = gameObject;
        if (readyButton != null) readyButton.onClick.AddListener(ToggleReady);
        if (startButton != null) { startButton.onClick.AddListener(StartGame); startButton.gameObject.SetActive(false); }
        if (leaveButton != null) leaveButton.onClick.AddListener(LeaveRoom);
        if (inviteButton != null) inviteButton.interactable = false;
        // 初始可见性 = 当前是否有网络会话
        // （在房间里还要把大厅各面板藏掉——比如"返回房间"重载大厅时，会话还在）
        bool inSession = NetworkServer.active || NetworkClient.active;
        roomPanel.SetActive(inSession);
        if (inSession && lobbyUI != null) lobbyUI.HideAllPanels();
    }

    private void Update()
    {
        bool inSession = NetworkServer.active || NetworkClient.active;
        if (roomPanel.activeSelf != inSession)
        {
            roomPanel.SetActive(inSession);
            if (lobbyUI != null)
            {
                if (inSession) lobbyUI.HideAllPanels();   // 进房：藏掉主菜单等
                else lobbyUI.ShowMainPanel();             // 出房：回到主菜单
            }
        }
        if (!inSession) return;

        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;   // 每 0.25 秒刷新一次（简单可靠）
        RefreshPanel();
    }

    private void RefreshPanel()
    {
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null || playersText == null) return;

        // 收集房间里的人（按显示名排序；房间最多 10 人，够用）
        List<RoomPlayer> list = new List<RoomPlayer>();
        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp != null) list.Add(rp);
        }
        list.Sort((a, b) => string.Compare(a.displayName, b.displayName));

        string text = "房间玩家（" + list.Count + " / " + Constants.TotalSlotCount + "）\n\n";
        int readyCount = 0;
        RoomPlayer myRoomPlayer = null;
        for (int i = 0; i < list.Count; i++)
        {
            RoomPlayer rp = list[i];
            if (rp.readyToBegin) readyCount++;
            if (rp.isLocalPlayer) myRoomPlayer = rp;

            string team = (rp.teamId == 0) ? "A队" : "B队";
            string state = rp.readyToBegin ? "已准备" : "未准备";
            text += rp.displayName + "　" + team + "　" + state + "\n";
        }

        // 提示语：说明现阶段在等什么
        if (list.Count < room.minPlayers)
            text += "\n至少 " + room.minPlayers + " 人、全员准备后由房主开始。";
        else if (!room.allPlayersReady)
            text += "\n等待全员准备…（已准备 " + readyCount + " / " + list.Count + "）";
        else
            text += NetworkServer.active ? "\n全员已准备——点“开始游戏”进入对局！"
                                         : "\n全员已准备，等待房主开始…";
        playersText.text = text;

        // 准备按钮：文字跟着自己的状态走
        if (readyButton != null)
        {
            bool ready = myRoomPlayer != null && myRoomPlayer.readyToBegin;
            if (readyButtonLabel != null) readyButtonLabel.text = ready ? "取消准备" : "准备";
            readyButton.interactable = myRoomPlayer != null;
        }

        // 开始按钮：只有房主、且全员准备时出现（【阶段2.4-C】房主手动开始）
        if (startButton != null)
        {
            bool showStart = NetworkServer.active && room.allPlayersReady;
            if (startButton.gameObject.activeSelf != showStart) startButton.gameObject.SetActive(showStart);
        }
    }

    // 房主开始对局
    private void StartGame()
    {
        GameNetworkManager room = NetworkManager.singleton as GameNetworkManager;
        if (room == null || !room.allPlayersReady) return;
        Debug.Log("[房间] 房主开始对局");
        room.ServerChangeScene(room.GameplayScene);
    }

    // 准备/取消准备（命令发给自己那个房间玩家对象，由服务器改）
    private void ToggleReady()
    {
        RoomPlayer myRoomPlayer = FindMyRoomPlayer();
        if (myRoomPlayer != null) myRoomPlayer.CmdChangeReadyState(!myRoomPlayer.readyToBegin);
    }

    // 离开房间：主机 = 解散（所有人断开、各自自动回大厅）；客机 = 自己退出回大厅
    private void LeaveRoom()
    {
        Time.timeScale = 1f;
        Debug.Log("[房间] 离开房间");
        if (NetworkServer.active) NetworkManager.singleton.StopHost();
        else if (NetworkClient.active) NetworkManager.singleton.StopClient();
    }

    private RoomPlayer FindMyRoomPlayer()
    {
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null) return null;
        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp != null && rp.isLocalPlayer) return rp;
        }
        return null;
    }
}