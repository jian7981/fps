using MySqlConnector;                  // MySqlConnection / MySqlCommand / MySqlException
using System.Security.Cryptography;    // RandomNumberGenerator / Rfc2898DeriveBytes / SHA256
using System.Text;                     // Encoding

// ==================== 数据库访问层 ====================
// 职责：连接串、密码哈希、登录令牌（生成 / 校验 / 过期清理）。
//
// 设计要点（面试可讲）：
//   ① 密码只存 PBKDF2(SHA-256) 派生密钥 + 每用户独立随机盐，库里没有任何明文；
//      iterations 单独存一列 → 以后想提高强度（比如 21 万次 → 60 万次）不影响老密码。
//   ② 令牌是"不透明随机串"，库里只存它的 SHA-256，原文只在注册/登录时回给客户端一次。
//      不用 JWT：无状态 JWT 无法即时吊销、还要管密钥轮换；这里一行 DELETE 就能登出/踢人。
//   ③ 过期令牌不靠后台定时器清理，照抄房间登记表的惯例——每次请求顺手 sweep。
//   ④ 数据库连不上时**不抛异常给外部、也不退出进程**：接口返回 503，
//      房间登记功能照常服务（保证日常联机调试不被账号功能拖累）。

static class Db
{
    public const int Pbkdf2Iterations = 210000;   // OWASP 对 PBKDF2-SHA256 的建议量级
    public const int TokenDays = 30;              // 令牌有效期

    // 连接串：优先环境变量 MYSQL_CONN；没有就退回本机默认值（方便本机开发）
    // 注意：默认账号是 schema.sql 建出来的**最小权限账号**，不是 root
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("MYSQL_CONN")
        ?? "Server=127.0.0.1;Port=3306;User ID=byb_app;Password=BybApp9x7K2q;Database=blowyourbrother;";

    // MySqlConnector 自带连接池，所以每次 new 一个连接就行，不必自己写池
    public static async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    // ==================== 密码 ====================

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(16);

    public static byte[] HashPassword(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    // 固定时间比较：避免按字节提前返回而泄露信息（面试常问）
    public static bool VerifyPassword(string password, byte[] expectedHash, byte[] salt, int iterations) =>
        CryptographicOperations.FixedTimeEquals(HashPassword(password, salt, iterations), expectedHash);

    // ==================== 令牌 ====================

    // 生成 32 字节随机串 → Base64Url（43 字符，可以直接放 URL / 请求头，不含 + / =）
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // 入库用的哈希（64 字符十六进制）
    public static string TokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // 发一个新令牌。复用调用方已有的连接，省一次建连
    public static async Task<(string token, DateTime expiresAt)> IssueTokenAsync(long userId, MySqlConnection conn)
    {
        string token = NewToken();
        DateTime expiresAt = DateTime.UtcNow.AddDays(TokenDays);

        await using var cmd = new MySqlCommand(
            "INSERT INTO tokens (token_hash, user_id, expires_at) VALUES (@h, @u, @e)", conn);
        cmd.Parameters.AddWithValue("@h", TokenHash(token));
        cmd.Parameters.AddWithValue("@u", userId);
        cmd.Parameters.AddWithValue("@e", expiresAt);
        await cmd.ExecuteNonQueryAsync();

        return (token, expiresAt);
    }

    // 校验令牌 → 返回 (user_id, status)。无效/过期返回 (0, 0)。顺手清掉过期令牌。
    // 为什么会连表取 status：查档案 / 战绩上报 都得判"这账号还能不能用"，
    // 统一在这里判，免得每个新接口各写一套、日后漏判一个。
    public static async Task<(long userId, byte status)> ResolveTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return (0, 0);

        await using var conn = await OpenAsync();

        await using (var sweep = new MySqlCommand("DELETE FROM tokens WHERE expires_at <= UTC_TIMESTAMP()", conn))
            await sweep.ExecuteNonQueryAsync();

        await using var cmd = new MySqlCommand(
            @"SELECT u.id, u.status
              FROM tokens t JOIN users u ON u.id = t.user_id
              WHERE t.token_hash = @h AND t.expires_at > UTC_TIMESTAMP()", conn);
        cmd.Parameters.AddWithValue("@h", TokenHash(token));

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (0, 0);
        return (reader.GetInt64(0), reader.GetByte(1));
    }

    // 登出：把这一条令牌删掉（其他设备的登录不受影响）
    public static async Task RevokeTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM tokens WHERE token_hash = @h", conn);
        cmd.Parameters.AddWithValue("@h", TokenHash(token));
        await cmd.ExecuteNonQueryAsync();
    }

    // ==================== 启动自检 ====================

    // 启动时打印一次数据库状态。连不上只警告，**不让整个后端挂掉**
    public static async Task ReportStartupStatusAsync()
    {
        try
        {
            await using var conn = await OpenAsync();
            await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM users", conn);
            int users = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Console.WriteLine($"[数据库] 已连接：{conn.Database}（已有账号 {users} 个）");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[数据库] 连接失败：" + ex.Message);
            Console.WriteLine("[数据库] 账号功能暂不可用；房间登记功能不受影响。");
        }
    }
}
