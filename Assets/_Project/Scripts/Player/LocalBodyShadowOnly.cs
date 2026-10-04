using UnityEngine;
using UnityEngine.Rendering;   // ShadowCastingMode 在这个命名空间里

// 第一人称：隐藏自己的身体网格，但保留影子
// 单机阶段：直接生效
// 阶段2 联机：在 OnStartLocalPlayer 里调用（只对本地玩家生效，别人的身体照常显示）
public class LocalBodyShadowOnly : MonoBehaviour
{
    [SerializeField] private Transform bodyRoot;   // 拖模型根物体（不填就用自己）

    private void Start()
    {
        Transform root = (bodyRoot != null) ? bodyRoot : transform;

        // includeInactive = true：连隐藏的子物体也一起设置，避免漏掉
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            // Shadows Only = 不渲染网格，但仍然投射阴影
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }
}