using UnityEngine;                   // MonoBehaviour / Mathf
using UnityEngine.UI;                // Slider / Button
using TMPro;                         // TextMeshProUGUI

// ============ 设置界面（鼠标灵敏度 / 主音量 / 【新功能】两个开镜灵敏度）============
// 拖滑条：立刻生效（音量直接改 AudioListener；灵敏度实时读存档）；点【返回】或面板被关掉时写一次盘
// 挂载：Lobby 场景的 SettingsPanel 上
public class SettingsUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyUI lobbyUI;                        // 点返回时切回主界面（拖 Canvas）
    public Slider sensitivitySlider;               // 鼠标灵敏度滑条（全局）
    public TextMeshProUGUI sensitivityValueText;   // 灵敏度数值文字（比如 "200"）
    public Slider volumeSlider;                    // 音量滑条
    public TextMeshProUGUI volumeValueText;        // 音量数值文字（比如 "80%"）
    public Button backButton;                      // 【返回】按钮

    [Header("【新功能】开镜灵敏度（滑条不拖也能用，没做的先留空）")]
    public Slider rifleScopeSlider;                     // 步枪开镜灵敏度滑条
    public TextMeshProUGUI rifleScopeValueText;         // 数值文字（可选）
    public Slider sniperScopeSlider;                    // 狙击枪开镜灵敏度滑条
    public TextMeshProUGUI sniperScopeValueText;        // 数值文字（可选）

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
        if (sniperScopeSlider != null)
        {
            sniperScopeSlider.minValue = SettingsManager.MinScopeSensitivity;
            sniperScopeSlider.maxValue = SettingsManager.MaxScopeSensitivity;
            sniperScopeSlider.wholeNumbers = true;
        }

        // 2. 先把滑条显示成存档里的当前值
        //    注意：先赋值、后绑事件，否则赋值那一刻会触发回调
        sensitivitySlider.value = SettingsManager.MouseSensitivity;
        volumeSlider.value = SettingsManager.MasterVolume;
        if (rifleScopeSlider != null) rifleScopeSlider.value = SettingsManager.RifleScopeSensitivity;
        if (sniperScopeSlider != null) sniperScopeSlider.value = SettingsManager.SniperScopeSensitivity;
        RefreshText();

        // 3. 绑定滑条和按钮（代码绑，Inspector 里的 OnClick 留空）
        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (rifleScopeSlider != null) rifleScopeSlider.onValueChanged.AddListener(OnRifleScopeChanged);
        if (sniperScopeSlider != null) sniperScopeSlider.onValueChanged.AddListener(OnSniperScopeChanged);
        backButton.onClick.AddListener(OnBackClicked);
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

    // 【新功能】狙击枪开镜灵敏度
    private void OnSniperScopeChanged(float value)
    {
        SettingsManager.SniperScopeSensitivity = value;
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
        if (sniperScopeValueText != null && sniperScopeSlider != null)
            sniperScopeValueText.text = Mathf.RoundToInt(sniperScopeSlider.value).ToString();
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
        return true;
    }
}