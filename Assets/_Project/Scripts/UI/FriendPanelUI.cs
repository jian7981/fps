using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 【阶段2.4-C 扩展】好友面板（占位版，王者风：右侧好友栏）
// 现在：展示"模拟好友数据 + 在线状态点"；点邀请给出提示。
// 阶段3（公网部署）：把 mockNames/mockOnline 换成后端返回的好友列表即可，其余不用改。
// 挂载：FriendsPanel 自己身上即可（它不隐藏自己；首次显示时 Start 会补跑）
public class FriendPanelUI : MonoBehaviour
{
    [System.Serializable]
    public class FriendRow
    {
        public GameObject rowRoot;    // 整行根物体（按数据多少显示/隐藏整行）
        public Image onlineDot;       // 在线状态圆点（绿=在线，灰=离线）
        public TMP_Text nameText;     // 好友名
        public Button inviteButton;   // 邀请按钮
    }

    [Header("引用（按顺序拖好，最多显示 10 个好友；配合 ScrollRect 装不下会自动可滚动）")]
    public FriendRow[] rows;

    // —— 模拟数据：阶段3 换成后端好友列表 ——
    // 演示 10 条（4 个在线、6 个离线），配合可滚动列表能看到"装不下就滑"的效果
    private readonly string[] mockNames = { "好友示例A", "好友示例B", "好友示例C", "好友示例D", "好友示例E",
                                            "好友示例F", "好友示例G", "好友示例H", "好友示例I", "好友示例J" };
    private readonly bool[] mockOnline = { true, true, true, false, false, false, true, false, false, false };

    private void Start()
    {
        for (int i = 0; i < rows.Length; i++)
        {
            FriendRow row = rows[i];
            if (row == null || row.rowRoot == null) continue;

            bool hasData = i < mockNames.Length;          // 数据不够就隐藏整行
            row.rowRoot.SetActive(hasData);
            if (!hasData) continue;

            bool online = mockOnline[i];
            if (row.nameText != null) row.nameText.text = mockNames[i];
            if (row.onlineDot != null)
                row.onlineDot.color = online ? new Color(0.30f, 0.90f, 0.40f)   // 在线：绿
                                             : new Color(0.50f, 0.50f, 0.50f);  // 离线：灰
            if (row.inviteButton != null)
            {
                row.inviteButton.interactable = online;   // 离线不能邀请
                row.inviteButton.onClick.AddListener(() =>
                    Debug.Log("[好友] 邀请 " + row.nameText.text + " 的功能将在公网版开放（阶段3）"));
            }
        }
    }
}