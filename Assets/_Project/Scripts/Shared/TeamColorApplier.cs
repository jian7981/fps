using UnityEngine;

// 按队伍给角色上色：0 = A队(蓝)，1 = B队(红)
// 【阶段2 注意】每个客户端只给自己/服务器权威设置；本地玩家用 isLocalPlayer 判断
[RequireComponent(typeof(Health))]
public class TeamColorApplier : MonoBehaviour
{
    [SerializeField] private Color teamAColor = new Color(0.25f, 0.55f, 1f);
    [SerializeField] private Color teamBColor = new Color(1f, 0.32f, 0.28f);

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor"); // URP/Lit 的颜色属性名

    private void Start()
    {
        Health health = GetComponent<Health>();
        Color color = (health.TeamId == 0) ? teamAColor : teamBColor;

        // MaterialPropertyBlock：只改这一个物体的颜色，不会污染共享材质（性能友好）
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            r.GetPropertyBlock(block);          // 先取出原有设置，避免覆盖掉别的属性
            block.SetColor(BaseColorID, color);
            r.SetPropertyBlock(block);
        }
    }
}