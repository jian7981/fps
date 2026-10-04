namespace Shared
{
    public static class Constants
    {
        // 场景名称
        public const string LobbySceneName = "Lobby";
        public const string GameSceneName1 = "map1";

        // 【新增】组队配置
        public const int TeamSize = 5 ; // 每队人数（5v5）
        public const int TotalSlotCount = 10 ; // 组队格子总数（= TeamSize * 2）
    }
}
