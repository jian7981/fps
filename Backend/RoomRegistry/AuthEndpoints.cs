using MySqlConnector;

// ==================== 账号接口 ====================
// 注册 / 登录 / 登出 / 查档案 / 改头像。
// 约定（和房间登记接口保持一致）：
//   - 返回 camelCase JSON（Unity 的 JsonUtility 只认小写字段名）
//   - 错误统一 {"error":"中文原因"}，客户端可以原样显示给玩家
static class AuthEndpoints
{
    // 请求头里带 "Authorization: Bearer <token>"
    private const string BearerPrefix = "Bearer ";

    // 【阶段4-G】预设头像编号上限（0 = 默认头像，所以数量是 MaxAvatarId + 1 个）
    // ⚠ 改了这里必须同步 Unity 侧 AvatarLibrary 资产里的 Sprite 数量，否则选了编号超范围的头像会被拒
    public const int MaxAvatarId = 11;

    public static void Map(WebApplication app)
    {
        // 注意：以 HttpContext 为入参的处理函数必须显式转成 Delegate。
        // 否则重载解析会挑中 RequestDelegate 那个重载（它的返回值是 Task），
        // 我们返回的 IResult 会被丢弃 —— 接口会返回 200 空响应体（ASP0016 警告说的就是这个）。
        app.MapPost("/api/auth/register", RegisterAsync);
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapPost("/api/auth/logout", (Delegate)LogoutAsync);
        app.MapGet("/api/profile", (Delegate)ProfileAsync);
        app.MapPost("/api/profile/avatar", (Delegate)SetAvatarAsync);   // 【阶段4-G】改头像
    }

    // ==================== 注册 ====================

    static async Task<IResult> RegisterAsync(RegisterRequest req)
    {
        // 先做基本校验，不合法的输入别让它打到数据库
        string username = (req.Username ?? "").Trim();
        string nickname = (req.Nickname ?? "").Trim();

        string? bad = CheckCredentials(username, req.Password);
        if (bad is not null) return Bad(bad);
        if (nickname.Length == 0) nickname = username;          // 没填昵称就用登录名
        if (nickname.Length > 32) return Bad("昵称最多 32 个字符");

        try
        {
            await using var conn = await Db.OpenAsync();

            byte[] salt = Db.NewSalt();
            byte[] hash = Db.HashPassword(req.Password!, salt, Db.Pbkdf2Iterations);

            await using (var cmd = new MySqlCommand(
                "INSERT INTO users (username, password_hash, salt, iterations, nickname) VALUES (@u, @h, @s, @i, @n)", conn))
            {
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@h", hash);
                cmd.Parameters.AddWithValue("@s", salt);
                cmd.Parameters.AddWithValue("@i", Db.Pbkdf2Iterations);
                cmd.Parameters.AddWithValue("@n", nickname);
                await cmd.ExecuteNonQueryAsync();
            }

            // LAST_INSERT_ID 是"本连接"范围，同一条连接上再查一次即可
            long userId;
            await using (var cmd = new MySqlCommand("SELECT LAST_INSERT_ID()", conn))
                userId = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            // 战绩表先给一行默认值：之后对局结束只做 UPDATE 累加，不必判"行在不在"
            await using (var cmd = new MySqlCommand("INSERT INTO player_stats (user_id) VALUES (@u)", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 【阶段4-G】展示资料同样先给一行默认值（avatar_id = 0 = 默认头像）：
            // 这样"改头像"永远只做 UPDATE，不用每次判行在不在
            await using (var cmd = new MySqlCommand("INSERT INTO player_profile (user_id) VALUES (@u)", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 注册成功直接把令牌发出去，省掉客户端再来一次登录的往返
            (string token, DateTime expiresAt) = await Db.IssueTokenAsync(userId, conn);
            return Results.Ok(new AuthResponse(token, userId, username, nickname, expiresAt.ToString("o")));
        }
        catch (MySqlException ex) when (ex.Number == 1062)   // 1062 = 唯一索引冲突
        {
            // 靠数据库的唯一约束兜底即可，不用"先查再插"（那样并发下仍有竞态）
            return Bad(ex.Message.Contains("nickname", StringComparison.OrdinalIgnoreCase)
                ? "昵称已被占用" : "用户名已存在", 409);
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
    }

    // ==================== 登录 ====================

    static async Task<IResult> LoginAsync(LoginRequest req)
    {
        string username = (req.Username ?? "").Trim();
        if (username.Length == 0 || string.IsNullOrEmpty(req.Password)) return Bad("请填写用户名和密码");

        try
        {
            await using var conn = await Db.OpenAsync();

            long userId; string dbUsername, nickname; byte[] hash, salt; int iterations; byte status;
            await using (var cmd = new MySqlCommand(
                "SELECT id, username, nickname, password_hash, salt, iterations, status FROM users WHERE username = @u", conn))
            {
                cmd.Parameters.AddWithValue("@u", username);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) return Bad("用户名或密码不对", 401);

                userId = reader.GetInt64(0);
                dbUsername = reader.GetString(1);
                nickname = reader.GetString(2);
                hash = (byte[])reader[3];
                salt = (byte[])reader[4];
                iterations = reader.GetInt32(5);
                status = reader.GetByte(6);
            }

            // 密码错也回同一句提示：不区分"用户不存在"和"密码不对"，避免被枚举账号
            if (!Db.VerifyPassword(req.Password, hash, salt, iterations)) return Bad("用户名或密码不对", 401);

            // 封禁检查刻意放在**密码校验之后**：不然谁都能靠"403 还是 401"判断某个账号存不存在
            if (status != 0) return Bad("账号已被封禁", 403);

            await using (var cmd = new MySqlCommand("UPDATE users SET last_login_at = UTC_TIMESTAMP() WHERE id = @u", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                await cmd.ExecuteNonQueryAsync();
            }

            (string token, DateTime expiresAt) = await Db.IssueTokenAsync(userId, conn);
            return Results.Ok(new AuthResponse(token, userId, dbUsername, nickname, expiresAt.ToString("o")));
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
    }

    // ==================== 登出 ====================

    static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        try
        {
            await Db.RevokeTokenAsync(BearerToken(ctx));
            return Results.Ok(new OkResponse(true));
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
    }

    // ==================== 查档案 ====================

    static async Task<IResult> ProfileAsync(HttpContext ctx)
    {
        try
        {
            string token = BearerToken(ctx);
            (long userId, byte status) = await Db.ResolveTokenAsync(token);
            if (userId == 0) return Bad("登录已失效，请重新登录", 401);

            // 和登录保持一致：封禁账号连自己的档案也不给看（否则就是个半截功能）
            if (status != 0) return Bad("账号已被封禁", 403);

            await using var conn = await Db.OpenAsync();
            await using var cmd = new MySqlCommand(
                @"SELECT u.id, u.username, u.nickname,
                         s.level, s.exp, s.matches, s.wins, s.kills, s.deaths, s.assists,
                         COALESCE(p.avatar_id, 0)
                  FROM users u
                  JOIN player_stats s ON s.user_id = u.id
                  LEFT JOIN player_profile p ON p.user_id = u.id
                  WHERE u.id = @u", conn);
            cmd.Parameters.AddWithValue("@u", userId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return Bad("账号不存在", 404);

            // 头像用 LEFT JOIN + COALESCE：**在这个表存在之前就注册的老账号**没有那一行，
            // 这里给 0（默认头像），绝不能因此让整个档案接口挂掉
            int level = reader.GetInt32(3);
            return Results.Ok(new ProfileResponse(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                level, reader.GetInt32(4), reader.GetInt32(5),
                reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9),
                reader.GetInt32(10),
                // 【阶段4-H】经验条分母由后端给：曲线只存在于 Progression.cs 一处，
                // 客户端不再自己写一份 level*100（否则一改曲线就两边对不上）
                LevelCurve.Need(level)));
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
    }

    // ==================== 改头像（【阶段4-G】）====================

    // 只改**自己**的头像。编号由客户端自报、服务器只做范围校验 ——
    // 头像不参与任何战斗判定，被伪造也只是改到自己头上，不值得为它加一层签名校验。
    static async Task<IResult> SetAvatarAsync(HttpContext ctx, AvatarRequest req)
    {
        try
        {
            string token = BearerToken(ctx);
            (long userId, byte status) = await Db.ResolveTokenAsync(token);
            if (userId == 0) return Bad("登录已失效，请重新登录", 401);
            if (status != 0) return Bad("账号已被封禁", 403);

            if (req.AvatarId < 0 || req.AvatarId > MaxAvatarId)
                return Bad($"头像编号超出范围（0~{MaxAvatarId}）");

            await using var conn = await Db.OpenAsync();
            // 用 INSERT ... ON DUPLICATE KEY UPDATE 而不是纯 UPDATE：
            // 万一这行因为历史原因不存在（表是后加的），一次就能补上，不必先查再判断
            await using (var cmd = new MySqlCommand(
                @"INSERT INTO player_profile (user_id, avatar_id) VALUES (@u, @a)
                  ON DUPLICATE KEY UPDATE avatar_id = @a", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@a", req.AvatarId);
                await cmd.ExecuteNonQueryAsync();
            }

            return Results.Ok(new AvatarResponse(true, req.AvatarId));
        }
        catch (Exception ex) { return ServiceUnavailable(ex); }
    }

    // ==================== 小工具 ====================

    static string BearerToken(HttpContext ctx)
    {
        string header = ctx.Request.Headers.Authorization.ToString();   // 没有这个头时是空串
        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? header.Substring(BearerPrefix.Length).Trim()
            : "";
    }

    static string? CheckCredentials(string username, string? password)
    {
        if (username.Length < 3 || username.Length > 32) return "用户名需 3~32 个字符";
        if (string.IsNullOrEmpty(password) || password.Length < 6 || password.Length > 64)
            return "密码需 6~64 个字符";
        return null;
    }

    static IResult Bad(string message, int statusCode = 400) =>
        Results.Json(new ApiError(message), statusCode: statusCode);

    static IResult ServiceUnavailable(Exception ex)
    {
        Console.WriteLine("[账号] 数据库异常：" + ex.Message);
        return Results.Json(new ApiError("数据库暂时不可用"), statusCode: 503);
    }
}

// ==================== 数据类型 ====================
// 属性名用小写开头是不必要的（ASP.NET Core 默认会把 PascalCase 输出成 camelCase）；
// 客户端 Unity 侧的字段名必须写成小写，才能被 JsonUtility 认出来。
record RegisterRequest(string? Username, string? Password, string? Nickname);
record LoginRequest(string? Username, string? Password);
record AuthResponse(string Token, long UserId, string Username, string Nickname, string ExpiresAt);
record ProfileResponse(long UserId, string Username, string Nickname,
                       int Level, int Exp, int Matches, int Wins, int Kills, int Deaths, int Assists,
                       int AvatarId, int ExpToNext);
record AvatarRequest(int AvatarId);
record AvatarResponse(bool Ok, int AvatarId);
record OkResponse(bool Ok);
record ApiError(string Error);
