using UnityEngine;                   // MonoBehaviour / Mathf
using UnityEngine.UI;                // Slider / Button
using TMPro;                         // TextMeshProUGUI

// ============ 设置界面（鼠标灵敏度 / 主音量 / 【新功能】两个开镜灵敏度）============
// 拖滑条：立刻生效（音量直接改 AudioListener；灵敏度实时读存档）；点【返回】或面板被关掉时写一次盘
// 挂载：Lobby 场景的 SettingsPanel 上
public class SettingsUI : MonoBehaviour
{
    [System.Serializable]
    public class SettingsTab
    {
        public Button button;        // 左侧导航按钮
        public GameObject page;      // 这一页的根物体
        public GameObject selectedMark; // 选中高亮条（可留空）
    }

    [Header("引用")]
    public LobbyUI lobbyUI;                        // 点返回时切回主界面（拖 Canvas）
    public Slider sensitivitySlider;               // 鼠标灵敏度滑条（全局）
    public TextMeshProUGUI sensitivityValueText;   // 灵敏度数值文字（比如 "200"）
    public Slider volumeSlider;                    // 音量滑条
    public TextMeshProUGUI volumeValueText;        // 音量数值文字（比如 "80%"）
    public Button backButton;                      // 【返回】按钮

    [Header("退出游戏（7.5）")]
    public Button quitGameButton;                  // 退出整个客户端（编辑器里 = 停止 Play）

    [Header("左侧分页（7.5：基础设置 / 灵敏度设置）")]
    public SettingsTab[] tabs;
    public int defaultTab = 0;
    public Color tabNormalColor = new Color(1f, 1f, 1f, 0.5f);
    public Color tabSelectedColor = Color.white;

    [Header("【阶段4-F】账号（没配 -lobbyserver 时自动隐藏这个按钮）")]
    [Tooltip("「退出登录」按钮（放在设置面板里）。留空 = 不做这个功能")]
    public Button logoutButton;
    [Tooltip("账号界面（挂在大厅 Canvas 上的 AuthScreenUI）。点「退出登录」后用它切到登录 / 注册界面")]
    public AuthScreenUI authScreen;

    [Header("【7.5】开镜灵敏度（滑条不拖也能用，没做的先留空）")]
    public Slider rifleScopeSlider;                     // 步枪开镜灵敏度滑条
    public TextMeshProUGUI rifleScopeValueText;         // 数值文字（可选）
    public Slider sniperScopeSmallSlider;               // 狙击枪小倍率开镜灵敏度
    public TextMeshProUGUI sniperScopeSmallValueText;
    public Slider sniperScopeLargeSlider;               // 狙击枪大倍率开镜灵敏度
    public TextMeshProUGUI sniperScopeLargeValueText;

    private void Start()
    {
        if (!CheckRefs()) return;

        // 1. 滑条范围用代码设置（不依赖 Inspector 里手填，避免忘填）
        sensitivitySlider.minValue = SettingsManager.MinMouseSensitivity;   // 50
        sensitivitySlider.maxValue = SettingsManager.MaxMouseSensitivity;   // 500
        sensitivitySlider.wholeNumbers = true;                              // 只取整数
        volumeSlider.minValue = 0f;                                         // 音量 0~1
        volumeSlider.maxValue = 1f;

        // 【新功能】两个开镜灵敏度滑条（范围整体更低）
        if (rifleScopeSlider != null)
        {
            rifleScopeSlider.minValue = SettingsManager.MinScopeSensitivity;
            rifleScopeSlider.maxValue = SettingsManager.MaxScopeSensitivity;
            rifleScopeSlider.wholeNumbers = true;
        }
        if (sniperScopeSmallSlider != null)
        {
            sniperScopeSmallSlider.minValue = SettingsManager.MinScopeSensitivity;
            sniperScopeSmallSlider.maxValue = SettingsManager.MaxScopeSensitivity;
            sniperScopeSmallSlider.wholeNumbers = true;
        }
        if (sniperScopeLargeSlider != null)
        {
            sniperScopeLargeSlider.minValue = SettingsManager.MinScopeSensitivity;
            sniperScopeLargeSlider.maxValue = SettingsManager.MaxScopeSensitivity;
            sniperScopeLargeSlider.wholeNumbers = true;
        }

        // 2. 先把滑条显示成存档里的当前值
        //    注意：先赋值、后绑事件，否则赋值那一刻会触发回调
        sensitivitySlider.value = SettingsManager.MouseSensitivity;
        volumeSlider.value = SettingsManager.MasterVolume;
        if (rifleScopeSlider != null) rifleScopeSlider.value = SettingsManager.RifleScopeSensitivity;
        if (sniperScopeSmallSlider != null) sniperScopeSmallSlider.value = SettingsManager.SniperScopeSensitivitySmall;
        if (sniperScopeLargeSlider != null) sniperScopeLargeSlider.value = SettingsManager.SniperScopeSensitivityLarge;
        RefreshText();

        // 3. 绑定滑条和按钮（代码绑，Inspector 里的 OnClick 留空）
        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (rifleScopeSlider != null) rifleScopeSlider.onValueChanged.AddListener(OnRifleScopeChanged);
        if (sniperScopeSmallSlider != null) sniperScopeSmallSlider.onValueChanged.AddListener(OnSniperSmallChanged);
        if (sniperScopeLargeSlider != null) sniperScopeLargeSlider.onValueChanged.AddListener(OnSniperLargeChanged);
        backButton.onClick.AddListener(OnBackClicked);
        if (quitGameButton != null) quitGameButton.onClick.AddListener(GameQuit.Quit);

        // 【7.5】左侧分页
        if (tabs != null)
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                SettingsTab tab = tabs[i];
                if (tab == null || tab.button == null) continue;
                int index = i;
                tab.button.onClick.AddListener(() => SelectTab(index));
            }
        }
        SelectTab(defaultTab);

        // 【阶段4-F】设置面板里的「退出登录」
        if (logoutButton != null) logoutButton.onClick.AddListener(OnLogoutClicked);
        RefreshLogoutButton();   // 按钮显隐按登录态初始化（每次打开面板都会再刷一次，见 OnEnable）
    }

    // 【新增，4-F】每次打开设置面板都刷一次「退出登录」的显隐
    //（登录态可能在面板关着的时候变了：比如刚在登录界面登出 / 换了账号）
    private void OnEnable()
    {
        RefreshLogoutButton();
        if (tabs != null && tabs.Length > 0) SelectTab(defaultTab);
    }

    // 【7.5】切页：只显示当前页，选中标记/按钮颜色跟着换
    public void SelectTab(int index)
    {
        if (tabs == null || tabs.Length == 0) return;
        index = Mathf.Clamp(index, 0, tabs.Length - 1);

        for (int i = 0; i < tabs.Length; i++)
        {
            SettingsTab tab = tabs[i];
            if (tab == null) continue;
            bool on = (i == index);
            if (tab.page != null) tab.page.SetActive(on);
            if (tab.selectedMark != null) tab.selectedMark.SetActive(on);
            if (tab.button != null && tab.button.targetGraphic != null)
                tab.button.targetGraphic.color = on ? tabSelectedColor : tabNormalColor;
        }
    }

    // 【新增，4-F】「退出登录」：登出 + 切到登录 / 注册界面（必须重新登录才回得到已登录区）
    //   主菜单上原来那个「账号」按钮就是挪到了这里，「设置」里语义也更明确
    private void OnLogoutClicked()
    {
        if (authScreen == null)
        {
            Debug.LogWarning("SettingsUI：authScreen 未赋值 → 点「退出登录」无法切到登录界面", this);
            return;
        }
        authScreen.LogoutAndShow();
    }

    // 只在"开了账号服务 且 已登录"时显示这个按钮（否则没东西可退，也避免没配服务时留个死按钮）
    private void RefreshLogoutButton()
    {
        if (logoutButton == null) return;
        logoutButton.gameObject.SetActive(AuthService.HasService && AuthService.IsLoggedIn);
    }

    // 灵敏度滑条变化：写进设置（先只在内存），并刷新数字
    private void OnSensitivityChanged(float value)
    {
        SettingsManager.MouseSensitivity = value;
        RefreshText();
    }

    // 音量滑条变化：写进设置，音量立即生效
    private void OnVolumeChanged(float value)
    {
        SettingsManager.MasterVolume = value;   // setter 里会顺手改 AudioListener.volume
        RefreshText();
    }

    // 【新功能】步枪开镜灵敏度
    private void OnRifleScopeChanged(float value)
    {
        SettingsManager.RifleScopeSensitivity = value;
        RefreshText();
    }

    // 【7.5】狙击枪小倍率
    private void OnSniperSmallChanged(float value)
    {
        SettingsManager.SniperScopeSensitivitySmall = value;
        RefreshText();
    }

    // 【7.5】狙击枪大倍率
    private void OnSniperLargeChanged(float value)
    {
        SettingsManager.SniperScopeSensitivityLarge = value;
        RefreshText();
    }

    // 【返回】：保存一次，切回主界面
    private void OnBackClicked()
    {
        SettingsManager.SaveAll();
        lobbyUI.ShowMainPanel();
    }

    // 兜底：面板被关掉/直接切场景/退出运行时也保存一次（防止没点返回就跑了）
    private void OnDisable()
    {
        SettingsManager.SaveAll();
    }

    // 刷新数值文字
    private void RefreshText()
    {
        sensitivityValueText.text = Mathf.RoundToInt(sensitivitySlider.value).ToString();     // 灵敏度显示整数
        volumeValueText.text = Mathf.RoundToInt(volumeSlider.value * 100f) + "%";             // 音量显示百分比

        // 【新功能】开镜灵敏度数值（没拖文字就不刷新）
        if (rifleScopeValueText != null && rifleScopeSlider != null)
            rifleScopeValueText.text = Mathf.RoundToInt(rifleScopeSlider.value).ToString();
        if (sniperScopeSmallValueText != null && sniperScopeSmallSlider != null)
            sniperScopeSmallValueText.text = Mathf.RoundToInt(sniperScopeSmallSlider.value).ToString();
        if (sniperScopeLargeValueText != null && sniperScopeLargeSlider != null)
            sniperScopeLargeValueText.text = Mathf.RoundToInt(sniperScopeLargeSlider.value).ToString();
    }

    // 引用自检：缺哪个就报哪个
    private bool CheckRefs()
    {
        if (lobbyUI == null) { Debug.LogError("SettingsUI：lobbyUI 未赋值"); return false; }
        if (sensitivitySlider == null) { Debug.LogError("SettingsUI：sensitivitySlider 未赋值"); return false; }
        if (sensitivityValueText == null) { Debug.LogError("SettingsUI：sensitivityValueText 未赋值"); return false; }
        if (volumeSlider == null) { Debug.LogError("SettingsUI：volumeSlider 未赋值"); return false; }
        if (volumeValueText == null) { Debug.LogError("SettingsUI：volumeValueText 未赋值"); return false; }
        if (backButton == null) { Debug.LogError("SettingsUI：backButton 未赋值"); return false; }
        if (logoutButton != null && authScreen == null)
            Debug.LogWarning("SettingsUI：logoutButton 已赋值但 authScreen 为空 → 点「退出登录」不会切界面");
        return true;
    }
}