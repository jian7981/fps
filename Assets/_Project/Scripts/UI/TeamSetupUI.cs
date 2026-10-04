using System.Collections.Generic;   // List<>
using UnityEngine;
using UnityEngine.UI;               // Button / Image / Toggle
using TMPro;                        // TextMeshProUGUI
using Shared;                       // Constants / PlayerSlot / MatchConfig

// ============ 组队界面 ============
// 10 个格子：前 5 格 A队、后 5 格 B队；第 1 格固定是"你"
// 点格子：空位 ⇄ AI 来回切换
// 【自动匹配AI】开：立刻把空位全部填成 AI；关：保持现状
// 【开始】：把当前配置打包成 MatchConfig，交给 LobbyManager 进图
// 【匹配】：联机占位
// 挂载：挂在 TeamPanel 上
public class TeamSetupUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyUI lobbyUI;             // 返回时切回主界面
    public LobbyManager lobbyManager;   // 开始游戏走它

    [Header("格子")]
    public Button slotButtonPrefab;     // 格子按钮预制体（从 Project 里拖 SlotButton.prefab）
    public Transform slotsParent;       // 格子容器（挂 GridLayoutGroup 的 SlotsGrid）

    [Header("开关与按钮")]
    public Toggle autoFillToggle;       // 【自动匹配AI】开关
    public Button startButton;          // 【开始】
    public Button matchButton;          // 【匹配】
    public Button backButton;           // 【返回】

    [Header("提示文字")]
    public TextMeshProUGUI hintText;    // 面板下方的提示

    // ---- 运行时数据（和 10 个格子一一对应）----
    private List<PlayerSlot> slots = new List<PlayerSlot>();                // 每格的数据
    private List<TextMeshProUGUI> slotTexts = new List<TextMeshProUGUI>();  // 每格上的文字
    private List<Image> slotImages = new List<Image>();                     // 每格的背景图（用来改颜色）

    // ---- 几种状态的颜色（和游戏里的队伍色保持一致）----
    private readonly Color emptyColor = new Color(0.35f, 0.35f, 0.35f);      // 空位：深灰
    private readonly Color aiTeamAColor = new Color(0.25f, 0.55f, 1f);       // A队AI：蓝
    private readonly Color aiTeamBColor = new Color(1f, 0.32f, 0.28f);       // B队AI：红
    private readonly Color localPlayerColor = new Color(0.3f, 0.85f, 0.4f);  // 你自己：绿

    private void Start()
    {
        if (!CheckRefs()) return;

        BuildSlots();     // 1. 生成数据 + 复制出 10 个格子按钮
        BindEvents();     // 2. 绑定开关和按钮
        if (autoFillToggle.isOn) FillEmptySlotsWithAI();   // 3. 若开关默认是开的，同步一次
        RefreshAll();     // 4. 统一刷新一遍显示
    }

    // 生成 10 个格子的数据，并从预制体复制出 10 个按钮
    private void BuildSlots()
    {
        for (int i = 0; i < Constants.TotalSlotCount; i++)
        {
            // (1) 先建数据：前5格A队，后5格B队；第1格是你，其余都还是空位
            PlayerSlot slot = new PlayerSlot();
            slot.teamId = (i < Constants.TeamSize) ? 0 : 1;
            slot.isLocalPlayer = (i == 0);
            slot.isAI = false;
            slots.Add(slot);

            // (2) 复制一个格子按钮，塞进容器里（GridLayoutGroup 会自动排版）
            Button btn = Instantiate(slotButtonPrefab, slotsParent);
            btn.name = "Slot_" + i;                                        // 改名字方便在 Hierarchy 里看
            slotImages.Add(btn.GetComponent<Image>());                     // 按钮背景图
            slotTexts.Add(btn.GetComponentInChildren<TextMeshProUGUI>());  // 按钮上的文字

            // (3) 绑定点击事件。注意：必须先把 i 存到局部变量再进闭包，
            //     否则 10 个按钮点起来全都是同一个编号（C# 闭包的经典坑）
            int index = i;
            btn.onClick.AddListener(() => OnSlotClicked(index));
        }
    }

    // 绑定开关和三个按钮
    private void BindEvents()
    {
        autoFillToggle.onValueChanged.AddListener(OnAutoFillChanged);
        startButton.onClick.AddListener(OnStartClicked);
        matchButton.onClick.AddListener(OnMatchClicked);
        backButton.onClick.AddListener(() => lobbyUI.ShowMainPanel());
    }

    // 点击某个格子：空位 ⇄ AI
    private void OnSlotClicked(int index)
    {
        PlayerSlot slot = slots[index];

        // 你自己的格子不允许改成 AI
        if (slot.isLocalPlayer)
        {
            SetHint("这是你自己的位置，不能改成 AI");
            return;
        }

        slot.isAI = !slot.isAI;   // 空位 → AI；AI → 空位
        RefreshSlot(index);
        SetHint(slot.isAI ? "已放置 AI" : "已取消 AI（变成空位）");
    }

    // 自动匹配AI开关变化
    private void OnAutoFillChanged(bool isOn)
    {
        if (!isOn)
        {
            // 需求：关闭时"保持当前玩家配置"——所以什么都不改
            SetHint("已关闭自动匹配：保留当前配置，不再自动填人");
            return;
        }
        FillEmptySlotsWithAI();
        SetHint("已自动用 AI 填满所有空位");
    }

    // 把所有空位（除了你自己）填成 AI，并刷新画面
    private void FillEmptySlotsWithAI()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].isLocalPlayer && !slots[i].isAI)
            {
                slots[i].isAI = true;
            }
        }
        RefreshAll();
    }

    // 点击【开始】
    private void OnStartClicked()
    {
        // 如果开关开着，进图前再补一次（防止开了开关后又手动取消了几格）
        if (autoFillToggle.isOn) FillEmptySlotsWithAI();

        // 把界面上的 10 个格子打包成一份对局配置
        MatchConfig config = new MatchConfig();
        config.mapName = lobbyManager.selectedMapName;
        config.autoFillAI = autoFillToggle.isOn;
        config.slots = new List<PlayerSlot>(slots);   // 复制一份交给对局

        // 交给 LobbyManager：它会放进中转站，然后加载地图
        lobbyManager.StartMatch(config);
    }

    // 点击【匹配】（联机预留）
    private void OnMatchClicked()
    {
        lobbyManager.StartMatchmaking();     // 目前只会打印一条日志
        SetHint("联机匹配将在阶段2/3开放，当前请点【开始】玩单机 5v5");
    }

    // 刷新全部格子
    private void RefreshAll()
    {
        for (int i = 0; i < slots.Count; i++) RefreshSlot(i);
    }

    // 刷新单个格子的显示（文字 + 颜色）
    private void RefreshSlot(int index)
    {
        PlayerSlot slot = slots[index];
        TextMeshProUGUI text = slotTexts[index];
        Image image = slotImages[index];

        if (slot.isLocalPlayer)
        {
            // 你自己的格子
            text.text = "你";
            image.color = localPlayerColor;
            return;
        }

        // 队伍字母 + 队内编号：A1~A5、B1~B5
        string teamLetter = (slot.teamId == 0) ? "A" : "B";
        int numberInTeam = (index % Constants.TeamSize) + 1;
        text.text = teamLetter + numberInTeam + " " + (slot.isAI ? "AI" : "空位");

        // 按状态/队伍上色，一眼能看出来
        if (slot.isAI) image.color = (slot.teamId == 0) ? aiTeamAColor : aiTeamBColor;
        else image.color = emptyColor;
    }

    // 更新提示文字（顺便打日志，方便在 Console 里验证）
    private void SetHint(string message)
    {
        hintText.text = message;
        Debug.Log("[组队] " + message);
    }

    private bool CheckRefs()
    {
        if (lobbyUI == null) { Debug.LogError("TeamSetupUI：lobbyUI 未赋值"); return false; }
        if (lobbyManager == null) { Debug.LogError("TeamSetupUI：lobbyManager 未赋值"); return false; }
        if (slotButtonPrefab == null) { Debug.LogError("TeamSetupUI：slotButtonPrefab 未赋值（要拖 Project 里的预制体）"); return false; }
        if (slotsParent == null) { Debug.LogError("TeamSetupUI：slotsParent 未赋值"); return false; }
        if (autoFillToggle == null) { Debug.LogError("TeamSetupUI：autoFillToggle 未赋值"); return false; }
        if (startButton == null) { Debug.LogError("TeamSetupUI：startButton 未赋值"); return false; }
        if (matchButton == null) { Debug.LogError("TeamSetupUI：matchButton 未赋值"); return false; }
        if (backButton == null) { Debug.LogError("TeamSetupUI：backButton 未赋值"); return false; }
        if (hintText == null) { Debug.LogError("TeamSetupUI：hintText 未赋值"); return false; }
        return true;
    }
}