using UnityEngine;
using TMPro;    // TMP_Text
using Mirror;   // NetworkClient / NetworkTime

// 【新功能】网络延迟显示（实时 RTT）
// 规则：联机会话里显示"延迟 xx ms"（绿<80 / 黄<160 / 红≥160）；纯单机自动隐藏
// 挂载：挂 map1 HUD 里那个显示延迟的文本自己身上即可（或挂 Canvas 上，拖 text 引用）
// 【修复】隐藏时只关"文字组件"（pingText.enabled），绝不 SetActive(false) 整个物体：
//   本脚本常常就挂在文本自己身上 —— 一旦把物体隐藏，脚本自己也会停，永远显示不回来（踩过的坑）
public class PingDisplayUI : MonoBehaviour
{
    [Header("引用")]
    public TMP_Text pingText;        // 显示"延迟 xx ms"的文本
    [Tooltip("可选：整个延迟面板（背景+图标+文字）。脚本建议挂在 MatchCanvas 上，pingPanel 作为其子物体，单机时整块隐藏")]
    public GameObject pingPanel;

    [Header("参数")]
    public float refreshInterval = 0.5f;   // 刷新间隔（秒）

    private float nextRefresh;

    private void Start()
    {
        if (pingText == null)
        {
            Debug.LogError("PingDisplayUI：pingText 未赋值（把显示延迟的文本拖进来）");
            return;
        }
        pingText.enabled = false;   // 只关文字组件（物体保持激活 → 本脚本照常运行）
        if (pingPanel != null) pingPanel.SetActive(false);
    }

    private void Update()
    {
        if (pingText == null) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshInterval;

        // 纯单机没有网络会话 → 隐藏文字（同样只关组件，不关物体）
        if (!NetworkClient.active)
        {
            if (pingText.enabled) pingText.enabled = false;
            if (pingPanel != null && pingPanel.activeSelf) pingPanel.SetActive(false);
            return;
        }

        if (!pingText.enabled) pingText.enabled = true;   // 联机中 → 显示文字
        if (pingPanel != null && !pingPanel.activeSelf) pingPanel.SetActive(true);

        int ms = Mathf.RoundToInt((float)(NetworkTime.rtt * 1000.0));   // rtt 单位是秒，换成毫秒
        pingText.text = "延迟 " + ms + " ms";

        // 颜色分档：绿(流畅) / 黄(偏高) / 红(卡)
        pingText.color = (ms < 80) ? new Color(0.35f, 0.95f, 0.45f)
                       : (ms < 160) ? new Color(1f, 0.85f, 0.3f)
                                    : new Color(1f, 0.35f, 0.3f);
    }
}