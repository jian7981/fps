using UnityEngine;
using UnityEngine.UI;

// ============ 大厅 UI 总控（四个界面的切换开关）============
// 四个界面：MainMenu 主界面 / MapPanel 地图选择 / TeamPanel 组队 / SettingsPanel 设置
// 挂载：挂在 Lobby 场景 Canvas 上
public class LobbyUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyManager lobbyManager;    // 大厅逻辑管理器

    [Header("四个界面")]
    public GameObject mainMenu;          // 大厅主界面（选择地图 + 组队 + 设置）
    public GameObject mapPanel;          // 地图选择界面
    public GameObject teamPanel;         // 组队界面
    public GameObject settingsPanel;     // 【新增】设置界面

    [Header("主界面上的按钮")]
    public Button selectMapButton;       // 【选择地图】按钮
    public Button teamButton;            // 【组队】按钮
    public Button settingsButton;        // 【新增】【设置】按钮

    [Header("联机入口（【阶段2.4-C】）")]
    public Button soloPracticeButton;         // 单人练习（打开原组队界面 = 单机流程）
    public Button quickMatchButton;           // 【扩展】快速匹配（公网版开放）
    public Button joinRoomButton;             // 加入房间（点击打开输 IP 弹窗）
    public JoinRoomDialogUI joinRoomDialog;   // 【扩展】加入房间弹窗（挂在 Canvas 上）

    private void Start()
    {
        if (!CheckRefs()) return;

        // 1. 绑定主界面按钮（都是代码绑，Inspector 里的 OnClick 留空）
        selectMapButton.onClick.AddListener(OpenMapPanel);        // 点"选择地图" → 打开地图选择界面
        teamButton.onClick.AddListener(lobbyManager.CreateRoom);  // 【改，2.4-C】点"组队" = 自动创建房间（StartHost）
        if (soloPracticeButton != null) soloPracticeButton.onClick.AddListener(OpenTeamPanel);   // 单人练习 = 原组队界面
        if (quickMatchButton != null) quickMatchButton.onClick.AddListener(OnQuickMatchClicked); // 【扩展】快速匹配
        if (joinRoomButton != null) joinRoomButton.onClick.AddListener(OnJoinRoomClicked);       // 加入房间（打开弹窗）
        settingsButton.onClick.AddListener(OpenSettingsPanel);    // 【新增】点"设置" → 打开设置界面

        // 2. 游戏一开始只显示主界面
        ShowMainPanel();
    }

    // 【阶段2.4-C 扩展】点"加入房间" → 打开输入 IP 的弹窗（不再在主界面直接摆输入框）
    private void OnJoinRoomClicked()
    {
        if (joinRoomDialog != null) joinRoomDialog.Show();
        else Debug.LogWarning("[大厅] 加入房间弹窗未挂载（joinRoomDialog）");
    }

    // 【阶段2.4-C 扩展】快速匹配：公网阶段（阶段3）接服务器撮合，自动进入现有房间；现在只给提示
    private void OnQuickMatchClicked()
    {
        Debug.Log("[大厅] 快速匹配将在公网版开放（阶段3：自动加入其他玩家的房间）");
    }

    // 打开地图选择界面
    public void OpenMapPanel()
    {
        mainMenu.SetActive(false);
        mapPanel.SetActive(true);
        teamPanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    // 打开组队界面
    public void OpenTeamPanel()
    {
        mainMenu.SetActive(false);
        mapPanel.SetActive(false);
        teamPanel.SetActive(true);
        settingsPanel.SetActive(false);
    }

    // 【新增】打开设置界面
    public void OpenSettingsPanel()
    {
        mainMenu.SetActive(false);
        mapPanel.SetActive(false);
        teamPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    // 回到大厅主界面（地图/组队/设置面板的"返回"按钮都调它）
    public void ShowMainPanel()
    {
        mainMenu.SetActive(true);
        mapPanel.SetActive(false);
        teamPanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    // 【阶段2.4-C】藏掉大厅的所有面板（进房间时用——房间面板显示期间不该看到主菜单）
    public void HideAllPanels()
    {
        mainMenu.SetActive(false);
        mapPanel.SetActive(false);
        teamPanel.SetActive(false);
        settingsPanel.SetActive(false);
    }

    // 引用自检：缺哪个就明确报哪个，避免对着 NullReference 发呆
    private bool CheckRefs()
    {
        if (lobbyManager == null) { Debug.LogError("LobbyUI：lobbyManager 未赋值"); return false; }
        if (mainMenu == null) { Debug.LogError("LobbyUI：mainMenu 未赋值"); return false; }
        if (mapPanel == null) { Debug.LogError("LobbyUI：mapPanel 未赋值"); return false; }
        if (teamPanel == null) { Debug.LogError("LobbyUI：teamPanel 未赋值"); return false; }
        if (settingsPanel == null) { Debug.LogError("LobbyUI：settingsPanel 未赋值"); return false; }
        if (selectMapButton == null) { Debug.LogError("LobbyUI：selectMapButton 未赋值"); return false; }
        if (teamButton == null) { Debug.LogError("LobbyUI：teamButton 未赋值"); return false; }
        if (settingsButton == null) { Debug.LogError("LobbyUI：settingsButton 未赋值"); return false; }
        return true;
    }
}