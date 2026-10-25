﻿﻿﻿using System;               // Action（大厅状态广播）
using System.Collections;   // IEnumerator（房间码 / 房间列表查询都是协程）
using UnityEngine;
using UnityEngine.SceneManagement;
using Shared;
using Mirror;   // 【阶段2.4-C】StartHost / StartClient / NetworkManager

// ============ 大厅逻辑管理器 ============
// 挂载：Lobby 场景里的 LobbyManager 物体上
public class LobbyManager : MonoBehaviour
{
    // 当前选中的地图场景名（初值就是 map1；地图选择界面会改它）
    // 用"属性初始化"而不是 Start 里赋值，保证任何时候读到的都不是 null
    public string selectedMapName { get; private set; } = Constants.GameSceneName1;

    // 【阶段3-D】大厅状态广播：创建房间 / 快速匹配 / 输码进房 的进度与失败原因都从这里发出去，
    // 由 LobbyUI 显示成一行状态文字 —— 否则这些过程只打 Console 日志，玩家点了按钮完全没反馈。
    public event Action<string> OnStatus;

    private void Status(string msg)
    {
        OnStatus?.Invoke(msg);
        Debug.Log("[大厅] " + msg);
    }

    private void Start()
    {
        // 【阶段3-房间码】读命令行 -lobbyserver：
        //   配了 → 启用公网模式（组队 = 领一个空房、快速匹配、输房间码进房）；
        //   没配 → 保持原来的局域网玩法（组队 = 自己开主机 StartHost），日常联机调试走这条
        LobbyService.InitFromCommandLine();

        // 【阶段3-F】本地有令牌就自动登录一次。
        // 没配 -lobbyserver 时它内部直接返回（不会发请求），对局域网/单人练习完全无影响。
        StartCoroutine(AuthService.AutoLogin(msg => Debug.Log("[账号] " + msg)));
    }

    // 【组队界面点"开始"走这里】带上配置开始对局
    public void StartMatch(MatchConfig config)
    {
        // 1. 把配置放进"中转站"，map1 场景里的 GameManager 会来取
        MatchConfigTransfer.Pending = config;

        // 2. 打印日志，方便验证大厅的配置有没有带过来
        Debug.Log("[大厅] 开始对局！地图：" + config.mapName
            + "｜A队 AI " + config.CountAI(0) + " 个｜B队 AI " + config.CountAI(1) + " 个");

        // 3. 加载地图场景（大厅会被销毁，中转站里的配置会活下来）
        SceneManager.LoadScene(selectedMapName);
    }

    // 旧入口：没经过组队界面时的保底开始方式（默认给你配满 9 个 AI）
    public void StartSinglePlayerGame()
    {
        StartMatch(MatchConfig.CreateDefault(selectedMapName));
    }

    // 【预留接口，阶段3联机用】快速匹配
    public void StartMatchmaking()
    {
        Debug.Log("[匹配] 快速匹配将在阶段3（公网部署）开放，当前是单机 Demo");
    }

    // 【预留接口，阶段2联机用】后面创建房间调用这个函数
    // 【阶段2.4-C】创建房间：本机成为房主（NetworkRoomManager 会把所有人留在 Lobby 房间）
    public void CreateRoom()
    {
        Status("创建房间（本机开主机）…");
        NetworkManager.singleton.StartHost();
    }

    // 【阶段2.4-C】加入房间：连接到指定 IP（留空 = localhost）
    // 【阶段3 改动】支持 "IP:端口" 写法（例：127.0.0.1:7778）——
    //   云服务器多开房间时，同一台机器上不同房间靠端口区分，客户端必须能指定端口；
    //   不写端口就用 transport 里配置的默认端口（Lobby 场景里是 7777）。
    public void JoinRoom(string ip)
    {
        ushort port = 0;

        // 解析可选端口：取最后一个冒号后面的数字（前面那截才是 IP）
        if (!string.IsNullOrWhiteSpace(ip))
        {
            int colon = ip.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(ip.Substring(colon + 1), out ushort parsed) && parsed > 0)
            {
                port = parsed;
                ip = ip.Substring(0, colon);
            }
        }

        ConnectTo(ip, port);
    }

    // 统一的连接出口：直连 IP、房间码进房、快速匹配 三条路最后都走这里
    private void ConnectTo(string ip, ushort port)
    {
        if (string.IsNullOrWhiteSpace(ip)) ip = "localhost";
        NetworkManager.singleton.networkAddress = ip;

        // 指定了端口 → 覆盖 transport 上的端口（和服务器 -servermode -port 用的是同一个字段）
        kcp2k.KcpTransport kcp = NetworkManager.singleton.transport as kcp2k.KcpTransport;
        if (kcp != null && port > 0) kcp.Port = port;

        Status("加入房间：" + ip + (port > 0 ? ":" + port : "（默认端口）") + " …");
        NetworkManager.singleton.StartClient();
    }

    // 【阶段3-D】拉一次房间列表给房间列表面板用（UI 不直接碰网络，统一从大厅逻辑层走）
    public void FetchRoomList(Action<RoomInfo[]> onDone)
    {
        StartCoroutine(FetchRoomListRoutine(onDone));
    }

    private IEnumerator FetchRoomListRoutine(Action<RoomInfo[]> onDone)
    {
        RoomInfo[] rooms = null;
        yield return LobbyService.GetRoomList(
            list => rooms = list,
            err => Status("拉取房间列表失败：" + err));
        onDone?.Invoke(rooms);   // 失败给 null，交给 UI 显示"服务不可用"
    }

    // 【阶段3-D】直接进列表里已经选中的房间：地址（ip:port）已经在手，不用像"输房间码"那样再查一次
    public void JoinListedRoom(RoomInfo room)
    {
        if (room == null) return;
        ConnectTo(room.ip, (ushort)room.port);
    }

    // ==================== 【阶段3-房间码】公网模式 ====================

    // 创建房间 = **领一个空房间**：从登记服务挑一个 0 人的房间连进去，
    // 因为"第一个进房的人就是房主"，所以进去之后自己就是房主，并拿到可分享的房间码。
    // （云机上的房间是预先开好的进程池；这一步不做加锁——两个人同时点最多一起进同一个空房，无害）
    public void CreateRoomOnline()
    {
        Status("正在领取空房间…");
        StartCoroutine(ClaimEmptyRoomRoutine());
    }

    // 快速匹配：优先挑"人最多但还没满"的房间（尽量把人凑到一房），没有未满的就挑空房
    public void QuickMatch()
    {
        Status("正在匹配…");
        StartCoroutine(QuickMatchRoutine());
    }

    // 按房间码进房（朋友拿到房主分享的 4 位码，输进来就能进）
    public void JoinRoomByCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) { Status("房间码为空"); return; }
        Status("正在查找房间码 " + code + " …");
        StartCoroutine(JoinByCodeRoutine(code.Trim().ToUpperInvariant()));
    }

    private IEnumerator ClaimEmptyRoomRoutine()
    {
        RoomInfo[] rooms = null;
        yield return LobbyService.GetRoomList(
            list => rooms = list,
            err => Status("拉取房间列表失败：" + err));

        if (rooms == null)
        {
            Status("房间登记服务不可用，无法创建房间");
            yield break;
        }

        RoomInfo pick = null;
        foreach (RoomInfo r in rooms)
        {
            if (r.inGame || r.players > 0) continue;   // 对局中 / 已经有人 → 跳过
            pick = r;
            break;
        }

        if (pick == null)
        {
            Status("当前没有空闲房间（云机上请多开几个房间进程）");
            yield break;
        }

        Status("领到空房间 " + pick.code + " → " + pick.ip + ":" + pick.port);
        ConnectTo(pick.ip, (ushort)pick.port);
    }

    private IEnumerator QuickMatchRoutine()
    {
        RoomInfo[] rooms = null;
        yield return LobbyService.GetRoomList(
            list => rooms = list,
            err => Status("拉取房间列表失败：" + err));

        if (rooms == null)
        {
            Status("房间登记服务不可用，无法快速匹配");
            yield break;
        }

        RoomInfo best = null;
        foreach (RoomInfo r in rooms)
        {
            if (r.inGame || r.players <= 0 || r.players >= r.maxPlayers) continue;   // 对局中 / 空房 / 已满 → 不是"凑人"对象
            if (best == null || r.players > best.players) best = r;                  // 优先挑人多的，尽快凑满一房
        }

        if (best == null)
        {
            // 没有可以凑人的半满房间 → 退化成"自己创建一个"（领一个空房，和 CreateRoomOnline 同样的处理）
            foreach (RoomInfo r in rooms)
            {
                if (r.inGame || r.players > 0) continue;
                best = r;
                break;
            }
        }

        if (best == null)
        {
            Status("暂时没有可加入的房间");
            yield break;
        }

        Status("匹配到房间 " + best.code + "（" + best.players + "/" + best.maxPlayers + "）");
        ConnectTo(best.ip, (ushort)best.port);
    }

    private IEnumerator JoinByCodeRoutine(string code)
    {
        RoomInfo info = null;
        yield return LobbyService.GetRoomByCode(
            code,
            found => info = found,
            err => Status("房间码 " + code + " 无效：" + err));

        if (info == null) yield break;

        Status("房间码 " + code + " → " + info.ip + ":" + info.port
               + "（" + info.players + "/" + info.maxPlayers + "）");
        ConnectTo(info.ip, (ushort)info.port);
    }

    // 记录选中的地图（地图选择界面调用）
    public void SelectMap(string mapName)
    {
        selectedMapName = mapName;
        Debug.Log("已选择地图：" + selectedMapName);
    }
}