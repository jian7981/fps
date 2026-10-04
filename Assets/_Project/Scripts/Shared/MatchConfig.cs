using System.Collections.Generic;   // 需要用到 List<>

namespace Shared
{
    // ======================= 单个"玩家格子"的数据 =======================
    // 组队界面里一个格子 = 一个 PlayerSlot，它只描述"这个位置上坐着谁"
    [System.Serializable]
    public class PlayerSlot
    {
        public int teamId;          // 0 = A队，1 = B队
        public bool isAI;           // true = 这个位置是 AI Bot；false = 空位
        public bool isLocalPlayer;  // true = 这个位置是"本机玩家"（整局只有 1 个）
    }

    // ======================= 一整局对战的配置 =======================
    // 由组队界面在点击【开始】时打包好，跟着场景切换带进 map1
    public class MatchConfig
    {
        public string mapName;                                  // 本局地图的场景名
        public bool autoFillAI;                                 // 是否开启了"自动匹配AI"
        public List<PlayerSlot> slots = new List<PlayerSlot>(); // 10 个格子

        // 数一数某一队里有几个 AI（给 GameManager 生成 Bot 用）
        public int CountAI(int teamId)
        {
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].teamId == teamId && slots[i].isAI) count++;
            }
            return count;
        }

       // 数一数某一队里有几个"真人"（目前只有本机玩家这一格） 
       // 【修正】旧写法用的 !isAI 会把"空位"也算成人类，这里改成只认 isLocalPlayer
        public int CountHuman ( int teamId ) 
        { 
            int count = 0 ; 
            for ( int i = 0 ; i < slots.Count; i++)
            {
                if (slots[i].teamId == teamId && slots[i].isLocalPlayer) 
                    count++;
            } 
            return count;
        }

        // 【新增】数一数某一队里有几个"空位"（没放人的格子）
        public int CountEmpty ( int teamId ) 
        { int count = 0 ; 
            for ( int i = 0 ; i < slots.Count; i++)
            { 
                // 不是 AI、也不是本机玩家 → 就是空位
                if (slots[i].teamId == teamId && !slots[i].isAI && !slots[i].isLocalPlayer) count++;
            }
            return count;
        }

// 默认配置：你在 A队第 1 格，其余 9 格全是 AI（自动补满）
public static MatchConfig CreateDefault(string mapName)
        {
            MatchConfig config = new MatchConfig();
            config.mapName = mapName;
            config.autoFillAI = true;   // 默认配置视为"开了自动匹配"
            for (int i = 0; i < Constants.TotalSlotCount; i++)
            {
                PlayerSlot slot = new PlayerSlot();
                slot.teamId = (i < Constants.TeamSize) ? 0 : 1;   // 前5格A队，后5格B队
                slot.isLocalPlayer = (i == 0);                    // 第1格永远是你
                slot.isAI = (i != 0);                             // 除了你，其余都是AI
                config.slots.Add(slot);
            }
            return config;
        }
    }

    // ======================= 大厅 → 对局 的数据中转站 =======================
    // 用途：切场景时 Unity 会把大厅里的对象全部销毁，数据没地方放，
    //       所以用一个静态变量当"过场景的一次性行李"。
    // 注意：铁律4 说的"不用静态存对局状态"指的是击杀数、比分这种实时战斗状态；
    //       这里只是开局瞬间传递一次的配置，不参与战斗计算。
    //       阶段2联机时，这份配置会改由服务器下发给房间里所有人。
    public static class MatchConfigTransfer
    {
        public static MatchConfig Pending;   // 大厅装进来，map1 的 GameManager 取走
    }
}