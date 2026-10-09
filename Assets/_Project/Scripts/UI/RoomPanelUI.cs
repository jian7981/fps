using System;                       // [Serializable]
using UnityEngine;
using UnityEngine.UI;               // Button
using TMPro;                        // TMP_Text
using Mirror;
using Shared;                       // Constants（总人数显示）

// 【阶段2.4-C】房间面板（王者荣耀风格的组队房间）
// 布局：上排 = A队 5 个槽位；下排 = B队 5 个槽位；右侧好友栏（FriendPanelUI 单独管）。
// 规则：点任意空位 = 换到那个位置（跨队换位 = 队伍一起变，服务器校验空位）；
//       点"准备/取消准备"；全体准备后由房主点"开始游戏"。
// 显示时机：处在网络会话里显示；断开后自动隐藏并回大厅主菜单。
// 挂载：大厅 Canvas（常驻物体）上——千万别挂在 RoomPanel 自己身上（它会隐藏，脚本会跟着停）
public class RoomPanelUI : MonoBehaviour
{
    // 单个槽位的引用（手动摆 10 个，按顺序拖进 slots）
    [Serializable]
    public class RoomSlotUI
    {
        public Button button;        // 槽位本体（可点击）
        public TMP_Text nameText;    // 玩家名 / "空位"
        public TMP_Text stateText;   // "已准备 / 未准备 / 点我换到这里"
    }

    [Header("引用")]
    public GameObject roomPanel;      // 面板根物体
    public LobbyUI lobbyUI;           // 进出房间时切换大厅面板显隐
    public RoomSlotUI[] slots;        // 10 个槽位：0~4 = A队1~5号位，5~9 = B队1~5号位
    public TMP_Text playersText;      // 提示语（人数/准备情况/下一步做什么）
    public Button readyButton;        // 准备/取消准备
    public TMP_Text readyButtonLabel; // 按钮上的文字
    public Button startButton;        // 【仅房主】全员准备后出现
    public Button leaveButton;        // 离开房间
    public Button inviteButton;       // 旧的"邀请好友"大按钮（可留空——功能已移到右侧好友栏）

    private float nextRefresh;

    private void Start()
    {
        if (roomPanel == null) roomPanel = gameObject;

        if (readyButton != null) readyButton.onClick.AddListener(ToggleReady);
        if (startButton != null) { startButton.onClick.AddListener(StartGame); startButton.gameObject.SetActive(false); }
        if (leaveButton != null) leaveButton.onClick.AddListener(LeaveRoom);
        if (inviteButton != null) inviteButton.interactable = false;   // 旧按钮：阶段3才开放（建议直接用右侧好友栏代替）

        // 槽位点击：点击第 i 个槽位 → 尝试换到该位置（任意队空位都行，服务器还会再校验一次）
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int slotIndex = i;   // 闭包捕获：必须用局部变量，否则每个按钮都会拿到同一个 i
                if (slots[i] != null && slots[i].button != null)
                    slots[i].button.onClick.AddListener(() => OnSlotClicked(slotIndex));
            }
        }

        // 初始可见性 = 当前是否有网络会话（在房间里还要把大厅各面板藏掉——比如"返回房间"重载大厅时）
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
        if (room == null) return;

        // 1. 构建"槽位 → 玩家"映射
        RoomPlayer[] bySlot = new RoomPlayer[10];
        RoomPlayer myRoomPlayer = null;
        int count = 0, readyCount = 0;
        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp == null) continue;
            int slotIndex = (rp.teamId == 0 ? 0 : 5) + Mathf.Clamp(rp.seatIndex, 0, 4);
            bySlot[slotIndex] = rp;
            count++;
            if (rp.readyToBegin) readyCount++;
            if (rp.isLocalPlayer) myRoomPlayer = rp;
        }

        // 2. 逐槽位刷新（有人：名字+状态；空位：任意队都可点换位）
        if (slots != null)
        {
            for (int i = 0; i < slots.Length && i < 10; i++)
            {
                RoomSlotUI slot = slots[i];
                if (slot == null || slot.button == null) continue;

                RoomPlayer occupant = bySlot[i];
                if (occupant != null)
                {
                    if (slot.nameText != null) slot.nameText.text = occupant.displayName;
                    if (slot.stateText != null) slot.stateText.text = occupant.readyToBegin ? "已准备" : "未准备";
                    slot.button.interactable = false;   // 有人的位置不响应点击（交换请求以后再做）
                }
                else
                {
                    if (slot.nameText != null) slot.nameText.text = "空位";
                    if (slot.stateText != null) slot.stateText.text = "点我换到这里";
                    slot.button.interactable = (myRoomPlayer != null);   // 空位都能点（跨队换位）
                }
            }
        }

        // 3. 提示语
        if (playersText != null)
        {
            string text = "房间玩家：" + count + " / " + Constants.TotalSlotCount + "\n\n";
            if (count < room.minPlayers)
                text += "至少 " + room.minPlayers + " 人、全员准备后由房主开始。";
            else if (!room.allPlayersReady)
                text += "等待全员准备…（已准备 " + readyCount + " / " + count + "）";
            else
                text += NetworkServer.active ? "全员已准备——点“开始游戏”进入对局！"
                                             : "全员已准备，等待房主开始…";
            text += "\n（点击任意空位可以换过去，换位会同时改变你的队伍）";
            playersText.text = text;
        }

        // 4. 准备按钮：文字跟着自己的状态走
        if (readyButton != null)
        {
            bool ready = myRoomPlayer != null && myRoomPlayer.readyToBegin;
            if (readyButtonLabel != null) readyButtonLabel.text = ready ? "取消准备" : "准备";
            readyButton.interactable = myRoomPlayer != null;
        }

        // 5. 开始按钮：只有房主、且全员准备时出现
        if (startButton != null)
        {
            bool showStart = NetworkServer.active && room.allPlayersReady;
            if (startButton.gameObject.activeSelf != showStart) startButton.gameObject.SetActive(showStart);
        }
    }

    // 点击槽位：请求换到该位置（服务器端 CmdChangeSeat 会校验"必须为空位"）
    private void OnSlotClicked(int slotIndex)
    {
        RoomPlayer mine = FindMyRoomPlayer();
        if (mine != null) mine.CmdChangeSeat(slotIndex);
    }

    // 房主开始对局
    private void StartGame()
    {
        GameNetworkManager room = NetworkManager.singleton as GameNetworkManager;
        if (room == null || !room.allPlayersReady) return;
        Debug.Log("[房间] 房主开始对局");
        room.ServerChangeScene(room.GameplayScene);
    }

    // 准备/取消准备
    private void ToggleReady()
    {
        RoomPlayer myRoomPlayer = FindMyRoomPlayer();
        if (myRoomPlayer != null) myRoomPlayer.CmdChangeReadyState(!myRoomPlayer.readyToBegin);
    }

    // 离开房间：主机 = 解散；客机 = 自己退出回大厅
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