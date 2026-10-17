using MySqlConnector;
using System.Security.Cryptography;   // FixedTimeEquals
using System.Text;                    // Encoding

// ==================== 战绩上报接口 ====================
// 调用方是**游戏服务器**（一局结束时替每个玩家上报一次），不是玩家客户端。
//
// 两道校验，缺一不可：
//   ① 服务器密钥（请求头 X-Report-Key）：证明"调用方是可信的游戏服务器"。
//      为什么光有玩家令牌不够 —— 令牌是玩家自己的，玩家拿着它就能直接调这个接口给自己刷战绩，
//      那"服务端权威上报"就名存实亡了。加一把服务器间共享的密钥，把两种身份分开。
//   ② 该玩家的登录令牌（请求头 Authorization）：证明"这局成绩算在谁头上"。
//
// 幂等：靠 match_report_log 的复合主键 (match_id, user_id)。
//   同一局的重复上报（超时重发、反复点"返回房间"）只会成功一次，战绩不重复累加。
//   刻意**不**用"受影响行数==0 就是重复"来判断：那个语义取决于驱动的 CLIENT_FOUND_ROWS 标志，
//   容易随驱动版本/连接参数变化而变；这里显式捕获主键冲突（MySQL 1062）最稳。
//
// 数值规则：exp 增量 = kills*10 + assists*5 + won*50（胜负权重最高）
//          升级：升到下一级需要 当前等级*100 经验（1 级 100、2 级 200…），溢出留到下一级
static class StatsEndpoints
{
    public const string ReportKeyHeader = "X-Report-Key";

    // 开发用默认值。后端可用环境变量 STATS_REPORT_KEY 覆盖，
    // 房间进程（Unity 侧）用启动参数 -statsreportkey 覆盖。**两边必须一致**。
    private static readonly string ReportKey =
        Environment.GetEnvironmentVariable("STATS_REPORT_KEY") ?? "byb-report-2026";

    // 单局单人的击杀/死亡/助攻合理上限，挡住"随便填个大数"
    private const int MaxStatValue = 200;

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/stats/report", (Delegate)ReportAsync);
    }

    static async Task<IResult> ReportAsync(StatsReportRequest req, HttpContext ctx)
    {
        if (!IsTrustedServer(ctx)) return Bad("上报密钥无效", 403);

        (long userId, byte status) = await Db.ResolveTokenAsync(BearerToken(ctx));
        if (userId == 0) return Bad("登录已失效，请重新登录", 401);
        if (status != 0) return Bad("账号已被封禁", 403);

        string matchId = (req.MatchId ?? "").Trim();
        if (matchId.Length == 0 || matchId.Length > 36) return Bad("matchId 不合法");
        if (req.TeamId < 0) return Bad("teamId 不合法");
        if (req.Kills < 0 || req.Kills > MaxStatValue) return Bad("kills 超出合理范围");
        if (req.Deaths < 0 || req.Deaths > MaxStatValue) return Bad("deaths 超出合理范围");
        if (req.Assists < 0 || req.Assists > MaxStatValue) return Bad("assists 超出合理范围");

        int expGain = req.Kills * 10 + req.Assists * 5 + (req.Won ? 50 : 0);

        try
        {
            await using var conn = await Db.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // ---- 幂等闸门：这一局这个人报过没有 ----
            bool duplicated = false;
            try
            {
                await using var cmd = new MySqlCommand(
                    @"INSERT INTO match_report_log
                          (match_id, user_id, team_id, kills, deaths, assists, won)
                      VALUES (@m, @u, @t, @k, @d, @a, @w)", conn, tx);
                cmd.Parameters.AddWithValue("@m", matchId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@t", req.TeamId);
                cmd.Parameters.AddWithValue("@k", req.Kills);
                cmd.Parameters.AddWithValue("@d", req.Deaths);
                cmd.Parameters.AddWithValue("@a", req.Assists);
                cmd.Parameters.AddWithValue("@w", req.Won ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1062)   // 1062 = 主键冲突
            {
                // 已经报过 → 一个字都不加，把当前等级/经验原样回给调用方
                duplicated = true;
            }

            if (duplicated)
            {
                (int dupLevel, int dupExp) = await ReadLevelExpAsync(conn, tx, userId);
                await tx.CommitAsync();
                return Results.Ok(new StatsReportResponse(true, true, 0, false, dupLevel, dupExp));
            }

            // ---- 累加累计战绩 ----
            await using (var cmd = new MySqlCommand(
                @"UPDATE player_stats SET
                      matches = matches + 1,
                      wins    = wins    + @w,
                      kills   = kills   + @k,
                      deaths  = deaths  + @d,
                      assists = assists + @a,
                      exp     = exp     + @gain
                  WHERE user_id = @u", conn, tx))
            {
                cmd.Parameters.AddWithValue("@w", req.Won ? 1 : 0);
                cmd.Parameters.AddWithValue("@k", req.Kills);
                cmd.Parameters.AddWithValue("@d", req.Deaths);
                cmd.Parameters.AddWithValue("@a", req.Assists);
                cmd.Parameters.AddWithValue("@gain", expGain);
                cmd.Parameters.AddWithValue("@u", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // ---- 升级结算（经验够了就把等级推上去，溢出留到下一级）----
            (int level, int exp) = await ReadLevelExpAsync(conn, tx, userId);
            int leveled = 0;
            int guard = 0;    // 防呆：每轮至少消耗 100 经验，正常绝不可能转到 1000 次
            while (exp >= level * 100 && guard++ < 1000)
            {
                exp -= level * 100;
                level++;
                leveled++;
            }

            if (leveled > 0)
            {
                await using var cmd = new MySqlCommand(
                    "UPDATE player_stats SET level = @l, exp = @e WHERE user_id = @u", conn, tx);
                cmd.Parameters.AddWithValue("@l", level);
                cmd.Parameters.AddWithValue("@e", exp);
                cmd.Parameters.AddWithValue("@u", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return Results.Ok(new StatsReportResponse(true, false, expGain, leveled > 0, level, exp));
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
    }

    static async Task<(int level, int exp)> ReadLevelExpAsync(MySqlConnection conn, MySqlTransaction tx, long userId)
    {
        await using var cmd = new MySqlCommand("SELECT level, exp FROM player_stats WHERE user_id = @u", conn, tx);
        cmd.Parameters.AddWithValue("@u", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (1, 0);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    // 共享密钥也要用固定时间比较：不要用"逐字节遇到不同就提前返回"那种写法
    static bool IsTrustedServer(HttpContext ctx)
    {
        byte[] provided = Encoding.UTF8.GetBytes(ctx.Request.Headers[ReportKeyHeader].ToString());
        byte[] expected = Encoding.UTF8.GetBytes(ReportKey);
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    static string BearerToken(HttpContext ctx)
    {
        const string prefix = "Bearer ";
        string header = ctx.Request.Headers.Authorization.ToString();
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header.Substring(prefix.Length).Trim()
            : "";
    }

    static IResult Bad(string message, int statusCode = 400) =>
        Results.Json(new ApiError(message), statusCode: statusCode);

    static IResult ServiceUnavailable(Exception ex)
    {
        Console.WriteLine("[战绩] 数据库异常：" + ex.Message);
        return Results.Json(new ApiError("数据库暂时不可用"), statusCode: 503);
    }
}

// ==================== 数据类型 ====================
record StatsReportRequest(string? MatchId, int TeamId, int Kills, int Deaths, int Assists, bool Won);
// duplicated = 这一局已经报过（本次没有任何累加）；leveledUp = 本次是否升级
record StatsReportResponse(bool Ok, bool Duplicated, int ExpGain, bool LeveledUp, int Level, int Exp);
