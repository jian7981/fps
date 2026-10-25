// ==================== 房间登记服务 ====================
// 作用：维护一张"房间码 → 房间地址(IP:端口) + 当前人数"的登记表，供三处使用：
//   ① 创建房间 —— 客户端从登记表里领一个空房间连进去（第一个进去的人自然成为房主）
//   ② 输码进房 —— 客户端用房间码查出地址后连接
//   ③ 房间列表 / 快速匹配 —— 客户端拉列表挑一个没满的房间
// （这正是《项目规划-定稿》阶段 3.3 说的：房间码与快速匹配共用同一张登记表）
//
// 形态：ASP.NET Core Minimal API + 内存字典，**房间登记不接数据库**。
//   理由：房间登记是"易失数据"——房间进程一关就该从列表消失，本来就不该进数据库。
//   账号/战绩走 MySQL（见同目录 Db.cs / AuthEndpoints.cs）：一个进程里两套互不耦合的职责。
//
// 启动：dotnet run            （默认监听 http://0.0.0.0:5000）
//       ROOM_REGISTRY_PORT=8080 dotnet run   （换端口）
//
// 接口：
//   POST   /api/rooms/heartbeat   房间注册 + 心跳（body 不带 code = 首次注册 → 服务端分配房间码返回）
//   DELETE /api/rooms/{code}      主动注销（房间空了 / 进程要关）
//   GET    /api/rooms             房间列表 → {"rooms":[...]}
//   GET    /api/rooms/{code}      按码查单房间（找不到返回 404）
//
// 账号接口（走 MySQL；数据库连不上只影响账号功能，房间登记照常服务）：
//   POST   /api/auth/register     注册（写库 + 直接发登录令牌）
//   POST   /api/auth/login        登录（发登录令牌）
//   POST   /api/auth/logout       登出（吊销当前令牌）
//   GET    /api/profile           查档案（请求头 Authorization: Bearer <token>）
//   POST   /api/stats/report      战绩上报（游戏服务器调用：Authorization + X-Report-Key 两样都要）
//
// ⚠️ 客户端 Unity 侧用 JsonUtility 解析，所以：
//    - 字段一律小写（靠 ASP.NET Core 默认的 camelCase 输出策略，C# 属性保持 PascalCase 即可）
//    - 列表额外包一层 {"rooms":[...]}（JsonUtility 不支持顶层数组）

using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// 监听 0.0.0.0：这样局域网/公网才能访问（默认只监听 localhost，外部连不上）
int port = 5000;
string? envPort = Environment.GetEnvironmentVariable("ROOM_REGISTRY_PORT");
if (envPort is not null && int.TryParse(envPort, out int parsedPort)) port = parsedPort;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// ==================== 登记表 ====================
// 房间码字符集：去掉容易看混的 0/O/1/I（要口头念给朋友的）
const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
const int CodeLength = 4;
const int TtlSeconds = 30;      // 超过这么久没心跳 → 视为失效（兜底：防房间进程崩溃后留下僵尸房）

var rooms = new ConcurrentDictionary<string, RoomEntry>(StringComparer.OrdinalIgnoreCase);
var random = new Random();

// 清掉过期房间。房间数量很少（几十个量级），每次请求顺手清一遍就够，不用开后台定时器
void Sweep()
{
    DateTime now = DateTime.UtcNow;
    foreach (var kv in rooms)
        if ((now - kv.Value.LastSeenUtc).TotalSeconds > TtlSeconds)
            rooms.TryRemove(kv.Key, out _);
}

// 生成一个没被占用的房间码
string NewCode()
{
    for (int attempt = 0; attempt < 200; attempt++)
    {
        char[] chars = new char[CodeLength];
        for (int i = 0; i < chars.Length; i++) chars[i] = CodeAlphabet[random.Next(CodeAlphabet.Length)];
        string code = new string(chars);
        if (!rooms.ContainsKey(code)) return code;
    }
    // 极端情况（4 位码全被占满）：退化成 6 位随机串
    return Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
}

// ==================== 接口 ====================

// 注册 / 心跳。要点：
//  - 公网 IP 优先用 HTTP 请求来源 IP 回填（云机上这就是朋友该拨的地址）；显式传了 ip 就用它（多网卡/反代/本地测试）
//  - code 为空 = 首次注册 → 服务端分配；code 非空 = 心跳续命。
//    如果服务端重启过、字典里已经没有这个 code，也照样按它登记（**房间码保持不变**，玩家无感）
app.MapPost("/api/rooms/heartbeat", (HeartbeatRequest req, HttpContext ctx) =>
{
    Sweep();

    string ip = !string.IsNullOrWhiteSpace(req.Ip)
        ? req.Ip.Trim()
        : (ctx.Connection.RemoteIpAddress?.ToString() ?? "");

    string code = string.IsNullOrWhiteSpace(req.Code)
        ? NewCode()
        : req.Code.Trim().ToUpperInvariant();

    rooms[code] = new RoomEntry(code, ip, req.Port, req.Players, req.MaxPlayers, req.InGame, DateTime.UtcNow);
    return Results.Ok(new HeartbeatResponse(code, ip, req.Port, TtlSeconds));
});

// 主动注销：房间人走光了就立刻调它 —— 让房间马上从列表消失，不让朋友看到空壳房
app.MapDelete("/api/rooms/{code}", (string code) =>
{
    rooms.TryRemove(code.ToUpperInvariant(), out _);
    return Results.NoContent();
});

// 房间列表（房间列表 UI / 快速匹配用）
app.MapGet("/api/rooms", () =>
{
    Sweep();
    DateTime now = DateTime.UtcNow;
    var list = rooms.Values
        .OrderBy(r => r.Code, StringComparer.Ordinal)
        .Select(r => new RoomItem(r.Code, r.Ip, r.Port, r.Players, r.MaxPlayers, r.InGame,
                                  (int)(now - r.LastSeenUtc).TotalSeconds))
        .ToList();
    return Results.Ok(new RoomListResponse(list));
});

// 按房间码查单个房间（输码进房用）
app.MapGet("/api/rooms/{code}", (string code) =>
{
    Sweep();
    if (!rooms.TryGetValue(code.ToUpperInvariant(), out var r))
        return Results.NotFound();

    return Results.Ok(new RoomItem(r.Code, r.Ip, r.Port, r.Players, r.MaxPlayers, r.InGame, 0));
});

// 【账号】挂上账号路由，并在后台探测一次数据库。
// 用"发出去不管"而不是 await：数据库不可达时建连可能要等十几秒，
// 不能让它拖住房间登记服务的启动。
AuthEndpoints.Map(app);
StatsEndpoints.Map(app);
CharacterEndpoints.Map(app);   // 角色/大厅装扮选择
_ = Db.ReportStartupStatusAsync();

Console.WriteLine($"房间登记服务已启动：http://0.0.0.0:{port}（Ctrl+C 停止）");
app.Run();

// ==================== 数据类型 ====================
// 内部记录：只在服务端用，不直接返回给客户端
record RoomEntry(string Code, string Ip, int Port, int Players, int MaxPlayers, bool InGame, DateTime LastSeenUtc);

// 以下类型会被序列化成 JSON 回给客户端（camelCase 输出 → 字段名正好是 Unity JsonUtility 要的小写）
record HeartbeatRequest(string? Code, string? Ip, int Port, int Players, int MaxPlayers, bool InGame);
record HeartbeatResponse(string Code, string Ip, int Port, int TtlSeconds);
record RoomItem(string Code, string Ip, int Port, int Players, int MaxPlayers, bool InGame, int AgeSec);
record RoomListResponse(IEnumerable<RoomItem> Rooms);
