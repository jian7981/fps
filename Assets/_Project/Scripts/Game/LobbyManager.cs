using UnityEngine;
using UnityEngine.SceneManagement;
using Shared;

public class LobbyManager : MonoBehaviour
{
    // 当前选中的地图名称，后面联机时可以同步给房间内所有人
    public string selectedMapName { get; private set; }

    private void Start()
    {
        // 默认选中Game地图
        selectedMapName = Constants.GameSceneName;
    }

    // 单机：点击开始游戏，加载选中地图
    public void StartSinglePlayerGame()
    {
        SceneManager.LoadScene(selectedMapName);
    }

    // 【预留接口，阶段2联机用】后面创建房间调用这个函数
    public void CreateRoom()
    {
        // 联机版本在这里写Mirror创建房间逻辑，单机这里暂时空着
        Debug.Log("创建房间，地图：" + selectedMapName);
    }

    // 选择地图，UI按钮绑定这个
    public void SelectMap(string mapName)
    {
        selectedMapName = mapName;
        Debug.Log("已选择地图：" + selectedMapName);
    }
}
