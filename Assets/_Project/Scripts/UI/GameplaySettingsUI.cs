using UnityEngine;
using UnityEngine.UI;   // Slider / Button
using TMPro;            // TextMeshProUGUI
using Mirror;                      // 【新功能】联机判断 + 本机玩家（退出到组队界面）
using Shared;                      // Constants（大厅场景名）
using UnityEngine.SceneManagement; // LoadScene（单机退出到大厅）

// 【新功能】游戏内设置面板（G 键呼出/关闭；不暂停游戏）
// 内容：主音量 + 全局灵敏度 + 步枪开镜灵敏度 + 狙击枪开镜灵敏度（三档灵敏度实时生效） + 退出对局（回大厅）
// 规则：
//   1) 按 G 开/关；也可点面板里的"关闭"按钮
//   2) 打开时：解锁鼠标 + 冻结本地操作（视角/开火/移动）；世界照常运行（绝不改 Time.timeScale）
//   3) 设置即时生效 + 走 SettingsManager 持久化（关闭时 SaveAll 写盘）
// 挂载：map1 的 MatchCanvas（常驻物体）上——不要挂在面板自己身上（面板会隐藏，脚本会跟着停）
public class GameplaySettingsUI : MonoBehaviour
{
    // 给 PlayerLook / WeaponController / PlayerMotor 读的全局开关（静态，跨脚本通信）
    public static bool IsOpen { get; private set; }

    [Header("引用")]
    public GameObject panel;                      // 设置面板根物体
    public Slider volumeSlider;                   // 主音量
    public TextMeshProUGUI volumeValueText;       // 音量数值（可选）
    public Slider globalSensitivitySlider;        // 全局灵敏度
    public TextMeshProUGUI globalSensitivityText; // 全局灵敏度数值（可选）
    public Slider rifleScopeSlider;               // 步枪开镜灵敏度
    public TextMeshProUGUI rifleScopeText;        // （可选）
    public Slider sniperScopeSlider;              // 狙击枪开镜灵敏度
    public TextMeshProUGUI sniperScopeText;       // （可选）
    public Button closeButton;                    // 面板上的"关闭"按钮（可选）
    public Button exitMatchButton;                // 【新功能】退出对局：不想玩的玩家直接回大厅（客机=自己退出、其他人继续打；主机=解散房间；单机=回大厅组队界面）

    private bool loading;   // 载入滑条值时屏蔽回调（避免"初始化赋值→误触发保存"）

    private void Start()
    {
        if (panel == null) panel = gameObject;

        // 滑条范围用 SettingsManager 的常量（和存档逻辑同源，不会对不上）
        if (volumeSlider != null) { volumeSlider.minValue = 0f; volumeSlider.maxValue = 1f; }
        if (globalSensitivitySlider != null)
        {
            globalSensitivitySlider.minValue = SettingsManager.MinMouseSensitivity;
            globalSensitivitySlider.maxValue = SettingsManager.MaxMouseSensitivity;
            globalSensitivitySlider.wholeNumbers = true;
        }
        if (rifleScopeSlider != null)
        {
            rifleScopeSlider.minValue = SettingsManager.MinScopeSensitivity;
            rifleScopeSlider.maxValue = SettingsManager.MaxScopeSensitivity;
            rifleScopeSlider.wholeNumbers = true;
        }
        if (sniperScopeSlider != null)
        {
            sniperScopeSlider.minValue = SettingsManager.MinScopeSensitivity;
            sniperScopeSlider.maxValue = SettingsManager.MaxScopeSensitivity;
            sniperScopeSlider.wholeNumbers = true;
        }

        // 载入当前值（先赋值、后绑事件）
        loading = true;
        if (volumeSlider != null) volumeSlider.value = SettingsManager.MasterVolume;
        if (globalSensitivitySlider != null) globalSensitivitySlider.value = SettingsManager.MouseSensitivity;
        if (rifleScopeSlider != null) rifleScopeSlider.value = SettingsManager.RifleScopeSensitivity;
        if (sniperScopeSlider != null) sniperScopeSlider.value = SettingsManager.SniperScopeSensitivity;
        loading = false;
        RefreshTexts();

        // 绑定事件
        if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (globalSensitivitySlider != null) globalSensitivitySlider.onValueChanged.AddListener(OnGlobalSensitivityChanged);
        if (rifleScopeSlider != null) rifleScopeSlider.onValueChanged.AddListener(OnRifleScopeChanged);
        if (sniperScopeSlider != null) sniperScopeSlider.onValueChanged.AddListener(OnSniperScopeChanged);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (exitMatchButton != null) exitMatchButton.onClick.AddListener(OnExitMatchClicked);   // 【新功能】退出对局（回大厅）

        panel.SetActive(false);
        IsOpen = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))   // G：呼出/关闭
        {
            if (IsOpen) Close();
            else Open();
        }
    }

    // 面板被销毁/场景卸载时清掉标志（避免"卡在打开状态"）
    private void OnDisable()
    {
        IsOpen = false;
    }

    public void Open()
    {
        panel.SetActive(true);
        IsOpen = true;

        // 解锁鼠标（能点滑条），但游戏世界不被暂停（不动 Time.timeScale）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        panel.SetActive(false);
        IsOpen = false;

        // 回到游戏：重新锁鼠标。
        // 注意：如果对局已结束（结算冻结 timeScale=0），保持解锁——结算面板还要用鼠标点按钮
        if (Time.timeScale > 0f)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        SettingsManager.SaveAll();   // 关闭时统一写一次盘（持久化）
    }

    // 【新功能】"退出对局"按钮：让不想玩的玩家直接退出到大厅
    //   联机-客机：StopClient → 只有自己回大厅（Mirror 自动回 offlineScene=Lobby），其他人继续打
    //   联机-主机：StopHost → 会话承载在主机上，主机退出 = 解散房间，所有人回大厅
    //   单机：没有会话，直接回大厅并打开组队界面
    private void OnExitMatchClicked()
    {
        Time.timeScale = 1f;                       // 【保险】结算冻结（=0）状态下退出也能正常回大厅

        if (NetworkServer.active || NetworkClient.active)
        {
            Close();                                // 收起面板 + 保存设置
            Cursor.lockState = CursorLockMode.None; // 大厅菜单需要鼠标（不依赖玩家对象销毁时机）
            Cursor.visible = true;

            if (NetworkServer.active)
            {
                Debug.Log("[设置] 主机退出对局 → 解散房间，所有人回大厅");
                NetworkManager.singleton.StopHost();
            }
            else
            {
                Debug.Log("[设置] 客机退出对局 → 自己回大厅，其他人继续打");
                NetworkManager.singleton.StopClient();
            }
        }
        else
        {
            // 单机：回大厅 + 直接打开组队界面
            LobbyUI.openTeamPanelOnLoad = true;    // 跨场景标记（LobbyUI.Start 里读取）
            Close();                               // 收起面板 + 保存设置
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(Constants.LobbySceneName);
        }
    }

    // ---- 滑条回调：统一"loading 期间不响应"，防止初始化时误写 ----
    private void OnVolumeChanged(float value)
    {
        if (loading) return;
        SettingsManager.MasterVolume = value;   // setter 里立即改 AudioListener.volume
        RefreshTexts();
    }

    private void OnGlobalSensitivityChanged(float value)
    {
        if (loading) return;
        SettingsManager.MouseSensitivity = value;
        RefreshTexts();
    }

    private void OnRifleScopeChanged(float value)
    {
        if (loading) return;
        SettingsManager.RifleScopeSensitivity = value;
        RefreshTexts();
    }

    private void OnSniperScopeChanged(float value)
    {
        if (loading) return;
        SettingsManager.SniperScopeSensitivity = value;
        RefreshTexts();
    }

    // 刷新数值文字（没拖文字的项自动跳过）
    private void RefreshTexts()
    {
        if (volumeValueText != null && volumeSlider != null)
            volumeValueText.text = Mathf.RoundToInt(volumeSlider.value * 100f) + "%";
        if (globalSensitivityText != null && globalSensitivitySlider != null)
            globalSensitivityText.text = Mathf.RoundToInt(globalSensitivitySlider.value).ToString();
        if (rifleScopeText != null && rifleScopeSlider != null)
            rifleScopeText.text = Mathf.RoundToInt(rifleScopeSlider.value).ToString();
        if (sniperScopeText != null && sniperScopeSlider != null)
            sniperScopeText.text = Mathf.RoundToInt(sniperScopeSlider.value).ToString();
    }
}