using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    [Header("【阶段3-D】房间列表 + 状态提示")]
    public Button roomListButton;             // 主界面上的"房间列表"按钮
    public RoomListPanelUI roomListPanel;     // 房间列表面板（脚本挂在 Canvas 上，它自己管面板显隐）
    public TMP_Text statusText;               // 状态行：创建房间 / 快速匹配 / 进房 的进度与结果

    [Header("【阶段3-F】账号")]
    public Button authButton;                 // 主界面上的「账号」按钮（登录后显示"账号：昵称"）
    public AuthScreenUI authScreen;           // 登录 / 注册界面（全屏；脚本挂在 Canvas 上）

    [Header("【新功能】退出游戏")]
    public Button quitButton;                 // 主界面上的"退出游戏"按钮

    // 【新功能】跨场景标记：从对局"退出到组队界面"返回大厅时，直接打开组队界面
    public static bool openTeamPanelOnLoad;

    private void Start()
    {
        if (!CheckRefs()) return;

        // 【阶段3-房间码】读命令行 -lobbyserver（幂等：LobbyManager 那边也会调）
        LobbyService.InitFromCommandLine();

        // 【阶段3-F】同步初始化账号服务：先把上次的令牌从本地存档读出来，
        // 免得有令牌的玩家进游戏时先闪一下"未登录"区
        AuthService.Init();

        // 【阶段3-D】订阅大厅状态广播 → 显示成一行文字（创建房间/快速匹配/输码进房的进度与失败原因）
        lobbyManager.OnStatus += ShowStatus;
        if (statusText != null) statusText.text = "";

        // 【阶段3-F】订阅登录态变化 → 刷新「账号」按钮上的文字
        AuthService.OnProfileChanged += RefreshAuthButton;

        // 1. 绑定主界面按钮（都是代码绑，Inspector 里的 OnClick 留空）
        selectMapButton.onClick.AddListener(OpenMapPanel);        // 点"选择地图" → 打开地图选择界面
        teamButton.onClick.AddListener(OnTeamClicked);            // 点"组队" → 按连接模式分流（见下）
        if (soloPracticeButton != null) soloPracticeButton.onClick.AddListener(OpenTeamPanel);   // 单人练习 = 原组队界面
        if (quickMatchButton != null) quickMatchButton.onClick.AddListener(OnQuickMatchClicked); // 【扩展】快速匹配
        if (joinRoomButton != null) joinRoomButton.onClick.AddListener(OnJoinRoomClicked);       // 加入房间（打开弹窗）
        if (roomListButton != null) roomListButton.onClick.AddListener(OnRoomListClicked);       // 【阶段3-D】打开房间列表
        if (authButton != null) authButton.onClick.AddListener(OnAuthClicked);                   // 【阶段3-F】登录 / 注册
        settingsButton.onClick.AddListener(OpenSettingsPanel);    // 【新增】点"设置" → 打开设置界面
        if (quitButton != null) quitButton.onClick.AddListener(QuitGame);   // 【新功能】退出游戏

        RefreshAuthButton();   // 按钮文字按当前登录态初始化一次

        // 2. 【阶段3-F】登录界面只在**还没登录**的时候拦一道。
        //    【Bug 修复】原来靠"是否从对局返回"的跨场景标记来判断 —— 但那个标记只在"设置面板 → 退出对局"
        //    里设置，结算面板的「返回大厅」根本没设 → 打完一局回大厅照样弹登录界面。
        //    改成看登录态：打完一局必然是已登录的，所以无论从哪条路径回大厅都不会再被拦；
        //    冷启动时如果没有令牌、或刚"退出登录"，才会显示登录界面。
        bool returningFromMatch = openTeamPanelOnLoad;
        openTeamPanelOnLoad = false;   // 用掉就清掉（下次正常回大厅不会再自动打开）

        if (AuthService.HasService && authScreen != null && !AuthService.IsLoggedIn)
        {
            // 显示登录界面；登录后由 AuthScreenUI 点「进入游戏」才真的进大厅。
            // 没配账号服务（-lobbyserver）时这个分支不会进 → 局域网与单人练习完全不受影响
            HideAllPanels();
            authScreen.Show();
            return;
        }

        // 没配账号服务 / 已登录 → 确保登录界面是关的，免得它盖住大厅
        if (authScreen != null) authScreen.Hide();

        // 3. 从对局"退出到组队界面"回大厅 → 直接打开组队界面；否则显示主界面
        if (returningFromMatch) OpenTeamPanel();
        else ShowMainPanel();
    }

    // 【阶段2.4-C 扩展】点"加入房间" → 打开输入 IP 的弹窗（不再在主界面直接摆输入框）
    private void OnJoinRoomClicked()
    {
        if (joinRoomDialog != null) joinRoomDialog.Show();
        else Debug.LogWarning("[大厅] 加入房间弹窗未挂载（joinRoomDialog）");
    }

    // 【阶段3-房间码】点"组队"：
    //   配了房间登记服务地址（-lobbyserver）→ 公网模式：从登记服务领一个空房间连进去，
    //                                          第一个进去的人就是房主，并拿到可分享的房间码；
    //   没配 → 保持原来的局域网玩法：自己开主机（StartHost），日常联机调试走这条
    private void OnTeamClicked()
    {
        if (LobbyService.HasService) lobbyManager.CreateRoomOnline();
        else lobbyManager.CreateRoom();
    }

    // 【阶段3-房间码】快速匹配：查房间列表，挑一个"没满、不在对局中"的房间加入
    private void OnQuickMatchClicked()
    {
        if (LobbyService.HasService) lobbyManager.QuickMatch();
        else ShowStatus("快速匹配需要房间登记服务（用 -lobbyserver 指定地址）");
    }

    // 【阶段3-D】打开房间列表面板：让位给列表（收起四个主面板）
    private void OnRoomListClicked()
    {
        if (roomListPanel == null)
        {
            Debug.LogWarning("[大厅] 房间列表面板未挂载（roomListPanel）");
            ShowStatus("房间列表功能未挂载");
            return;
        }
        HideAllPanels();           // 四个主面板让位（列表面板在别的物体上，不受影响）
        roomListPanel.Show();      // 面板自己管显隐 + 拉数据
    }

    // 【阶段3-D】把大厅逻辑层广播的状态（创建房间/快速匹配/进房的进度与失败原因）显示到状态行
    private void ShowStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        else Debug.Log("[大厅] " + msg);
    }

    // 【阶段3-F】从主菜单打开账号界面（已登录时它显示的是"资料 + 进入游戏 + 退出登录"）
    private void OnAuthClicked()
    {
        if (authScreen == null)
        {
            Debug.LogWarning("[大厅] 账号界面未挂载（authScreen）");
            ShowStatus("账号功能未挂载");
            return;
        }
        HideAllPanels();          // 主菜单让位（账号界面在别的物体上，不受影响）
        authScreen.Show();
    }

    // 【阶段3-F】按登录态刷新「登录」按钮的文字
    private void RefreshAuthButton()
    {
        if (authButton == null) return;

        TMP_Text label = authButton.GetComponentInChildren<TMP_Text>();
        if (label == null) return;

        label.text = AuthService.IsLoggedIn ? ("账号：" + AuthService.CurrentNickname) : "登录";
    }

    private void OnDestroy()
    {
        // 取消订阅（大厅物体销毁时避免残留回调）
        if (lobbyManager != null) lobbyManager.OnStatus -= ShowStatus;
        AuthService.OnProfileChanged -= RefreshAuthButton;
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

        // 【Bug 修复】房间列表面板也要关 —— 它自己管显隐，不在上面那四个里；
        // 登录界面弹出时如果不关它，它会盖在登录界面上（实测踩到）
        if (roomListPanel != null) roomListPanel.Hide();
    }

    // 【新功能】"退出游戏"按钮：编辑器里停止运行；打包版里关闭应用
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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