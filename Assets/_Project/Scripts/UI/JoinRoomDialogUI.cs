using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 【阶段2.4-C 扩展】"加入房间"弹窗（模态）：输入房主 IP → 确定 → 发起连接
// 挂载：大厅 Canvas（常驻物体）上——不要挂在弹窗自己身上（弹窗会隐藏，脚本会跟着停，RoomPanelUI 踩过这个坑）
public class JoinRoomDialogUI : MonoBehaviour
{
    [Header("引用")]
    public GameObject dialog;          // 弹窗根物体（激活时显示）
    public TMP_InputField ipInput;     // IP 输入框（留空 = localhost）
    public Button confirmButton;       // 确定：发起连接
    public Button cancelButton;        // 取消：关闭弹窗
    public LobbyManager lobbyManager;  // 连接逻辑（大厅里那个）

    private void Start()
    {
        if (dialog == null) dialog = gameObject;
        if (confirmButton != null) confirmButton.onClick.AddListener(OnConfirm);
        if (cancelButton != null) cancelButton.onClick.AddListener(Hide);
        dialog.SetActive(false);
    }

    private void Update()
    {
        // Esc 关闭弹窗
        if (dialog.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Hide();
    }

    // 打开弹窗：清空输入并自动聚焦（直接就能打字）
    public void Show()
    {
        dialog.SetActive(true);
        if (ipInput != null)
        {
            ipInput.text = "";
            ipInput.ActivateInputField();
        }
    }

    public void Hide()
    {
        dialog.SetActive(false);
    }

    private void OnConfirm()
    {
        string ip = (ipInput != null) ? ipInput.text : "";
        Hide();
        if (lobbyManager != null) lobbyManager.JoinRoom(ip);
        else Debug.LogWarning("[大厅] 加入房间失败：lobbyManager 未挂载");
    }
}