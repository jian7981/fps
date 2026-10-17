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
    public TMP_Text roomCodeText;     // 【阶段3-房间码】显示本机房间码（发给朋友即可加入）
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

        // 初始可见性 = "当前有网络会话" 且 "登录界面没挡在前面"
        // 【Bug 修复】返回大厅时如果账号登录界面挡在前面，房间面板绝不能再露出来（会盖到登录界面上）
        bool inSession = NetworkServer.active || NetworkClient.active;
        roomPanel.SetActive(inSession && !AuthGateUp());
        if (inSession && !AuthGateUp() && lobbyUI != null) lobbyUI.HideAllPanels();
    }

    // 【Bug 修复】"登录界面正挡在大厅前面" = 配了账号服务、但还没登录。
    // 这种时候房间面板（以及大厅各面板）都不该露头，否则会盖在登录界面上。
    private static bool AuthGateUp() => AuthService.HasService && !AuthService.IsLoggedIn;

    private void Update()
    {
        bool inSession = NetworkServer.active || NetworkClient.active;
        bool gateUp = AuthGateUp();
        bool showRoom = inSession && !gateUp;

        if (roomPanel.activeSelf != showRoom)
        {
            roomPanel.SetActive(showRoom);
            if (lobbyUI != null)
            {
                if (showRoom) lobbyUI.HideAllPanels();          // 进房：藏掉主菜单等
                else if (!gateUp) lobbyUI.ShowMainPanel();      // 出房：回到主菜单（登录界面挡着时别抢）
            }
        }
        if (!showRoom) return;

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

        // 【修复，云模式】Mirror 的 room.allPlayersReady 不是同步字段（只在服务器上计算）→ 客户端永远是 false，
        // 云模式下房主是普通客户端，会看不到"开始游戏"。这里用同步过来的房间数据自己算一遍同样规则：人数达标 + 全员准备。
        bool allReady = count >= room.minPlayers && readyCount == count;

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
                    if (slot.nameText != null)
                    {
                        string shownName = occupant.displayName;
                        if (occupant.isRoomOwner) shownName += "（房主）";   // 【阶段3】一眼看到谁是房主
                        slot.nameText.text = shownName;
                    }
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
            else if (!allReady)   // 【修复】同上：客户端不能用 room.allPlayersReady（不会同步）
                text += "等待全员准备…（已准备 " + readyCount + " / " + count + "）";
            else
            {
                bool iAmOwner = myRoomPlayer != null && myRoomPlayer.isRoomOwner;   // 【阶段3】房主是身份标记，不再看谁跑服务器
                text += iAmOwner ? "全员已准备——点“开始游戏”进入对局！"
                                 : "全员已准备，等待房主开始…";
            }
            text += "\n（点击任意空位可以换过去，换位会同时改变你的队伍）";
            playersText.text = text;
        }

        // 【阶段3-房间码】显示本机房间码：服务器登记成功后经 SyncVar 下发；
        // 还没拿到时显示"生成中…"—— 只是显示，绝不阻塞进房或开局
        if (roomCodeText != null)
        {
            string code = (myRoomPlayer != null) ? myRoomPlayer.roomCode : null;
            roomCodeText.text = string.IsNullOrEmpty(code)
                ? "房间码：生成中…"
                : "房间码：" + code + "　（发给朋友即可加入）";
        }

        // 4. 准备按钮：文字跟着自己的状态走
        if (readyButton != null)
        {
            bool ready = myRoomPlayer != null && myRoomPlayer.readyToBegin;
            if (readyButtonLabel != null) readyButtonLabel.text = ready ? "取消准备" : "准备";
            readyButton.interactable = myRoomPlayer != null;
        }

        // 5. 开始按钮：只有"房主"（身份标记）、且全员准备时出现
        // 【阶段3 改动】原来判断的是 NetworkServer.active（跑服务器的那台机器）→ 云服务器（专用服）上没人能点。
        // 现在房主是逻辑身份：局域网 = 第一个进房的人（=主机玩家，行为和以前完全一致）；云服务器 = 第一个进房的玩家。
        if (startButton != null)
        {
            bool showStart = myRoomPlayer != null && myRoomPlayer.isRoomOwner && allReady;   // 【修复】用本地算的 allReady（room.allPlayersReady 不会同步到客户端）
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
        RoomPlayer mine = FindMyRoomPlayer();
        if (mine == null || !mine.isRoomOwner) return;   // 只有房主能开

        if (NetworkServer.active)                        // 局域网主机模式：房主就是服务器本机 → 直接切
        {
            GameNetworkManager room = NetworkManager.singleton as GameNetworkManager;
            if (room == null || !room.allPlayersReady) return;
            Debug.Log("[房间] 房主开始对局（本机）");
            room.ServerChangeScene(room.GameplayScene);
        }
        else                                             // 云服务器：房主是普通玩家 → 发请求给服务器校验后切换
        {
            Debug.Log("[房间] 房主请求开始对局（上报服务器）");
            mine.CmdRequestStartGame();
        }
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