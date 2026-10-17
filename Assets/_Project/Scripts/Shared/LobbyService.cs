using System;                       // Action / Environment
using System.Collections;           // IEnumerator
using System.Text;                  // Encoding（POST 的 body 用）
using UnityEngine;
using UnityEngine.Networking;       // UnityWebRequest

// 【阶段3-房间码】房间登记服务的客户端封装
// 后端在 Backend/RoomRegistry（四个接口），这里包成协程，调用方 StartCoroutine 驱动：
//   Heartbeat      上报 / 心跳（code 为空 = 首次注册 → 服务端分配房间码）
//   Unregister     主动注销（房间人走光了立刻调，让房间马上从列表消失）
//   GetRoomByCode  按房间码查房间（输码进房）
//   GetRoomList    房间列表（房间列表 UI / 快速匹配）
//
// 配置：BaseUrl 默认空 = **不启用公网模式**（走原来的局域网 StartHost，日常联机调试不受影响）；
//       命令行参数 `-lobbyserver http://主机:5000` 可覆盖（服务器进程与客户端共用同一个参数名）。
//
// ⚠️ JSON 字段名必须和后端输出一致（Unity 的 JsonUtility 区分大小写），后端输出是小写 camelCase

public static class LobbyService
{
    public static string BaseUrl = "";          // 例："http://1.2.3.4:5000"；留空 = 不走公网模式
    public const int TimeoutSeconds = 3;

    public static bool HasService => !string.IsNullOrWhiteSpace(BaseUrl);

    private static bool initialized;

    // 从命令行读 -lobbyserver（幂等：服务器和客户端谁先调都行）
    public static void InitFromCommandLine()
    {
        if (initialized) return;
        initialized = true;

        string url = ArgValue("-lobbyserver");
        if (!string.IsNullOrWhiteSpace(url)) BaseUrl = url.Trim().TrimEnd('/');
    }

    private static string ArgValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    // ==================== 对外四个接口 ====================

    // 注册 / 心跳。code 为空 → 服务端分配房间码并在 onSuccess 里回给你
    // ipOverride 留空 → 让服务端用 HTTP 请求来源 IP 回填（云机上这就是朋友该拨的公网 IP）
    public static IEnumerator Heartbeat(string code, string ipOverride, int port, int players, int maxPlayers, bool inGame,
                                        Action<HeartbeatResponse> onSuccess, Action<string> onFail = null)
    {
        var body = new HeartbeatRequest
        {
            code = code ?? "",
            ip = ipOverride ?? "",
            port = port,
            players = players,
            maxPlayers = maxPlayers,
            inGame = inGame
        };
        yield return PostJson("/api/rooms/heartbeat", JsonUtility.ToJson(body), onSuccess, onFail);
    }

    // 主动注销（房间空了 / 进程要关）
    public static IEnumerator Unregister(string code)
    {
        if (!HasService || string.IsNullOrEmpty(code)) yield break;

        using (UnityWebRequest req = UnityWebRequest.Delete(BaseUrl + "/api/rooms/" + code))
        {
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();
            // 注销失败不重试：TTL 兜底也会把它清掉
        }
    }

    // 按房间码查房间（输码进房用）
    public static IEnumerator GetRoomByCode(string code, Action<RoomInfo> onFound, Action<string> onFail)
    {
        yield return Get("/api/rooms/" + code, text =>
        {
            RoomInfo info = Parse<RoomInfo>(text);
            if (info != null && !string.IsNullOrEmpty(info.code)) onFound?.Invoke(info);
            else onFail?.Invoke("返回内容无法解析");
        }, onFail);
    }

    // 房间列表（房间列表 UI / 快速匹配用；失败给空数组而不是 null，调用方少判一次）
    public static IEnumerator GetRoomList(Action<RoomInfo[]> onSuccess, Action<string> onFail)
    {
        yield return Get("/api/rooms", text =>
        {
            RoomListWrapper wrapper = Parse<RoomListWrapper>(text);
            onSuccess?.Invoke(wrapper != null && wrapper.rooms != null ? wrapper.rooms : new RoomInfo[0]);
        }, onFail);
    }

    // ==================== 内部：HTTP 收发 ====================

    private static IEnumerator Get(string path, Action<string> onBody, Action<string> onFail)
    {
        if (!HasService) { onFail?.Invoke("未配置房间登记服务地址"); yield break; }

        using (UnityWebRequest req = UnityWebRequest.Get(BaseUrl + path))
        {
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
                onBody?.Invoke(req.downloadHandler.text);
            else
                onFail?.Invoke(req.responseCode == 404 ? "房间不存在或已关闭" : req.error);
        }
    }

    private static IEnumerator PostJson(string path, string json, Action<HeartbeatResponse> onSuccess, Action<string> onFail)
    {
        if (!HasService) { onFail?.Invoke("未配置房间登记服务地址"); yield break; }

        using (UnityWebRequest req = new UnityWebRequest(BaseUrl + path, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = TimeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                HeartbeatResponse res = Parse<HeartbeatResponse>(req.downloadHandler.text);
                if (res != null && !string.IsNullOrEmpty(res.code)) onSuccess?.Invoke(res);
                else onFail?.Invoke("心跳响应无法解析");
            }
            else
            {
                onFail?.Invoke(req.error);
            }
        }
    }

    private static T Parse<T>(string json) where T : class
    {
        try { return JsonUtility.FromJson<T>(json); }
        catch { return null; }
    }
}

// ==================== 数据结构（字段名必须和后台 JSON 一致）====================

[Serializable]
public class RoomInfo
{
    public string code;
    public string ip;
    public int port;
    public int players;
    public int maxPlayers;
    public bool inGame;     // true = 正在对局中（此时进不去，列表里应过滤掉）
    public int ageSec;
}

[Serializable]
public class HeartbeatResponse
{
    public string code;
    public string ip;
    public int port;
    public int ttlSeconds;
}

[Serializable]
internal class HeartbeatRequest
{
    public string code;
    public string ip;
    public int port;
    public int players;
    public int maxPlayers;
    public bool inGame;
}

// JsonUtility 不支持顶层数组，所以列表接口包了一层 {"rooms":[...]}
[Serializable]
internal class RoomListWrapper
{
    public RoomInfo[] rooms;
}
