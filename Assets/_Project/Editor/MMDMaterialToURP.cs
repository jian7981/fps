using UnityEditor;
using UnityEngine;

// 把 MMD4Mecanim（内置管线）材质批量转换成 URP/Lit，并保留主贴图/颜色/双面/透明
// 用法：Unity 菜单栏 → Tools → 转换 MMD 材质到 URP
public static class MMDMaterialToURP
{
    // 要处理的材质目录（就是庄方宜的材质文件夹）
    private const string MaterialFolder = "Assets/ThirdParty/1/Materials";

    [MenuItem("Tools/转换 MMD 材质到 URP")]
    private static void Convert()
    {
        // 找出该目录下所有材质资产
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            if (!mat.shader.name.StartsWith("MMD4Mecanim")) continue;   // 只动 MMD 材质，别误伤其他

            // ---- 换 shader 前先把要保留的信息读出来（换完属性名就不一样了）----
            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            Vector2 mainScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 mainOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
            Texture bumpTex = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
            float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0f;

            bool bothFaces = mat.shader.name.Contains("BothFaces");                       // 原 shader 是否双面
            bool transparent = mat.shader.name.Contains("Transparent") || mat.renderQueue >= 3000;

            // ---- 换成 URP/Lit ----
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");

            if (mainTex != null)
            {
                mat.SetTexture("_BaseMap", mainTex);          // 主贴图搬家：_MainTex → _BaseMap
                mat.SetTextureScale("_BaseMap", mainScale);
                mat.SetTextureOffset("_BaseMap", mainOffset);
            }
            mat.SetColor("_BaseColor", color);                // 颜色搬家：_Color → _BaseColor
            if (bumpTex != null) mat.SetTexture("_BumpMap", bumpTex);

            mat.SetFloat("_Metallic", 0f);                    // MMD 模型不用金属度
            mat.SetFloat("_Smoothness", 0.1f);                // 压低高光，接近卡通质感

            if (bothFaces) mat.SetFloat("_Cull", 0f);         // 0 = 关闭背面剔除（头发/裙子需要）

            if (transparent)
            {
                // 半透明材质（比如头发、纱）：开启透明混合
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else if (cutoff > 0f)
            {
                // 镂空材质（头发边缘）：开启 Alpha Clipping
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", cutoff);
                mat.EnableKeyword("_ALPHATEST_ON");
            }

            EditorUtility.SetDirty(mat);
            count++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[MMD→URP] 转换完成，共处理 {count} 个材质");
    }
}