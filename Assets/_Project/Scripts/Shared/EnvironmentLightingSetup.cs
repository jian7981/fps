using UnityEngine;
using UnityEngine.Rendering;

// 统一 map1 的环境光照，保证任何加载方式下色调一致
public class EnvironmentLightingSetup : MonoBehaviour
{
    [SerializeField] private Material skybox;          // 留空则不覆盖天空盒
    [SerializeField] private float ambientIntensity = 1f; // 阴影偏暗就调大（1.2 ~ 2）

    private void Awake()
    {
        if (skybox != null) RenderSettings.skybox = skybox;

        // 与"编辑器里直接运行"一致：用天空盒计算环境光，阴影处的亮度全靠它
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = ambientIntensity;

        // 场景里没有烘焙数据，运行时不会自动算环境光探针，这里手动算一次
        DynamicGI.UpdateEnvironment();
    }
}