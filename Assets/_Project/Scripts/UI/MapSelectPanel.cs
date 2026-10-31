using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============ 【7.5】地图选择界面（数据驱动）============
// 所有地图都写在 MapCatalog.asset 的 maps 数组里；本脚本按数据生成地图卡片。
// 以后加地图：只加 MapCatalog 的一条数据 + 把场景加进 Build Settings，不用改代码/UI。
// 挂载：MapPanel 上；卡片容器和卡片 prefab 在 Inspector 里拖。
public class MapSelectPanel : MonoBehaviour
{
    [Header("数据（新增地图只改这个资产）")]
    public MapCatalog mapCatalog;

    [Header("UI")]
    public Transform mapCardsContent;      // 卡片容器（挂 Grid Layout Group）
    public MapCardUI mapCardPrefab;        // 地图卡片预制体（挂 MapCardUI）
    public TMP_Text selectedMapText;       // "当前已选：xxx"
    public Button backButton;              // 返回大厅

    [Header("引用")]
    public LobbyUI lobbyUI;
    public LobbyManager lobbyManager;

    private readonly List<MapCardUI> cards = new List<MapCardUI>();

    private void Start()
    {
        if (!CheckRefs()) return;
        backButton.onClick.AddListener(() => lobbyUI.ShowMainPanel());
        BuildCards();
    }

    // 按 MapCatalog 生成卡片；默认选中当前存档里的地图，没有就选第一张
    private void BuildCards()
    {
        for (int i = 0; i < cards.Count; i++)
            if (cards[i] != null) Destroy(cards[i].gameObject);
        cards.Clear();

        if (mapCatalog == null || mapCatalog.Count == 0)
        {
            selectedMapText.text = "未配置地图";
            Debug.LogError("MapSelectPanel：MapCatalog 没有地图数据");
            return;
        }

        for (int i = 0; i < mapCatalog.Count; i++)
        {
            MapCatalog.MapEntry entry = mapCatalog.Get(i);
            if (entry == null || string.IsNullOrEmpty(entry.sceneName)) continue;

            MapCardUI card = Instantiate(mapCardPrefab, mapCardsContent, false);
            MapCatalog.MapEntry captured = entry;
            card.Bind(captured, () => SelectMap(captured.sceneName));
            cards.Add(card);
        }

        string initial = (lobbyManager != null) ? lobbyManager.selectedMapName : "";
        if (mapCatalog.Find(initial) == null) initial = mapCatalog.Get(0).sceneName;

        SelectMap(initial);
    }

    // 选中一张地图：记进 LobbyManager + 更新文字 + 更新所有卡片的选中叠层
    private void SelectMap(string sceneName)
    {
        MapCatalog.MapEntry entry = mapCatalog.Find(sceneName);
        if (entry == null) return;

        lobbyManager.SelectMap(sceneName);
        selectedMapText.text = "当前已选：" + entry.displayName;

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            cards[i].SetSelected(cards[i].SceneName == sceneName);
        }
    }

    private bool CheckRefs()
    {
        if (lobbyUI == null) { Debug.LogError("MapSelectPanel：lobbyUI 未赋值"); return false; }
        if (lobbyManager == null) { Debug.LogError("MapSelectPanel：lobbyManager 未赋值"); return false; }
        if (mapCatalog == null) { Debug.LogError("MapSelectPanel：mapCatalog 未赋值"); return false; }
        if (mapCardsContent == null) { Debug.LogError("MapSelectPanel：mapCardsContent 未赋值"); return false; }
        if (mapCardPrefab == null) { Debug.LogError("MapSelectPanel：mapCardPrefab 未赋值"); return false; }
        if (selectedMapText == null) { Debug.LogError("MapSelectPanel：selectedMapText 未赋值"); return false; }
        if (backButton == null) { Debug.LogError("MapSelectPanel：backButton 未赋值"); return false; }
        return true;
    }
}
