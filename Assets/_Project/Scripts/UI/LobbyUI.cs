using UnityEngine;
using UnityEngine.UI;

// ============ 大厅 UI 总控（三个界面的切换开关）============
// 三个界面：MainMenu 主界面 / MapPanel 地图选择 / TeamPanel 组队
// 挂载：挂在 Lobby 场景 Canvas 上
public class LobbyUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyManager lobbyManager;    // 大厅逻辑管理器

    [Header("三个界面")]
    public GameObject mainMenu;          // 大厅主界面（选择地图 + 组队）
    public GameObject mapPanel;          // 地图选择界面
    public GameObject teamPanel;         // 组队界面

    [Header("主界面上的按钮")]
    public Button selectMapButton;       // 【选择地图】按钮
    public Button teamButton;            // 【组队】按钮

    private void Start()
    {
        if (!CheckRefs()) return;

        // 1. 绑定主界面按钮（都是代码绑，Inspector 里的 OnClick 留空）
        selectMapButton.onClick.AddListener(OpenMapPanel);   // 点"选择地图" → 打开地图选择界面
        teamButton.onClick.AddListener(OpenTeamPanel);       // 点"组队" → 打开组队界面

        // 2. 游戏一开始只显示主界面
        ShowMainPanel();
    }

    // 打开地图选择界面
    public void OpenMapPanel()
    {
        mainMenu.SetActive(false);
        mapPanel.SetActive(true);
        teamPanel.SetActive(false);
    }

    // 打开组队界面
    public void OpenTeamPanel()
    {
        mainMenu.SetActive(false);
        mapPanel.SetActive(false);
        teamPanel.SetActive(true);
    }

    // 回到大厅主界面（地图面板和组队面板的"返回"按钮都调它）
    public void ShowMainPanel()
    {
        mainMenu.SetActive(true);
        mapPanel.SetActive(false);
        teamPanel.SetActive(false);
    }

    // 引用自检：缺哪个就明确报哪个，避免对着 NullReference 发呆
    private bool CheckRefs()
    {
        if (lobbyManager == null) { Debug.LogError("LobbyUI：lobbyManager 未赋值"); return false; }
        if (mainMenu == null) { Debug.LogError("LobbyUI：mainMenu 未赋值"); return false; }
        if (mapPanel == null) { Debug.LogError("LobbyUI：mapPanel 未赋值"); return false; }
        if (teamPanel == null) { Debug.LogError("LobbyUI：teamPanel 未赋值"); return false; }
        if (selectMapButton == null) { Debug.LogError("LobbyUI：selectMapButton 未赋值"); return false; }
        if (teamButton == null) { Debug.LogError("LobbyUI：teamButton 未赋值"); return false; }
        return true;
    }
}