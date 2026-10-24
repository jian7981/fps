using System;                       // Action
using System.Collections;           // IEnumerator
using System.Text;                  // Encoding（POST body）
using UnityEngine;
using UnityEngine.Networking;       // UnityWebRequest
using Shared;                       // DataManager（令牌走存档层，不直连 PlayerPrefs）

// 【阶段3-F】账号服务（客户端侧）
// 职责：注册 / 登录 / 登出 / 查档案 / 战绩上报；以及令牌的保存与自动登录。
//
// 设计要点：
//   · **复用 LobbyService.BaseUrl 这一个开关**：没配 -lobbyserver 时整个账号功能自动关闭，
//     局域网组队与单人练习流程一行都不用改（HasService 直接转交给 LobbyService）
//   · 令牌持久化走 DataManager.Store（IDataStore），不直接碰 PlayerPrefs
//   · 响应 DTO 的字段名必须小写 —— Unity 的 JsonUtility 区分大小写
//   · 战绩上报是**给服务端用的**（房间进程替每个玩家上报），所以要额外带一把服务器密钥，
//     玩家自己拿着令牌是调不通那个接口的（见后端 StatsEndpoints 的说明）
public static class AuthService
{
    private const string TokenKey = "auth_token";    // 本地存档里的键名
    private const int TimeoutSeconds = 5;

    // 开发用默认值；必须和后端的 STATS_REPORT_KEY / 房间进程的 -statsreportkey 一致
    private const string DefaultReportKey = "byb-report-2026";

    private static bool initialized;

    /// <summary>
    /// 手动初始化（读命令行 -lobbyserver / -statsreportkey，并从本地存档读出上次的令牌）。
    /// <para>各接口内部也会自动调用，这里暴露出来是为了让大厅能在第一帧就拿到正确的登录态
    /// —— 否则有令牌的玩家进游戏时会先闪一下"未登录"区再跳到"已登录"区。</para>
    /// </summary>
    public static void Init() => EnsureInit();

    // ---------- 当前登录态 ----------
    public static string CurrentToken { get; private set; } = "";
    public static long CurrentUserId { get; private set; }
    public static string CurrentNickname { get; private set; } = "";
    public static ProfileInfo CachedProfile { get; private set; }
    public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentToken);

    /// <summary>
    /// 【阶段4-G】当前头像编号（0 = 默认头像）。
    /// 还没拉到档案（刚启动、档案还在路上）时返回 0 —— 宁可先显示默认头像，也不要空引用。
    /// </summary>
    public static int CurrentAvatarId => (CachedProfile != null) ? CachedProfile.avatarId : 0;

    /// <summary>
    /// 【阶段4-H】升到下一级需要的经验（经验条的分母）**由后端下发**（ProfileInfo.expToNext）。
    /// <para>为什么要删掉原来这里的 <c>level * 100</c> 公式：等级曲线改成三段式后
    /// （Lv1~9 每级 400 / Lv10~29 每级 800 / Lv30+ 每级 1200），客户端再自己算一份
    /// 就一定会和后端各说各话 —— 表现为"经验条显示快满了，实际还差很远"。
    /// 现在唯一实现只在后端 Progression.cs 的 LevelCurve 里。</para>
    /// </summary>
    public static int ExpToNextOf(ProfileInfo info)
        => (info != null && info.expToNext > 0) ? info.expToNext : 1;

    /// <summary>
    /// 【阶段4-H】对局结算：服务器把经验下发到本机后，直接改本地缓存并广播。
    /// <para>为什么本地改而不是再拉一次档案：结算面板弹出来的瞬间就要显示新等级/新经验，
    /// 再走一次网络往返会慢一拍；而且服务器发来的就是最终结果，本地照抄即可。</para>
    /// </summary>
    public static void ApplyMatchReward(int level, int exp, int expToNext)
    {
        if (CachedProfile == null) return;
        CachedProfile.level = Mathf.Max(1, level);
        CachedProfile.exp = Mathf.Max(0, exp);
        CachedProfile.expToNext = (expToNext > 0) ? expToNext : 1;
        OnProfileChanged?.Invoke();
    }

    // 房间进程（服务端）上报战绩用的密钥
    public static string ReportKey { get; private set; } = DefaultReportKey;

    // 登录态 / 档案变化时广播（UI 订阅它刷新显示，不轮询）
    public static event Action OnProfileChanged;

    // 有没有配置账号服务地址（等同"有没有配 -lobbyserver"）
    public static bool HasService => LobbyService.HasService;

    private static void EnsureInit()
    {
        if (initialized) return;
        initialized = true;

        LobbyService.InitFromCommandLine();                                   // 复用同一个地址开关
        CurrentToken = DataManager.Store.GetString(TokenKey, "");             // 上次登录留下的令牌

        string key = ArgValue("-statsreportkey");
        if (!string.IsNullOrWhiteSpace(key)) ReportKey = key.Trim();
    }

    private static string ArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    // ==================== 注册 / 登录 / 登出 ====================

    // 注册。昵称留空 → 后端用用户名当昵称
    public static IEnumerator Register(string username, string password, string nickname,
                                       Action<AuthResult> onSuccess, Action<string> onFail = null)
        => AuthPost("/api/auth/register",
                    new AuthRequest { username = username, password = password, nickname = nickname ?? "" },
                    onSuccess, onFail);

    public static IEnumerator Login(string username, string password,
                                    Action<AuthResult> onSuccess, Action<string> onFail = null)
        => AuthPost("/api/auth/login",
                    new AuthRequest { username = username, password = password, nickname = "" },
                    onSuccess, onFail);

    private static IEnumerator AuthPost(string path, AuthRequest body,
                                        Action<AuthResult> onSuccess, Action<string> onFail)
    {
        EnsureInit();

        yield return PostJson(path, JsonUtility.ToJson(body), "", "",
            text =>
            {
                AuthResult result = Parse<AuthResult>(text);
                if (result == null || string.IsNullOrEmpty(result.token))
                {
                    onFail?.Invoke("返回内容无法解析");
                    return;
                }
                SaveToken(result.token, result.userId, result.nickname);
                onSuccess?.Invoke(result);
            },
            onFail);
    }

    // 登出：先把本地令牌清掉（立刻生效），再尽力通知后端吊销
    // （后端吊销失败也不影响本机 —— 令牌本来就有 30 天过期兜底）
    public static IEnumerator Logout()
    {
        EnsureInit();

        string token = CurrentToken;
        ClearToken();
        if (string.IsNullOrEmpty(token)) yield break;

        yield return PostJson("/api/auth/logout", "{}", token, "", _ => { }, _ => { });
    }

    // ==================== 档案 ====================

    public static IEnumerator GetProfile(Action<ProfileInfo> onSuccess, Action<string> onFail = null)
    {
        EnsureInit();
        if (!IsLoggedIn)
        {
            onFail?.Invoke("尚未登录");
            yield break;
        }

        yield return GetJson("/api/profile", CurrentToken,
            text =>
            {
                ProfileInfo info = Parse<ProfileInfo>(text);
                if (info == null)
                {
                    onFail?.Invoke("返回内容无法解析");
                    return;
                }
                ApplyProfile(info);
                onSuccess?.Invoke(info);
            },
            onFail);
    }

    // 启动时尝试自动登录：本地有令牌就拉一次档案；令牌已失效（401）就把本地令牌清掉
    public static IEnumerator AutoLogin(Action<string> onResult = null)
    {
        EnsureInit();
        if (!IsLoggedIn)
        {
            onResult?.Invoke("未登录");
            yield break;
        }

        yield return GetProfile(
            info => onResult?.Invoke("已自动登录：" + info.nickname),
            err =>
            {
                ClearToken();
                onResult?.Invoke("自动登录失败：" + err);
            });
    }

    // ==================== 头像（【阶段4-G】）====================

    // 改头像：只存编号（0 = 默认头像）。
    // 成功后**本地立刻改掉并广播**，不必再拉一次档案 —— 主菜单卡片 / 个人主页 / 房间玩家框
    // 全都订阅 OnProfileChanged 自动重画，所以这里一广播各处就同步了。
    public static IEnumerator SetAvatar(int avatarId, Action<int> onSuccess = null, Action<string> onFail = null)
    {
        EnsureInit();
        if (!IsLoggedIn)
        {
            onFail?.Invoke("尚未登录");
            yield break;
        }

        yield return PostJson("/api/profile/avatar",
            JsonUtility.ToJson(new AvatarRequest { avatarId = avatarId }), CurrentToken, "",
            text =>
            {
                AvatarResult result = Parse<AvatarResult>(text);
                if (result == null || !result.ok)
                {
                    onFail?.Invoke("返回内容无法解析");
                    return;
                }
                if (CachedProfile != null) CachedProfile.avatarId = result.avatarId;
                OnProfileChanged?.Invoke();
                onSuccess?.Invoke(result.avatarId);
            },
            onFail);
    }

    // ==================== 历史战绩（【阶段4-G】）====================

    // 拉本账号的最近对局。数据源是后端 match_report_log（一人一局一行，本来是为"上报幂等"建的）。
    // limit 会被后端夹在 1~50 之间（客户端说了不算数，也就是"别指望靠它把整张表拉下来"）。
    public static IEnumerator GetMatchHistory(int limit, Action<MatchRecord[]> onSuccess,
                                              Action<string> onFail = null)
    {
        EnsureInit();
        if (!IsLoggedIn)
        {
            onFail?.Invoke("尚未登录");
            yield break;
        }

        int n = Mathf.Clamp(limit, 1, 50);
        yield return GetJson("/api/matches?limit=" + n, CurrentToken,
            text =>
            {
                MatchHistoryResult result = Parse<MatchHistoryResult>(text);
                if (result == null)
                {
                    onFail?.Invoke("返回内容无法解析");
                    return;
                }
                onSuccess?.Invoke(result.matches ?? new MatchRecord[0]);
            },
            onFail);
    }

    // ==================== 战绩上报（服务端用）====================

    // 替某个玩家上报一局战绩。playerToken 是**那个玩家**的登录令牌
    // （房间进程在玩家进房时收到，存在服务端专属字段里，不会下发给其他人）。
    public static IEnumerator ReportMatchStats(string playerToken, string matchId, int teamId,
                                               int kills, int deaths, int assists, bool won,
                                               Action<MatchStatsResult> onSuccess = null,
                                               Action<string> onFail = null)
    {
        EnsureInit();

        var body = new MatchStatsReport
        {
            matchId = matchId,
            teamId = teamId,
            kills = kills,
            deaths = deaths,
            assists = assists,
            won = won
        };

        yield return PostJson("/api/stats/report", JsonUtility.ToJson(body), playerToken, ReportKey,
            text =>
            {
                MatchStatsResult result = Parse<MatchStatsResult>(text);
                if (result == null)
                {
                    onFail?.Invoke("返回内容无法解析");
                    return;
                }
                onSuccess?.Invoke(result);
            },
            onFail);
    }

    // ==================== 登录态维护 ====================

    private static void SaveToken(string token, long userId, string nickname)
    {
        CurrentToken = token;
        CurrentUserId = userId;
        CurrentNickname = nickname ?? "";

        DataManager.Store.SetString(TokenKey, token);
        DataManager.Store.Save();          // 立刻落盘：下次启动才能自动登录
        OnProfileChanged?.Invoke();
    }

    private static void ClearToken()
    {
        CurrentToken = "";
        CurrentUserId = 0;
        CurrentNickname = "";
        CachedProfile = null;

        DataManager.Store.SetString(TokenKey, "");
        DataManager.Store.Save();
        OnProfileChanged?.Invoke();
    }

    private static void ApplyProfile(ProfileInfo info)
    {
        CachedProfile = info;
        CurrentUserId = info.userId;
        CurrentNickname = info.nickname ?? "";
        OnProfileChanged?.Invoke();
    }

    // ==================== 内部：HTTP 收发（写法对齐 LobbyService）====================

    private static IEnumerator PostJson(string path, string json, string token, string reportKey,
                                        Action<string> onBody, Action<string> onFail)
    {
        if (!HasService)
        {
            onFail?.Invoke("未配置账号服务地址（用 -lobbyserver 指定）");
            yield break;
        }

        using (UnityWebRequest req = new UnityWebRequest(LobbyService.BaseUrl + path, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
            if (!string.IsNullOrEmpty(reportKey)) req.SetRequestHeader("X-Report-Key", reportKey);
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();

            string body = (req.downloadHandler != null) ? req.downloadHandler.text : "";
            if (req.result == UnityWebRequest.Result.Success) onBody?.Invoke(body);
            else onFail?.Invoke(ErrorMessage(body, "请求失败：" + req.error));
        }
    }

    private static IEnumerator GetJson(string path, string token, Action<string> onBody, Action<string> onFail)
    {
        if (!HasService)
        {
            onFail?.Invoke("未配置账号服务地址（用 -lobbyserver 指定）");
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequest.Get(LobbyService.BaseUrl + path))
        {
            if (!string.IsNullOrEmpty(token)) req.SetRequestHeader("Authorization", "Bearer " + token);
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();

            string body = (req.downloadHandler != null) ? req.downloadHandler.text : "";
            if (req.result == UnityWebRequest.Result.Success) onBody?.Invoke(body);
            else onFail?.Invoke(ErrorMessage(body, "请求失败：" + req.error));
        }
    }

    private static T Parse<T>(string json) where T : class
    {
        try { return JsonUtility.FromJson<T>(json); }
        catch { return null; }
    }

    // 后端失败时统一返回 {"error":"中文原因"}，这里取出来直接显示给玩家
    private static string ErrorMessage(string body, string fallback)
    {
        AuthError err = Parse<AuthError>(body);
        return (err != null && !string.IsNullOrEmpty(err.error)) ? err.error : fallback;
    }
}

// ==================== 数据结构（字段名必须和后端 JSON 一致：JsonUtility 区分大小写）====================

[Serializable]
public class AuthRequest
{
    public string username;
    public string password;
    public string nickname = "";   // 登录时不用，填空串即可
}

[Serializable]
public class AuthResult
{
    public string token;
    public long userId;
    public string username;
    public string nickname;
    public string expiresAt;
}

[Serializable]
public class ProfileInfo
{
    public long userId;
    public string username;
    public string nickname;
    public int level;
    public int exp;
    public int matches;
    public int wins;
    public int kills;
    public int deaths;
    public int assists;
    public int avatarId;   // 【阶段4-G】预设头像编号（0 = 默认）
    public int expToNext;  // 【阶段4-H】升到下一级还差多少（后端 LevelCurve 唯一给值）
}

// 【阶段4-G】改头像的请求 / 响应（字段名必须和后端 JSON 一致）
[Serializable]
public class AvatarRequest
{
    public int avatarId;
}

[Serializable]
public class AvatarResult
{
    public bool ok;
    public int avatarId;
}

// 【阶段4-G】历史战绩的一条。reportedAt 是 ISO 8601 的 **UTC** 时间串（后端特意带上了 Z 后缀），
// 显示前要自己 ToLocalTime() 转成本机时间 —— 玩家看到的一定得是他那边的时间。
[Serializable]
public class MatchRecord
{
    public string matchId;
    public int teamId;
    public int kills;
    public int deaths;
    public int assists;
    public bool won;
    public string reportedAt;
    public int expGain;    // 【阶段4-H】那一局实际入账的经验（老对局没有流水 = 0 → 不显示）
}

// JsonUtility **解析不了"根就是数组"的 JSON**，所以后端把列表包了一层 {"matches":[...]}
[Serializable]
public class MatchHistoryResult
{
    public MatchRecord[] matches;
}

[Serializable]
public class MatchStatsReport
{
    public string matchId;
    public int teamId;
    public int kills;
    public int deaths;
    public int assists;
    public bool won;
}

[Serializable]
public class MatchStatsResult
{
    public bool ok;
    public bool duplicated;   // true = 这一局已经报过，本次没累加（幂等生效）
    public int expGain;
    public bool leveledUp;
    public int level;
    public int exp;
    // 【阶段4-H】结算明细（字段名必须和后端 JSON 一致）
    public int expToNext;     // 升到下一级还要多少
    public int levelBefore;   // 结算前的等级（升了几级 = level - levelBefore）
    public int baseExp;       // 参与保底
    public int killExp;
    public int assistExp;
    public int winExp;
    public int dailyExp;      // 每日首胜
    public bool capped;       // 是否撞到单局 300 上限
}

[Serializable]
public class AuthError
{
    public string error;
}
