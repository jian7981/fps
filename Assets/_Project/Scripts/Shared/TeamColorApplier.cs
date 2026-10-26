﻿﻿﻿﻿﻿using UnityEngine;

// 按队伍给角色上色：0 = A队(蓝)，1 = B队(红)
// 【阶段5-B】默认关闭（tintEnabled = false）：全身染色会破坏 RifleGirl 美术，
//             敌我识别统一改用 TeamOutlineController 的红色/浅绿色轮廓。
// 【阶段2 注意】每个客户端只给自己/服务器权威设置；本地玩家用 isLocalPlayer 判断
//
// 【阶段4-D1 修复】两个毛病：
//   ① 原来把整棵子树里**所有** Renderer 都刷了队伍色 → 连武器（第一人称 view model 挂在相机下）
//      也一起被染色。现在加了"不染色的材质名单"，武器材质（框架武器 = Guns Main Mat）直接跳过。
//   ② 原来用 GetComponentsInChildren<Renderer>()（**不含未激活物体**）。第一人称的武器 view model 是
//      "切枪时整棵激活/失活"的 → TeamColorApplier.Start() 那一刻没激活的那把枪会被整棵跳过，
//      表现就是"切到狙击后手臂变回原色"。现在改成 includeInactive: true，两把枪的手臂都能拿到队伍色。
[RequireComponent(typeof(Health))]
public class TeamColorApplier : MonoBehaviour
{
    [Header("队伍染色开关")]
    [Tooltip("默认关闭：阶段 5-B 起敌我识别统一用 TeamOutlineController 轮廓，避免破坏角色美术。" +
             "需要临时恢复全身染色时再勾选")]
    [SerializeField] private bool tintEnabled = false;

    [SerializeField] private Color teamAColor = new Color(0.25f, 0.55f, 1f);
    [SerializeField] private Color teamBColor = new Color(1f, 0.32f, 0.28f);

    [Header("不染色的材质（名字 = 材质资产名，不是 (Instance)）")]
    [Tooltip("这些材质的渲染器不刷队伍色 —— 武器不该被队伍色污染。" +
             "以后换/加武器如果发现枪被染色，把那个材质名加进来即可")]
    [SerializeField] private string[] excludedMaterialNames = { "Guns Main Mat" };

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor"); // URP/Lit 的颜色属性名

    private void Start()
    {
        // 阶段 5-B：默认关闭全身染色，敌我识别改由 TeamOutlineController 负责
        if (!tintEnabled) return;

        Health health = GetComponent<Health>();
        Color color = (health.TeamId == 0) ? teamAColor : teamBColor;

        // MaterialPropertyBlock：只改这一个物体的颜色，不会污染共享材质（性能友好）
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        // includeInactive: true → 当前没激活的武器 view model 也要上色（否则切过去才发现是原色）
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (IsExcluded(r)) continue;        // 武器材质 → 跳过，保持枪本来的颜色

            r.GetPropertyBlock(block);          // 先取出原有设置，避免覆盖掉别的属性
            block.SetColor(BaseColorID, color);
            r.SetPropertyBlock(block);
        }
    }

    // 这个渲染器是不是用了"不染色"的材质（用 sharedMaterials，避免读到运行时实例化的 (Instance) 名字）
    private bool IsExcluded(Renderer r)
    {
        if (excludedMaterialNames == null || excludedMaterialNames.Length == 0) return false;

        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            for (int n = 0; n < excludedMaterialNames.Length; n++)
                if (mats[i].name == excludedMaterialNames[n]) return true;
        }
        return false;
    }
}
