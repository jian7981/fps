using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("玩家预制体")]
    public GameObject playerPrefab;

    [Header("===== 出生点设置 5v5 =====")]
    public Transform[] teamASpawnPoints;
    public Transform[] teamBSpawnPoints;

    private void Start()
    {
        // GameManager只在map1场景存在，直接生成玩家
        SpawnPlayerSingle();
    }

    void SpawnPlayerSingle()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("playerPrefab 没有赋值！");
            return;
        }
        if (teamASpawnPoints == null || teamASpawnPoints.Length == 0)
        {
            Debug.LogError("A队出生点数组为空！");
            return;
        }
        Transform spawnPos = teamASpawnPoints[0];
        Instantiate(playerPrefab, spawnPos.position, spawnPos.rotation);
        Debug.Log("单机：玩家生成成功，出生位置：" + spawnPos.name);
    }

    public Transform GetRandomSpawnPoint(int teamId)
    {
        Transform[] targetSpawns = (teamId == 0) ? teamASpawnPoints : teamBSpawnPoints;
        int randomIndex = Random.Range(0, targetSpawns.Length);
        return targetSpawns[randomIndex];
    }
}
