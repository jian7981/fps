using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 【阶段4-A】把借鉴来的 FPS Framework 特效材质（原本是内置管线着色器）批量转成 URP
// 背景：这些材质用的都是 Unity 内置着色器（Standard / Particles/*），在 URP 项目里会显示为粉色
// 用法：菜单栏 → Tools → 转换 FPS Framework 材质到 URP（可重复执行，已经是 URP 的会自动跳过）
//
// 分流规则（按原材质的混合因子判断，不依赖具体着色器名字）：
//   · 不透明（SrcBlend = One, DstBlend = Zero）→ Universal Render Pipeline/Lit
//   · 透明 / 粒子（其余）                      → Universal Render Pipeline/Particles/Unlit
// 保留：主贴图（含平铺与偏移）、颜色、混合因子、ZWrite、Alpha 裁剪阈值、渲染队列语义
// 健壮性：写属性前一律 HasProperty 判空 —— URP 各版本的属性名有差异，不判空会直接抛异常中断
public static class FPSFrameworkMaterialToURP
{
    // 只处理这个目录下的材质（避免误伤工程里其他材质）
    private const string MaterialFolder = "Assets/_Project/ThirdParty/FPSFramework/Materials";

    [MenuItem("Tools/转换 FPS Framework 材质到 URP")]
    private static void Convert()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder });
        int converted = 0, skipped = 0, failed = 0;

        try
        {
            AssetDatabase.StartAssetEditing();   // 批量改动期间暂停导入，快很多

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;

                // 已经是 URP 着色器 → 跳过（保证本操作可重复执行）
                if (mat.shader.name.StartsWith("Universal Render Pipeline")) { skipped++; continue; }

                // ---- 换 shader 前先把要保留的信息读出来（换完属性名就不一样了）----
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Vector2 texScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
                Vector2 texOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0f;

                // 混合因子能直接搬到 URP（两边同名同语义）→ 用它判断"不透明 / 透明"
                float srcBlend = mat.HasProperty("_SrcBlend") ? mat.GetFloat("_SrcBlend") : (float)BlendMode.SrcAlpha;
                float dstBlend = mat.HasProperty("_DstBlend") ? mat.GetFloat("_DstBlend") : (float)BlendMode.OneMinusSrcAlpha;
                bool opaque = Mathf.Approximately(srcBlend, (float)BlendMode.One)
                           && Mathf.Approximately(dstBlend, (float)BlendMode.Zero);

                string target = opaque ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Particles/Unlit";
                Shader shader = Shader.Find(target);
                if (shader == null)
                {
                    Debug.LogWarning($"[FPSFramework→URP] 找不到着色器 {target}，跳过 {mat.name}");
                    failed++;
                    continue;
                }

                mat.shader = shader;

                // ---- 贴图与颜色搬家（_MainTex → _BaseMap，_Color → _BaseColor）----
                if (mainTex != null && mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", mainTex);
                    mat.SetTextureScale("_BaseMap", texScale);
                    mat.SetTextureOffset("_BaseMap", texOffset);
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

                if (opaque)
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);   // 0 = Opaque
                    if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
                    mat.renderQueue = (int)RenderQueue.Geometry;

                    // 原来是"带镂空的不透明"（比如叶片贴图）再把裁剪还原上
                    if (cutoff > 0f && mat.HasProperty("_Cutoff"))
                    {
                        mat.SetFloat("_Cutoff", cutoff);
                        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                        mat.EnableKeyword("_ALPHATEST_ON");
                    }
                }
                else
                {
                    if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // 1 = Transparent
                    if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", srcBlend);
                    if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", dstBlend);
                    if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

                    // URP 粒子的混合模式枚举：0 = Alpha，2 = Additive
                    if (mat.HasProperty("_Blend"))
                        mat.SetFloat("_Blend", Mathf.Approximately(dstBlend, (float)BlendMode.One) ? 2f : 0f);

                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }

                // 粒子着色器的"软粒子"会按深度把贴花淡掉 —— 我们用的是平贴在墙上的贴花，显式关掉
                mat.DisableKeyword("_SOFTPARTICLES_ON");

                EditorUtility.SetDirty(mat);
                converted++;
                Debug.Log($"[FPSFramework→URP] {mat.name} → {target}");
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[FPSFramework→URP] 完成：转换 {converted} 个，跳过 {skipped} 个（已是 URP），失败 {failed} 个");
    }
}
