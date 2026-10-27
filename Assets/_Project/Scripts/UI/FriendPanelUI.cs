using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ==================== 好友栏（7.2 正式版） ====================
// 数据：当前仍是 mockNames/mockOnline（阶段3 换成后端返回的好友列表）。
// 结构：ScrollRect/Content 里按行预制体动态生成，不再手动摆 10 行。
//
// 挂载：FriendsPanel 上即可（它不隐藏自己；第一次显示时 Start 会补跑）。
// 预制体：RoomFriendRow.prefab（根物体挂 FriendRowUI）
public class FriendPanelUI : MonoBehaviour
{
    [Header("引用")]
    public GameObject rowPrefab;         // RoomFriendRow.prefab
    public RectTransform content;        // ScrollRect/Viewport/Content（挂 Vertical Layout Group + Content Size Fitter）
    public TMP_InputField searchInput;   // 可选：搜索框（输入后按名字过滤）
    public Button inviteAllButton;       // 可选：一键邀请

    // —— 模拟数据：阶段3 换成后端好友列表 ——
    private readonly string[] mockNames = { "好友示例A", "好友示例B", "好友示例C", "好友示例D", "好友示例E",
                                            "好友示例F", "好友示例G", "好友示例H", "好友示例I", "好友示例J" };
    private readonly bool[] mockOnline = { true, true, true, false, false, false, true, false, false, false };

    private readonly List<FriendRowUI> rows = new List<FriendRowUI>();

    private void Start()
    {
        if (!CheckRefs()) return;

        BuildRows();

        if (searchInput != null)
            searchInput.onValueChanged.AddListener(FilterRows);

        if (inviteAllButton != null)
            inviteAllButton.onClick.AddListener(InviteAll);
    }

    private void BuildRows()
    {
        // 清掉场景里可能残留的旧行（旧版是手动摆 10 行）
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            GameObject child = content.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
        rows.Clear();

        for (int i = 0; i < mockNames.Length; i++)
        {
            GameObject go = Instantiate(rowPrefab, content, false);
            go.name = "FriendRow_" + mockNames[i];

            FriendRowUI row = go.GetComponent<FriendRowUI>();
            if (row == null)
            {
                Debug.LogError("FriendPanelUI：rowPrefab 上没有 FriendRowUI 组件");
                Destroy(go);
                continue;
            }

            row.Bind(mockNames[i], mockOnline[i], OnInviteClicked);
            rows.Add(row);
        }
    }

    private void FilterRows(string query)
    {
        query = (query ?? "").Trim();
        for (int i = 0; i < rows.Count; i++)
        {
            bool match = query.Length == 0 ||
                         rows[i].FriendName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            rows[i].gameObject.SetActive(match);
        }
    }

    private void OnInviteClicked(string friendName)
    {
        Debug.Log("[好友] 邀请 " + friendName + " 的功能将在公网版开放（阶段3）");
    }

    private void InviteAll()
    {
        Debug.Log("[好友] 一键邀请将在公网版开放（阶段3）");
    }

    private bool CheckRefs()
    {
        if (rowPrefab == null) { Debug.LogError("FriendPanelUI：rowPrefab 未赋值（拖 RoomFriendRow.prefab）"); return false; }
        if (content == null) { Debug.LogError("FriendPanelUI：content 未赋值（拖 FriendsScroll/Viewport/Content）"); return false; }
        return true;
    }
}
