using UnityEngine;
using UnityEngine.UI;
using TMPro;    // TextMeshProUGUI
using Shared;   // Constants

// ============ 地图选择界面 ============
// 职责：点地图按钮 → 告诉 LobbyManager 选了哪张图；点返回 → 回到大厅主界面
// 挂载：挂在 MapPanel 上
public class MapSelectPanel : MonoBehaviour
{
    [Header("引用")]
    public LobbyUI lobbyUI;              // 返回时用它切回主界面
    public LobbyManager lobbyManager;    // 选图时把结果记在它身上

    [Header("UI")]
    public Button map1Button;            // 【地图1】按钮（以后加地图就再加按钮）
    public Button backButton;            // 【返回】按钮
    public TextMeshProUGUI selectedMapText;   // 显示"当前已选：xxx"

    private void Start()
    {
        if (!CheckRefs()) return;

        // 1. 绑定按钮：地图1 用的是 Constants 里的场景名，避免手打字符串打错
        map1Button.onClick.AddListener(() => SelectMap(Constants.GameSceneName1));
        backButton.onClick.AddListener(() => lobbyUI.ShowMainPanel());

        // 2. 打开面板时，把当前已选的地图显示出来
        selectedMapText.text = "当前已选：" + ToDisplayName(lobbyManager.selectedMapName);
    }

    // 选中一张地图：1. 记进 LobbyManager  2. 立刻给玩家文字反馈
    private void SelectMap(string sceneName)
    {
        lobbyManager.SelectMap(sceneName);
        selectedMapText.text = "当前已选：" + ToDisplayName(sceneName);
    }

    // 把场景名翻译成给人看的名字（以后加地图在这里加一行）
    private string ToDisplayName(string sceneName)
    {
        if (sceneName == Constants.GameSceneName1) return "地图1（map1）";
        return sceneName;   // 没登记过的就原样显示场景名
    }

    private bool CheckRefs()
    {
        if (lobbyUI == null) { Debug.LogError("MapSelectPanel：lobbyUI 未赋值"); return false; }
        if (lobbyManager == null) { Debug.LogError("MapSelectPanel：lobbyManager 未赋值"); return false; }
        if (map1Button == null) { Debug.LogError("MapSelectPanel：map1Button 未赋值"); return false; }
        if (backButton == null) { Debug.LogError("MapSelectPanel：backButton 未赋值"); return false; }
        if (selectedMapText == null) { Debug.LogError("MapSelectPanel：selectedMapText 未赋值"); return false; }
        return true;
    }
}