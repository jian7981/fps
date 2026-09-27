using UnityEngine;
using UnityEngine.UI;
using Shared;

public class LobbyUI : MonoBehaviour
{
    [Header("引用")]
    public LobbyManager lobbyManager;

    [Header("UI按钮")]
    public Button mapGameButton;
    public Button startSingleBtn;

    private void Start()
    {
        if(mapGameButton != null && lobbyManager !=null)
        {
            mapGameButton.onClick.AddListener(() => lobbyManager.SelectMap(Constants.GameSceneName));
        }
        else
        {
            Debug.LogError("mapGameButton 或 lobbyManager 没有赋值");

        }
        if(startSingleBtn !=null && lobbyManager != null)
        {
            startSingleBtn.onClick.AddListener(lobbyManager.StartSinglePlayerGame);
        }
        else
        {
            Debug.LogError("startSingleBtn或lobbyManager 没有赋值");
        }
    }
}
