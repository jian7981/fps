using MySqlConnector;
using System.Security.Cryptography;   // FixedTimeEquals
using System.Text;                    // Encoding

// ==================== 战绩接口 ====================
// · POST /api/stats/report —— 调用方是**游戏服务器**（一局结束时替每个玩家上报一次），不是玩家客户端。
// · GET  /api/matches      —— 【阶段4-G】玩家查自己的"最近对局"，给个人主页的「历史战绩」页用。
//
// 上报接口的两道校验，缺一不可：
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
// 【阶段4-H】数值规则全部收进 Progression.cs（LevelCurve / ExpRules）：
//   单局经验 = 参与 20 + 击杀 8（第 11 杀起 4）+ 助攻 4 + 胜利 40 + 每日首胜 50，单局封顶 300
//   升级曲线 = Lv1~9 每级 400 / Lv10~29 每级 800 / Lv30+ 每级 1200，溢出留到下一级
//   每日首胜按 **UTC+8** 的日期在 player_daily 里认领（一天一次，赢了才有）
//   每局每人另写一行 exp_ledger 经验流水（历史战绩里的 +EXP 就查它）
static class StatsEndpoints
{
    public const string ReportKeyHeader = "X-Report-Key";

    // 开发用默认值。后端可用环境变量 STATS_REPORT_KEY 覆盖，
    // 房间进程（Unity 侧）用启动参数 -statsreportkey 覆盖。**两边必须一致**。
    private static readonly string ReportKey =
        Environment.GetEnvironmentVariable("STATS_REPORT_KEY") ?? "byb-report-2026";

    // 单局单人的击杀/死亡/助攻合理上限，挡住"随便填个大数"
    private const int MaxStatValue = 200;

    // 【阶段4-G】历史战绩每页条数（默认 20，最多 50）
    private const int DefaultHistoryLimit = 20;
    private const int MaxHistoryLimit = 50;

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/stats/report", (Delegate)ReportAsync);
        app.MapGet("/api/matches", (Delegate)HistoryAsync);   // 【阶段4-G】最近对局
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

        // "今天"按 **UTC+8** 切：国内玩家的每日首胜必须在本地零点刷新，不能用 UTC 零点
        // （UTC 零点 = 北京时间早上 8 点，玩家会觉得"怎么早上打一把首胜就重置了"）
        string day = DateTime.UtcNow.AddHours(8).ToString("yyyy-MM-dd");

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
                // duplicated 时各项分项都是 0、等级不变；客户端据此显示"这一局之前已结算"
                return Results.Ok(new StatsReportResponse(
                    true, true, 0, false, dupLevel, dupExp, LevelCurve.Need(dupLevel), dupLevel,
                    0, 0, 0, 0, 0, false));
            }

            // ---- 每日首胜：赢了才去认领当天的名额 ----
            // INSERT IGNORE 的语义是确定的：插进去 → 受影响行数 1；主键已存在 → 0。
            // 故意不用 INSERT ... ON DUPLICATE KEY UPDATE：那个的"受影响行数"会受
            // CLIENT_FOUND_ROWS 连接选项影响（本项目驱动默认就是 found rows），
            // 拿它判断"是不是第一次"会随驱动配置变化 —— 和上面幂等闸门同一个坑。
            bool firstWinToday = false;
            if (req.Won)
            {
                await using var cmd = new MySqlCommand(
                    "INSERT IGNORE INTO player_daily (user_id, day, first_win_bonus) VALUES (@u, @d, 1)",
                    conn, tx);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@d", day);
                firstWinToday = await cmd.ExecuteNonQueryAsync() > 0;
            }

            // ---- 算这一局的经验明细（公式全在 Progression.cs）----
            ExpBreakdown exp = ExpRules.Compute(req.Kills, req.Assists, req.Won, firstWinToday);

            // ---- 升级结算（溢出留到下一级）----
            (int levelBefore, int expBefore) = await ReadLevelExpAsync(conn, tx, userId);
            (int levelAfter, int expAfter, int leveled) =
                LevelCurve.Apply(levelBefore, expBefore, exp.Total);

            // ---- 累加累计战绩 ----
            await using (var cmd = new MySqlCommand(
                @"UPDATE player_stats SET
                      matches = matches + 1,
                      wins    = wins    + @w,
                      kills   = kills   + @k,
                      deaths  = deaths  + @d,
                      assists = assists + @a,
                      level   = @l,
                      exp     = @e
                  WHERE user_id = @u", conn, tx))
            {
                cmd.Parameters.AddWithValue("@w", req.Won ? 1 : 0);
                cmd.Parameters.AddWithValue("@k", req.Kills);
                cmd.Parameters.AddWithValue("@d", req.Deaths);
                cmd.Parameters.AddWithValue("@a", req.Assists);
                cmd.Parameters.AddWithValue("@l", levelAfter);
                cmd.Parameters.AddWithValue("@e", expAfter);
                cmd.Parameters.AddWithValue("@u", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // ---- 经验流水：历史战绩里的 "+EXP"、以后的补发核对都查它 ----
            await using (var cmd = new MySqlCommand(
                @"INSERT INTO exp_ledger (user_id, match_id, exp_gain, level_before, level_after)
                  VALUES (@u, @m, @g, @lb, @la)", conn, tx))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@m", matchId);
                cmd.Parameters.AddWithValue("@g", exp.Total);
                cmd.Parameters.AddWithValue("@lb", levelBefore);
                cmd.Parameters.AddWithValue("@la", levelAfter);
                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return Results.Ok(new StatsReportResponse(
                true, false, exp.Total, leveled > 0, levelAfter, expAfter, LevelCurve.Need(levelAfter),
                levelBefore, exp.Base, exp.Kill, exp.Assist, exp.Win, exp.Daily, exp.Capped));
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

    // ==================== 最近对局（【阶段4-G】）====================

    // 只查**自己**的记录（用令牌解析出的 userId，不接受"查别人"的参数）。
    //
    // 数据源是 match_report_log —— 这张表当初是为"上报幂等"建的（一人一局一行），
    // 现在顺带成了"对局历史"的现成数据源：不用另建表、也不用补历史数据。
    // 这也正是当初建它时留的那句话：它同时是"以后对局历史/战绩明细的现成挂载点"。
    static async Task<IResult> HistoryAsync(HttpContext ctx)
    {
        (long userId, byte status) = await Db.ResolveTokenAsync(BearerToken(ctx));
        if (userId == 0) return Bad("登录已失效，请重新登录", 401);
        if (status != 0) return Bad("账号已被封禁", 403);

        // 条数上限从 query 里取，但要**夹在自己规定的范围内**：
        // 不能让客户端传个大数字把整张表拉下来
        int limit = DefaultHistoryLimit;
        if (int.TryParse(ctx.Request.Query["limit"], out int asked) && asked > 0)
            limit = Math.Min(asked, MaxHistoryLimit);

        try
        {
            await using var conn = await Db.OpenAsync();
            // LEFT JOIN 经验流水：在这张表存在之前上报的老对局没有流水行 → COALESCE 给 0，
            // 客户端那一行就不显示 "+EXP"（绝不能因为老数据让整个历史接口挂掉）
            await using var cmd = new MySqlCommand(
                @"SELECT m.match_id, m.team_id, m.kills, m.deaths, m.assists, m.won, m.reported_at,
                         COALESCE(e.exp_gain, 0)
                  FROM match_report_log m
                  LEFT JOIN exp_ledger e ON e.user_id = m.user_id AND e.match_id = m.match_id
                  WHERE m.user_id = @u
                  ORDER BY m.reported_at DESC
                  LIMIT @n", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            cmd.Parameters.AddWithValue("@n", limit);

            var list = new List<MatchHistoryItem>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // ⚠ match_id 列是 CHAR(36)，而 MySqlConnector 的默认 GuidFormat 就是把 CHAR(36) 当 GUID
                // → 这里用 GetString(0) 会抛 "Unable to cast object of type 'System.Guid' to type 'System.String'"
                // （实测踩到）。统一走 GetValue().ToString()：不管驱动把它当 GUID 还是字符串都能读出来。
                DateTime reported = reader.GetDateTime(6);
                list.Add(new MatchHistoryItem(
                    Convert.ToString(reader.GetValue(0)) ?? "",
                    reader.GetInt32(1),                  // team_id
                    reader.GetInt32(2),                  // kills
                    reader.GetInt32(3),                  // deaths
                    reader.GetInt32(4),                  // assists
                    reader.GetInt32(5) != 0,             // won
                    // 库里存的是 UTC，但驱动返回的 DateTime.Kind 是 Unspecified →
                    // 直接 ToString("o") 出来的串**不带 Z 后缀**，客户端看不出这是 UTC 还是本地时间
                    // （实测：返回的是 "2026-10-17T15:50:57.228919"）。这里显式标成 UTC，格式才没有歧义。
                    DateTime.SpecifyKind(reported, DateTimeKind.Utc).ToString("o"),
                    reader.GetInt32(7)));                 // exp_gain（老记录 = 0）
            }

            // 注意要**包一层对象**（不能直接返回数组）：Unity 的 JsonUtility 解析不了根就是数组的 JSON
            return Results.Ok(new MatchHistoryResponse(list));
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
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

// duplicated  = 这一局已经报过（本次没有任何累加）
// leveledUp   = 本次是否升级
// expToNext   = 升到下一级还需要多少经验（客户端经验条的分母，不再自己算曲线）
// levelBefore = 结算前的等级（"Lv.9 → Lv.10" 用）
// Base/Kill/Assist/Win/Daily = 各分项（结算面板展示明细用）
// capped      = 本局是否撞到 300 上限（各分项之和 > ExpGain）
record StatsReportResponse(bool Ok, bool Duplicated, int ExpGain, bool LeveledUp, int Level, int Exp,
                           int ExpToNext, int LevelBefore, int BaseExp, int KillExp, int AssistExp,
                           int WinExp, int DailyExp, bool Capped);

// 【阶段4-G】最近对局的一条；ReportedAt 是 ISO 8601 的 UTC 时间串
// 【阶段4-H】ExpGain = 那一局实际入账的经验（老对局没有流水 → 0，客户端就不显示）
record MatchHistoryItem(string MatchId, int TeamId, int Kills, int Deaths, int Assists,
                        bool Won, string ReportedAt, int ExpGain);
// 包一层（Unity 的 JsonUtility 解析不了"根就是数组"的 JSON）
record MatchHistoryResponse(List<MatchHistoryItem> Matches);
