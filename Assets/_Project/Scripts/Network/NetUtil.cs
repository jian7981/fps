using Mirror;

// 【阶段2.1】网络 / 单机判别工具
// 纯单机（编辑器里直接运行 map1、没有网络会话）时不存在"本地玩家"这个概念，
// 一律视为可本地操控 —— 保证阶段1的单机调试路径行为完全不变。
public static class NetUtil
{
    // 这个 NetworkBehaviour 是否应该响应"本机输入 / 本机物理"
    // 联机中：只有属于自己客户端的玩家对象才返回 true（别人的玩家对象由网络同步驱动）
    public static bool IsLocalControl(NetworkBehaviour nb)
    {
        if (nb == null) return false;

        // 没有网络会话 = 纯单机
        if (!NetworkServer.active && !NetworkClient.active) return true;

        // 联机中：只认自己的玩家对象
        return nb.netIdentity != null && nb.netIdentity.isLocalPlayer;
    }
    // 【阶段2.2】是否由"服务器"执行逻辑：
    // 纯单机（无网络会话）恒为 true —— 保证单机路径行为不变；
    // 联机中只有服务器为 true（Host 也算服务器），客户端为 false。
    public static bool IsServerSide()
    {
        if (!NetworkServer.active && !NetworkClient.active) return true;   // 纯单机
        return NetworkServer.active;
    }
}