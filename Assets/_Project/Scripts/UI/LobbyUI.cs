using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyManager lobbyManager;

    [Header("UI按钮")]
    public Button mapGameButton;
    public Button startSingleBtn;

    private void Start()
    {
        if (mapGameButton != null)
        {
            mapGameButton.onClick.AddListener(() =>
            {
                if (lobbyManager != null)
                    lobbyManager.SelectMap("map1");
                else
                    Debug.LogError("LobbyManager 没有赋值！");
            });
        }
        else
        {
            Debug.LogError("mapGameButton 没有赋值！去Canvas的LobbyUI面板拖入按钮");
        }

        if (startSingleBtn != null && lobbyManager != null)
        {
            startSingleBtn.onClick.AddListener(lobbyManager.StartSinglePlayerGame);
        }
        else
        {
            Debug.LogError("startSingleBtn 或 lobbyManager 没有赋值！");
        }
    }
}
