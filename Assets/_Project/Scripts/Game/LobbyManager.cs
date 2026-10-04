using UnityEngine;
using UnityEngine.SceneManagement;
using Shared;

// ============ 大厅逻辑管理器 ============
// 挂载：Lobby 场景里的 LobbyManager 物体上
public class LobbyManager : MonoBehaviour
{
    // 当前选中的地图场景名（初值就是 map1；地图选择界面会改它）
    // 用"属性初始化"而不是 Start 里赋值，保证任何时候读到的都不是 null
    public string selectedMapName { get; private set; } = Constants.GameSceneName1;

    // 【组队界面点"开始"走这里】带上配置开始对局
    public void StartMatch(MatchConfig config)
    {
        // 1. 把配置放进"中转站"，map1 场景里的 GameManager 会来取
        MatchConfigTransfer.Pending = config;

        // 2. 打印日志，方便验证大厅的配置有没有带过来
        Debug.Log("[大厅] 开始对局！地图：" + config.mapName
            + "｜A队 AI " + config.CountAI(0) + " 个｜B队 AI " + config.CountAI(1) + " 个");

        // 3. 加载地图场景（大厅会被销毁，中转站里的配置会活下来）
        SceneManager.LoadScene(selectedMapName);
    }

    // 旧入口：没经过组队界面时的保底开始方式（默认给你配满 9 个 AI）
    public void StartSinglePlayerGame()
    {
        StartMatch(MatchConfig.CreateDefault(selectedMapName));
    }

    // 【预留接口，阶段3联机用】快速匹配
    public void StartMatchmaking()
    {
        Debug.Log("[匹配] 快速匹配将在阶段3（公网部署）开放，当前是单机 Demo");
    }

    // 【预留接口，阶段2联机用】后面创建房间调用这个函数
    public void CreateRoom()
    {
        Debug.Log("创建房间，地图：" + selectedMapName);
    }

    // 记录选中的地图（地图选择界面调用）
    public void SelectMap(string mapName)
    {
        selectedMapName = mapName;
        Debug.Log("已选择地图：" + selectedMapName);
    }
}