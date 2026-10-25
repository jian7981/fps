using System.Collections.Generic;   // HashSet（出生点占用记录）
using UnityEngine;                  // Time / Debug / Transform
using Mirror;
// 注意：不要 using kcp2k —— 它里面也有个 Utils，会和 Mirror.Utils 撞名（用全名引用 kcp2k.KcpTransport 即可）

// 【阶段2.1】游戏网络管理器
// 【阶段2.4-A】升级为 NetworkRoomManager：大厅即房间（Lobby=RoomScene，map1=GameplayScene）
// Transport（KCP）、Player Prefab、Online/Offline Scene、房间设置 全部在 Inspector 配置。
// 【阶段2.4-C】全员准备后由房主在房间面板点"开始游戏"；出生点按"队伍 + 座位号"分配并避让已占用点位。
public class GameNetworkManager : NetworkRoomManager
{
    // 【阶段2.4-C 扩展】本局已被占用的出生点：座位映射优先，避免两个玩家站同一个出生点；
    // 每次场景切换时清空（为下一局重新累计）
    private readonly HashSet<Transform> usedSpawnPoints = new HashSet<Transform>();

    // 【阶段5】角色选择：按 RoomPlayer 上报的 characterId，从目录里取对应的战斗预制体。
    // 显式字段留空时，用 LobbyCharacterSystem 留下的跨场景静态目录（Lobby 场景销毁后仍可用）。
    [Header("角色选择（战斗外观）")]
    [SerializeField] private CharacterCatalog characterCatalog;

    // ================= 【阶段3-房间码】登记服务相关 =================
    private string publicIpOverride = "";   // -publicip：显式指定对外 IP（留空 = 让登记服务用请求来源 IP 回填）
    private int roomPort = 7777;            // 本房间对外端口（来自 -port）
    private string roomCode = "";           // 服务器权威房间码（登记成功后写入，并下发给各端 RoomPlayer）
    private float nextHeartbeatTime;        // 下次心跳时刻
    private bool heartbeatInFlight;         // 有没有心跳请求正在飞（避免卡顿时叠一堆请求）

    // ================= 【阶段3-云服务器】命令行启动模式 =================
    // 用法（打包版 exe）：
    //   "Blow up your brother.exe" -servermode               → 专用服务器（无本地玩家，纯服务端，默认端口 7777）
    //   "Blow up your brother.exe" -servermode -port 7778    → 指定端口
    // 云机上：一个进程 = 一个房间；多开进程（端口不同）= 多房间。
    // 编辑器 / 不带参数的普通启动：行为完全不变。
    // ⚠ 注意：不能用 -server —— 那是 Unity 播放器的内置参数（要求跟 <slave_count> <server_ip:port> 两个参数，
    //   参数不全会直接让进程退出；实测踩过这个坑），所以自定义参数统一用 -servermode。
    public override void Start()
    {
        // 【阶段5】Mirror 的动态生成预制体必须在 Server/Client 启动前注册；
        // 否则客机会报 "Failed to spawn server object ... assetId=..."，
        // 表现就是房主能进图、另一个玩家卡在房间/加载画面。
        // 在 base.Start() 之前把所有角色的 combatPrefab 加进 spawnPrefabs。
        RegisterCharacterCombatPrefabs();

        base.Start();

        // 【阶段3-房间码】读 -lobbyserver（服务器与客户端共用同一个参数名）；
        // 没配就是空 → 全流程不启用房间登记（不影响原来的局域网玩法）
        LobbyService.InitFromCommandLine();

        string[] args = System.Environment.GetCommandLineArgs();
        if (!HasArg(args, "-servermode")) return;

        ushort port = 7777;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-port" && ushort.TryParse(args[i + 1], out ushort p)) port = p;
        }

        roomPort = port;
        publicIpOverride = GetArgValue(args, "-publicip");   // 【阶段3-房间码】留空则用请求来源 IP

        // 端口写进 KcpTransport（transport 挂在同一个物体上）再启动
        kcp2k.KcpTransport kcp = transport as kcp2k.KcpTransport;
        if (kcp != null) kcp.Port = port;

        if (!NetworkServer.active && !NetworkClient.active)   // 防重复启动（基类的自动启动开关先跑过）
        {
            Debug.Log($"[服务器] 专用服务器模式启动，端口 {port}（等待玩家加入…）");
            StartServer();
        }
    }

    private static bool HasArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
            if (args[i] == name) return true;
        return false;
    }

    // 取形如 `-名字 值` 的参数值；没有就返回空串
    private static string GetArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return "";
    }

    // ================= 【阶段3】玩家断开时的服务器侧处理 =================
    // ① 房主（第一个进房的人）断开时，把房主身份交给场上剩下的第一个人，避免"没人能点开始"
    // ② 空转兜底（见 Update）：对局中玩家全部离开后，把专用服务器带回房间场景
    // 注意：Mirror 调用本回调时，"正在断开的那个人"已经被移出 roomSlots
    //（房间场景走 conn.identity、对局场景走 conn.owned —— RoomPlayer 以 KeepAuthority 一直挂在连接名下），
    //       所以这里看不到离开者，只能按"剩下的人里还有没有房主"判断要不要移交
    public override void OnRoomServerDisconnect(NetworkConnectionToClient conn)
    {
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null) return;

        // ---- 房主移交：房里没房主了（走的就是房主）且还有人 → 交给第一个剩下的人 ----
        RoomPlayer firstOther = null;
        bool anyOwner = false;
        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp == null) continue;
            if (firstOther == null) firstOther = rp;
            if (rp.isRoomOwner) anyOwner = true;
        }

        if (!anyOwner && firstOther != null)
        {
            firstOther.isRoomOwner = true;
            Debug.Log($"[房间] 房主离开了，移交给 {firstOther.displayName}");
        }
    }

    // 【阶段3】专用服务器空转兜底：对局场景里一名玩家都不剩时，把服务器带回房间场景。
    // 不回来的话：云服务器上没人操作它，会永远卡在 map1（黑屏），新玩家再点"加入房间"会被
    // "Not in Room scene...disconnecting" 直接踢掉 —— 表现就是"退出后再也加不进房间"。
    // 用每帧兜底而不是只在断开回调里打标记：断开回调可能正好发生在场景切换途中，只依赖它有漏网风险；
    // 三个条件都很便宜，每帧判断开销可忽略。loadingSceneAsync 守卫保证切场景期间不会重复触发。
    public override void Update()
    {
        base.Update();

        UpdateRoomRegistration();   // 【阶段3-房间码】登记 / 心跳 / 注销

        if (!NetworkServer.active) return;                        // 不是服务器（单机 / 纯客户端）：不用管
        if (NetworkServer.connections.Count > 0) return;          // 场上还有人：对局继续，别动
        if (Utils.IsSceneActive(RoomScene)) return;               // 已经在房间场景：正常状态
        if (NetworkManager.loadingSceneAsync != null) return;     // 场景切换进行中：等切换完再看

        Debug.Log("[服务器] 场上没有玩家了，服务器返回房间场景");
        ServerChangeScene(RoomScene);
    }

    // ================= 【阶段3-房间码】房间登记（房间码 / 房间列表的服务器侧）=================
    // 【为什么空房间也要登记】云机上的房间是"预先开好的进程池"（一进程=一房间）。
    //   "创建房间" = 客户端从登记表里领一个**空闲房间**连进去（第一个进去的人就是房主）——
    //   所以空房间必须出现在登记表里，否则"创建房间"根本找不到可用的房间。
    //   能不能加入由列表里的 players / inGame 字段区分：空房 = 可创建，有人未满 = 可加入，对局中 = 不可进。
    //   进程崩了/断网留下的幽灵房间，由后端的 TTL（30 秒没心跳就清）兜底。
    private void UpdateRoomRegistration()
    {
        if (!NetworkServer.active || !LobbyService.HasService) return;
        if (NetworkManager.loadingSceneAsync != null) return;   // 切场景途中人数和场景状态都不准，等切完再算

        // 首次注册必须立即发（不能等下一个心跳周期）：房间码要先于玩家的查询进到登记表
        if (string.IsNullOrEmpty(roomCode) || Time.time >= nextHeartbeatTime) SendHeartbeat();

        SyncRoomCodeToPlayers();   // 房间码下发给各端（含"拿到码之后才进来"的人）
    }

    private void SendHeartbeat()
    {
        if (heartbeatInFlight) return;          // 上一次还没回来就不叠请求
        heartbeatInFlight = true;
        nextHeartbeatTime = Time.time + 3f;     // 每 3 秒续命一次

        int players = NetworkServer.connections.Count;
        bool inGame = !Utils.IsSceneActive(RoomScene);   // 离开房间场景 = 已进对局（列表里要过滤掉，不让别人加入）

        StartCoroutine(LobbyService.Heartbeat(
            roomCode, publicIpOverride, roomPort, players, Shared.Constants.TotalSlotCount, inGame,
            res =>
            {
                heartbeatInFlight = false;
                if (res.code != roomCode)
                {
                    roomCode = res.code;   // 首次注册：服务端分配的房间码
                    Debug.Log($"[房间码] 登记成功：房间码 {roomCode}（{res.ip}:{res.port}）");
                    SyncRoomCodeToPlayers();
                }
                else
                {
                    nextHeartbeatTime = Time.time + 3f;
                }
            },
            err =>
            {
                // 失败（服务还没起 / 中途重启 / 网络抖动）→ 交给下个周期自动重试
                heartbeatInFlight = false;
                nextHeartbeatTime = Time.time + 1f;
                Debug.LogWarning($"[房间码] 登记/心跳失败，将自动重试：{err}");
            }));
    }

    // 房间空了：注销（只在进程正常停止时调，见 OnStopServer）
    private void UnregisterRoom()
    {
        Debug.Log($"[房间码] 服务器已停止，注销房间 {roomCode}");
        StartCoroutine(LobbyService.Unregister(roomCode));
        roomCode = "";
        SyncRoomCodeToPlayers();
    }

    // 把权威房间码同步给房间里的每个玩家（RoomPlayer.roomCode 是 SyncVar，各端房间面板读它显示）
    private void SyncRoomCodeToPlayers()
    {
        NetworkRoomManager room = NetworkManager.singleton as NetworkRoomManager;
        if (room == null) return;

        foreach (NetworkRoomPlayer basePlayer in room.roomSlots)
        {
            RoomPlayer rp = basePlayer as RoomPlayer;
            if (rp != null && rp.roomCode != roomCode) rp.roomCode = roomCode;
        }
    }

    // ================= 【阶段3-房间码】进程正常停止时把房间从登记表摘掉 =================
    // 否则朋友会在列表里看到一个其实已经关掉的房间。
    // 尽力而为：进程正在退出时协程未必跑得完，跑不完就交给后端 TTL 兜底（30 秒后自动清掉）
    public override void OnStopServer()
    {
        base.OnStopServer();
        if (!string.IsNullOrEmpty(roomCode)) UnregisterRoom();
    }

    // 【阶段2.3-B2】客户端断开时的收尾（主动停服 / 被动掉线都会走这里）：
    // 1) 恢复时间：对局结束时全场 timeScale=0 冻结，这里"解冻"，避免回大厅后菜单点不动
    // 2) "回大厅"不用写：Mirror 内置机制会在断开后自动加载 offlineScene（= Lobby），各端各自完成
    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Time.timeScale = 1f;
        Debug.Log("[网络] 与服务器断开，Mirror 将自动返回大厅（offlineScene）");
    }

    // 【阶段2.4-C】全员准备后不自动开局 —— 改为由房主在房间面板点"开始游戏"
    // （覆写后"故意不调用 base"：基类实现会立刻 ServerChangeScene(GameplayScene)）
    public override void OnRoomServerPlayersReady()
    {
        Debug.Log("[房间] 全员已准备，等待房主点开始…");
    }

    // 【阶段2.4-B2】回到房间场景时解冻：对局结束时全场 timeScale=0，
    // 若只靠点按钮的那台机器恢复，客机回房间后菜单会点不动 —— 所以在场景切换回调里各端统一恢复
    // 【阶段2.4-C 扩展】顺带清空"出生点占用记录"（下一局生成玩家时会重新累计）
    public override void OnRoomServerSceneChanged(string sceneName)
    {
        usedSpawnPoints.Clear();
        if (Utils.IsSceneActive(RoomScene)) Time.timeScale = 1f;
    }

    public override void OnRoomClientSceneChanged()
    {
        if (Utils.IsSceneActive(RoomScene)) Time.timeScale = 1f;
    }

    // 【阶段2.4-B】玩家进图时：把房间玩家的数据（队伍）带到游戏玩家身上
    // （这个回调发生在"生成之后、替换玩家对象之前"，队伍在这里写好，会随初始同步包发给各端）
    public override bool OnRoomServerSceneLoadedForPlayer(NetworkConnectionToClient conn, GameObject roomPlayer, GameObject gamePlayer)
    {
        RoomPlayer rp = roomPlayer.GetComponent<RoomPlayer>();
        Health h = gamePlayer.GetComponentInChildren<Health>();
        if (rp != null && h != null)
        {
            h.SetTeamId(rp.teamId);
            h.SetDisplayIndex(rp.joinOrder);   // 【阶段3-玩家名】把"进房顺序"带到游戏玩家身上（没登录时的兜底名）
            h.SetAccount(rp.displayName, rp.accountToken);   // 【阶段4】真昵称 + 令牌（令牌只留在服务器，用来上报战绩）
            Debug.Log($"[房间] {rp.displayName} 进入对局：{(rp.teamId == 0 ? "A" : "B")}队 {rp.seatIndex + 1} 号位");
        }
        return true;
    }

    // 【阶段2.4-B/C】按"队伍 + 座位号"分配出生点：
    // A队1号位 → A队出生点[0]、2号位 → [1] …；该点已被本局别人占用 → 顺延找本队下一个空的
    public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        RoomPlayer rp = roomPlayer.GetComponent<RoomPlayer>();
        int team = (rp != null) ? rp.teamId : 0;
        int seat = (rp != null) ? Mathf.Clamp(rp.seatIndex, 0, 4) : 0;

        // map1 刚加载完，找场景里的 GameManager 拿该队的出生点
        GameManager gm = FindObjectOfType<GameManager>();
        Transform[] list = (gm != null) ? ((team == 0) ? gm.teamASpawnPoints : gm.teamBSpawnPoints) : null;
        if (list == null || list.Length == 0) return null;   // 找不到就回退：基类默认逻辑（NetworkStartPosition 轮流）

        Transform spawn = PickSpawn(list, seat);
        usedSpawnPoints.Add(spawn);                          // 记为本局已占用

        // 【阶段5】按房间玩家上报的角色 id 选战斗预制体；未知/未配置 → 回退默认 XBot Player。
        string characterId = (rp != null) ? rp.characterId : "xbot";
        GameObject prefab = ResolveCombatPlayerPrefab(characterId);
        if (prefab == null) prefab = playerPrefab;
        GameObject gamePlayer = Instantiate(prefab, spawn.position, spawn.rotation);

        // 【扩展】把他的"专属出生点"记在 Health 上 → 之后死亡复活会优先回到这个点（被占则顺延）
        Health h = gamePlayer.GetComponentInChildren<Health>();
        if (h != null) h.SetRespawnPoint(spawn);

        Debug.Log($"[房间] {(team == 0 ? "A" : "B")}队 {seat + 1} 号位出生点：{spawn.name}；战斗角色：{characterId}（{(prefab != null ? prefab.name : "null")}）");
        return gamePlayer;   // 返回给基类去替换 roomPlayer
    }

    // 从角色目录解析战斗预制体；只允许目录里存在且 combatPrefab 已配置的 id。
    // 这个校验放在服务器侧：客户端只能上报 id，不能指定任意预制体。
    private GameObject ResolveCombatPlayerPrefab(string characterId)
    {
        CharacterCatalog catalog = characterCatalog != null ? characterCatalog : LobbyCharacterSystem.ActiveCatalog;
        if (catalog == null) return playerPrefab;

        CharacterDefinition def = catalog.Find(characterId);
        if (def != null && def.combatPrefab != null) return def.combatPrefab;

        return playerPrefab;
    }

    // 把目录里所有 combatPrefab 注册到 NetworkManager.spawnPrefabs。
    // 这是 Mirror 的硬性要求：服务器动态生成、但不在场景里的预制体，客机必须先认识 assetId。
    // 以后新增角色时只要配好 CharacterDefinition.combatPrefab，这里自动注册，不用再手动维护预制体列表。
    private void RegisterCharacterCombatPrefabs()
    {
        CharacterCatalog catalog = characterCatalog != null ? characterCatalog : LobbyCharacterSystem.ActiveCatalog;
        if (catalog == null || catalog.characters == null) return;

        int added = 0;
        for (int i = 0; i < catalog.characters.Length; i++)
        {
            CharacterDefinition def = catalog.characters[i];
            if (def == null || def.combatPrefab == null) continue;
            // playerPrefab 由 Mirror 基类单独注册；重复放进 spawnPrefabs 会触发
            // "Replacing existing prefab with assetId..." 警告，这里直接跳过。
            if (def.combatPrefab == playerPrefab) continue;
            if (spawnPrefabs.Contains(def.combatPrefab)) continue;

            spawnPrefabs.Add(def.combatPrefab);
            added++;
        }

        if (added > 0)
            Debug.Log($"[角色选择] 已注册 {added} 个战斗预制体到 NetworkManager.spawnPrefabs");
    }

    // 选出生点：优先"座位号 % 长度"对应的那个；被占用了就顺延找下一个空的；全被占了就返回座位对应的
    private Transform PickSpawn(Transform[] list, int seat)
    {
        int startIndex = seat % list.Length;
        for (int i = 0; i < list.Length; i++)
        {
            Transform candidate = list[(startIndex + i) % list.Length];
            if (candidate != null && !usedSpawnPoints.Contains(candidate)) return candidate;
        }
        return list[startIndex];
    }
}