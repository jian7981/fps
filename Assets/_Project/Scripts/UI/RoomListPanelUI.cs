using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 【阶段3-D】房间列表面板
// 数据来源：房间登记服务（GET /api/rooms）——经 LobbyManager.FetchRoomList 转一手，UI 不直接碰网络。
// 布局沿用好友栏那套：预先摆好 N 行（RoomRow），按数据多少显示/隐藏整行，装不下由 ScrollRect 滚动。
// 行内四要素：房间码 / 人数 / 状态（空闲·可加入·已满·对局中）/ 加入按钮。
//
// 挂载：大厅 Canvas（常驻物体）上 —— 不要挂在面板自己身上（面板会隐藏，脚本跟着停，RoomPanelUI 踩过这个坑）。
//      panelRoot 才指向那个要显隐的面板根物体。
public class RoomListPanelUI : MonoBehaviour
{
    [System.Serializable]
    public class RoomRow
    {
        public GameObject rowRoot;      // 整行根物体（按数据多少显示/隐藏）
        public TMP_Text codeText;       // 房间码（例：UVS7）
        public TMP_Text infoText;       // 人数 + 状态（例：2/4 人 · 可加入）
        public Button joinButton;       // 加入按钮（不可加入的房间置灰）

        [Header("【7.5】状态 chip（可选）")]
        public Image statusChip;
        public Sprite idleChipSprite;      // chip_neutral
        public Sprite joinableChipSprite;  // chip_green
        public Sprite fullChipSprite;      // chip_yellow
        public Sprite inGameChipSprite;    // chip_red
    }

    [Header("引用")]
    public LobbyManager lobbyManager;   // 大厅逻辑（拉列表 / 进房都从它走）
    public LobbyUI lobbyUI;             // 返回时用它把大厅主菜单显示回来
    public GameObject panelRoot;        // 面板根物体（显示/隐藏这一块）
    public Button refreshButton;        // 刷新按钮
    public Button closeButton;          // 返回按钮（回大厅主菜单）
    public TMP_Text hintText;           // 提示行：加载中 / 没有房间 / 服务不可用（可留空）
    public RoomRow[] rows;              // 预先摆好的行（6 行足够）

    private bool refreshing;

    private void Start()
    {
        // panelRoot 不允许留空：留空会把"挂脚本的物体（大厅 Canvas）"当成面板关掉
        if (panelRoot == null)
        {
            Debug.LogError("RoomListPanelUI：panelRoot 未赋值（不要留空，留空会误关掉挂脚本的物体）");
            return;
        }

        if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);
        if (closeButton != null) closeButton.onClick.AddListener(Close);

        panelRoot.SetActive(false);   // 一开始不显示（由 LobbyUI 调 Show 打开）
    }

    private void Update()
    {
        // Esc 关闭面板（和"加入房间"弹窗一致的操作习惯）
        if (panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    public void Show()
    {
        if (panelRoot == null) { Debug.LogError("RoomListPanelUI：panelRoot 未赋值，无法显示"); return; }
        panelRoot.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(false);
    }

    // 返回大厅主菜单：自己收起 + 让 LobbyUI 把主菜单显示回来（否则会停在空白界面）
    public void Close()
    {
        Hide();
        if (lobbyUI != null) lobbyUI.ShowMainPanel();
    }

    // 拉一次列表并刷新所有行
    public void Refresh()
    {
        if (refreshing) return;                       // 防止连点刷新叠请求
        if (lobbyManager == null) { SetHint("lobbyManager 未赋值"); return; }

        refreshing = true;
        SetHint("正在刷新房间列表…");

        lobbyManager.FetchRoomList(rooms =>
        {
            refreshing = false;

            if (rooms == null) { SetHint("房间登记服务不可用"); ShowRows(null); return; }
            if (rooms.Length == 0) { SetHint("当前没有房间（云机上请多开几个房间进程）"); ShowRows(null); return; }

            SetHint("共 " + rooms.Length + " 个房间");
            ShowRows(SortForDisplay(rooms));
        });
    }

    // 可加入的排前面；同一档里人多的排前面（尽量凑人）
    private static RoomInfo[] SortForDisplay(RoomInfo[] rooms)
    {
        List<RoomInfo> list = new List<RoomInfo>(rooms);
        list.Sort((a, b) =>
        {
            int ra = Joinable(a) ? 0 : 1;
            int rb = Joinable(b) ? 0 : 1;
            if (ra != rb) return ra.CompareTo(rb);
            return b.players.CompareTo(a.players);
        });
        return list.ToArray();
    }

    private static bool Joinable(RoomInfo r) => !r.inGame && r.players < r.maxPlayers;

    private void ShowRows(RoomInfo[] display)
    {
        int count = (display != null) ? display.Length : 0;

        for (int i = 0; i < rows.Length; i++)
        {
            RoomRow row = rows[i];
            if (row == null || row.rowRoot == null) continue;

            bool hasData = i < count;                 // 数据不够就隐藏整行
            row.rowRoot.SetActive(hasData);
            if (!hasData) continue;

            RoomInfo room = display[i];

            if (row.codeText != null) row.codeText.text = room.code;
            if (row.infoText != null)
                row.infoText.text = room.players + "/" + room.maxPlayers + " 人 · " + StateText(room);

            if (row.statusChip != null)
            {
                Sprite chip = StateChip(room, row);
                if (chip != null) row.statusChip.sprite = chip;
            }

            if (row.joinButton != null)
            {
                row.joinButton.onClick.RemoveAllListeners();   // 行会被反复填充，先清旧的监听避免叠加
                bool can = Joinable(room);
                row.joinButton.interactable = can;
                if (can) row.joinButton.onClick.AddListener(() => OnJoinClicked(room));
            }
        }
    }

    private static string StateText(RoomInfo r)
    {
        if (r.inGame) return "对局中";
        if (r.players >= r.maxPlayers) return "已满";
        return (r.players == 0) ? "空闲" : "可加入";
    }

    // 【7.5】状态 chip 底图
    private static Sprite StateChip(RoomInfo r, RoomRow row)
    {
        if (r.inGame) return row.inGameChipSprite;
        if (r.players >= r.maxPlayers) return row.fullChipSprite;
        return (r.players == 0) ? row.idleChipSprite : row.joinableChipSprite;
    }

    // 加入：地址已经在列表里拿到了，直接连（不用像"输房间码"那样再查一次）
    private void OnJoinClicked(RoomInfo room)
    {
        Hide();                                       // 先收起列表（随后会进入房间界面）
        lobbyManager.JoinListedRoom(room);
    }

    private void SetHint(string msg)
    {
        if (hintText != null) hintText.text = msg;
    }
}
