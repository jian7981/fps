using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("玩家预制体")]
    public GameObject playerPrefab;

    [Header("出生点设置 5V5")]
    public Transform[] teamASpawnPoints; // 队伍A的出生点
    public Transform[] teamBSpawnPoints; // 队伍B的出生点

    private void Start()
    {
        //Gamemanager 只在map1存在，直接生成玩家
        SpawnPlayerSingle();
    }

    void SpawnPlayerSingle()
    {
        if(playerPrefab == null)
        {
            Debug.LogError("玩家预制体未设置！");
            return;
        }
        if(teamASpawnPoints==null || teamASpawnPoints.Length == 0)
        {
            Debug.LogError("队伍A出生点未设置！");
            return;
        }
        Transform spawnPos = teamASpawnPoints[0];
        Instantiate(playerPrefab, spawnPos.position, spawnPos.rotation);
        Debug.Log("玩家已生成在队伍A出生点：" + spawnPos.name);
    }
    public Transform GetRandomSpawnPoint(int teamId)
    {
        Transform[] targetSpawns = (teamId == 0) ? teamASpawnPoints : teamBSpawnPoints;
        if(targetSpawns==null||targetSpawns.Length == 0)
        {
            Debug.LogWarning("队伍" + teamId + "没有出生点，回退到队伍A");
            targetSpawns = teamASpawnPoints;
        }
        int randomIndex = Random.Range(0, targetSpawns.Length);
        return targetSpawns[randomIndex];
    }
}