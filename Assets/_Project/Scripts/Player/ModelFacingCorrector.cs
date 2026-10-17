using UnityEngine;
using Mirror;   // NetworkBehaviour（动态检测日志里区分"本地玩家/远程玩家"）

// 【阶段2.5】模型朝向校正：把"下载/导入模型自带的初始朝向偏移"自动转正
// ----------------------------------------------------------------------
// 背景：不同来源的模型，制作时"正面"指向不一致：
//   现有 X Bot（Mixamo）：正面 = 角色正前方（+Z），实测偏 0.0°（不需要校正，挂上等于空转，日志会说明）
//   枪械模型（AK74/M107）：自带朝向相反，挂载时被转了 180°
//   新下载的人物模型（MMD 转换等）：可能偏 45°/90°（如俯视图里朝西北）→ 直接放进预制体会斜着走
// 原理：玩法逻辑只转角色【根物体】（PlayerLook 转 yaw、BotController.FaceTowards 转向），
//       模型只是根物体的子物体 → 给【模型这一层】补一个水平旋转，模型就转正了。
//       只动模型这一层，所以：根物体的移动/碰撞/网络同步/相机 不受影响；
//       枪挂在手骨骼下 → 跟着一起转对；动画是模型内部的事，不受影响。
// 修补方式自动二选一：
//   静态节点：找到"包住骨架的中间层"（如 Armature）→ 转一次就生效
//   动画骨骼：骨架直接挂在角色根下、没有中间层（如 X Bot 的 mixamorig:Hips）→ 必须每帧在
//             LateUpdate 里把角度叠加到骨骼的"当前动画姿态"上（只写一次会被动画的下一帧覆盖=白写）
// 两种模式：
//   Auto   自动：用"左/右"成对骨骼（优先大腿，退化到肩/臂）的位置差算出模型当前正面朝哪，
//                自动补正。Humanoid 骨架直接用骨骼映射；通用骨架按常见命名找（含 MMD 的日文命名）
//   Manual 手动：直接填"需要把模型顺时针转多少度"（俯视图里调到正面朝北为止）——也会打印自动检测值作参考
// 动态检测（调试）：每 3 秒无条件打一条 [模型朝向·动态] 日志（腿线偏角 + 根朝向对观察相机的夹角 + 动画片段名）
//   —— 静态检测（Awake/T-pose）和动态检测（动画播放中）的"腿线偏角"用同一套算法，直接可比：
//      静态 0° + 动态 90° = 动画姿态本身把身体转了 90°；而"根朝向差"≈±90 = 根/同步层面没面向观察者
// 挂载：角色预制体根物体上（和 Health 同级），Model Root 留空即可（自动找骨架那层），也可手动拖
public class ModelFacingCorrector : MonoBehaviour
{
    public enum Mode { Auto, Manual }

    [Header("校正模式")]
    [Tooltip("Auto = 自动检测模型当前朝向并补正（推荐）；Manual = 手动填一个固定角度")]
    [SerializeField] private Mode mode = Mode.Auto;

    [Header("引用（留空 = 自动找：骨架中间层 / 动画根骨骼）")]
    [Tooltip("要转正的模型节点（留空自动找；手动拖的话，拖骨架根那一层：Armature 或 mixamorig:Hips）")]
    [SerializeField] private Transform modelRoot;

    [Header("手动校正角（度）")]
    [Tooltip("只在 Manual 模式（或自动检测失败）时使用。俯视图（屏幕上方 = 北 = 角色正前）：" +
             "模型正面需要顺时针转多少度能转正就填多少，逆时针填负数。如偏西 45°（朝西北）填 45")]
    [SerializeField] private float manualYaw = 0f;

    [Header("调试")]
    [Tooltip("在 Console 打印检测到的朝向和补正角度（搭模型阶段建议开着）")]
    [SerializeField] private bool logResult = true;
    [Tooltip("动态检测（调试）：每隔几秒无条件打一条 [模型朝向·动态] 日志——" +
             "给三个数：姿态内部偏角（腿线）、角色根朝向对观察相机的夹角、当前动画片段名。" +
             "定位\"面对面不朝我\"这类问题时开着；平时可关")]
    [SerializeField] private bool debugLiveFacing = true;
    [SerializeField] private float liveCheckInterval = 3f;    // 动态检测间隔（秒）

    private bool targetIsAnimated;      // 修正目标是不是"动画驱动的骨骼"（是 → LateUpdate 逐帧叠加）
    private float fixYaw;               // 最终补正角（度）
    private bool hasWritten;            // 逐帧叠加模式：是否写过一次（防动画被剔除/暂停时反复叠加、越转越多）
    private Quaternion lastWritten;     // 逐帧叠加模式：上一帧写出去的值

    private float nextLiveCheckTime;    // 下一次动态检测的时间点

    private void Awake()
    {
        // ---- 1. 解析要修正的节点 ----
        bool animated = false;
        if (modelRoot == null || modelRoot == transform)
        {
            Transform auto = ResolveTargetNode(out animated);
            if (modelRoot == transform && logResult)
                Debug.LogWarning("[模型朝向] " + name + "：Model Root 拖的是【角色根物体】本身（不能转：玩法/网络/相机都挂在它上面），已自动改用 " +
                                 (auto != null ? auto.name : "（没找到替代）") + " 代替", this);
            modelRoot = auto;
        }
        else
        {
            animated = IsAnimatedBone(modelRoot);   // 直接拖了骨骼 → 必须走逐帧叠加
        }

        if (modelRoot == null || modelRoot == transform)
        {
            Debug.LogWarning("[模型朝向] " + name + "：没找到可修正的模型节点 —— 请展开角色模型，把骨架根那一层（Armature / Hips）拖进 Model Root", this);
            return;
        }
        targetIsAnimated = animated;

        // ---- 2. 算补正角 ----
        float yaw = manualYaw;          // 默认用手动值（自动失败时就用它）
        string source = "手动";

        float detected;                 // 模型正面当前偏了多少度（俯视，向东为正）
        bool hasDetected = MeasureFacingYaw(out detected);

        if (mode == Mode.Auto)
        {
            if (hasDetected)
            {
                yaw = -detected;        // 补正量 = 反向抵消，模型正面对齐根物体正前方
                source = "自动（检测到偏 " + detected.ToString("F1") + "°）";
            }
            else if (logResult)
            {
                Debug.LogWarning("[模型朝向] " + name + "：自动检测失败（没找到可用的左右骨骼），改用手动角度 " + manualYaw.ToString("F1") + "°", this);
            }
        }
        else if (hasDetected)
        {
            // 手动模式也跑一次检测：日志里给出参考值，两个数差很多说明 manualYaw 可能填错
            source = "手动（自动检测参考：模型当前偏 " + detected.ToString("F1") + "°）";
        }
        fixYaw = yaw;

        // ---- 3. 应用 ----
        // 静态节点：转一次就生效（动画不会碰它）
        if (Mathf.Abs(fixYaw) > 0.01f && !targetIsAnimated)
            modelRoot.localRotation = Quaternion.Euler(0f, fixYaw, 0f) * modelRoot.localRotation;
        // 动画骨骼：交给 LateUpdate 每帧叠加（这里只记 fixYaw，见 LateUpdate）

        if (logResult)
            Debug.Log("[模型朝向] " + name + "：模型=" + modelRoot.name
                      + (targetIsAnimated ? "（动画骨骼·逐帧叠加）" : "（静态节点·转一次）")
                      + "，" + source + "，补正 " + fixYaw.ToString("F1") + "°", this);
    }

    private void LateUpdate()
    {
        // ---- 1. 动画骨骼模式：把补正角叠加到"当前帧的动画姿态"上 ----
        // （动画每帧会写骨骼的局部旋转，直接写一次会被下一帧覆盖；这里每帧在动画之后重写）
        if (targetIsAnimated && modelRoot != null && Mathf.Abs(fixYaw) > 0.01f)
        {
            Quaternion current = modelRoot.localRotation;

            // 这帧动画没写它（比如动画被剔除/暂停时不会有新值）→ 现在读到的是我们自己上次写的值，
            // 再叠加就会越转越多 → 跳过这一帧；等动画恢复写值后再继续叠加
            if (!(hasWritten && current == lastWritten))
            {
                lastWritten = Quaternion.Euler(0f, fixYaw, 0f) * current;
                hasWritten = true;
                modelRoot.localRotation = lastWritten;
            }
        }

        // ---- 2. 动态检测（调试）：每 3 秒无条件打一条测量日志（腿线偏角/根-相机夹角/动画片段名） ----
        if (debugLiveFacing) CheckLiveFacing();
    }

    // 动态检测（调试）：每 liveCheckInterval 秒无条件打一条日志
    //   腿线偏    = 当前动画姿态下，模型正面相对【角色根】偏了多少（和 Awake 静态检测同一套算法，可直接对比）
    //   本地玩家 → "我方视角对着对方差 X°"（≈0 = 我正看着对面那个人；验证"面对面"用的是它）
    //   远程玩家 → "根朝向对着本机相机差 X°"（≈0 = 对方模型正对着我；±90 = 我看到的是侧面）
    //   动画      = 当前播放的片段名（确认两个窗口播的是不是同一段）
    private void CheckLiveFacing()
    {
        if (Time.time < nextLiveCheckTime) return;
        nextLiveCheckTime = Time.time + liveCheckInterval;

        // 1) 姿态内部偏角（腿线）
        string leg = "量不到左右骨骼";
        float legYaw;
        if (MeasureFacingYaw(out legYaw)) leg = "腿线偏 " + legYaw.ToString("F1") + "°";

        // 2) 朝向关系：本地玩家量"我的视角有没有看着对方"；远程玩家量"对方模型有没有对着我"
        string rel = "";
        Transform obs = FindObserverCamera();
        NetworkBehaviour nb = GetComponent<NetworkBehaviour>();
        bool isLocal = NetUtil.IsLocalControl(nb);

        if (obs != null)
        {
            if (isLocal)
            {
                Transform other = FindOtherPlayer(nb);
                if (other != null)
                {
                    Vector3 toOther = other.position - obs.position;
                    toOther.y = 0f;
                    Vector3 viewFwd = obs.forward;
                    viewFwd.y = 0f;
                    if (toOther.sqrMagnitude > 0.01f && viewFwd.sqrMagnitude > 0.001f)
                        rel = "，我方视角对着对方差 " + Vector3.SignedAngle(viewFwd.normalized, toOther.normalized, Vector3.up).ToString("F1") + "°";
                }
            }
            else
            {
                Vector3 toObs = obs.position - transform.position;
                toObs.y = 0f;
                Vector3 rootFwd = transform.forward;
                rootFwd.y = 0f;
                if (toObs.sqrMagnitude > 0.01f && rootFwd.sqrMagnitude > 0.001f)
                    rel = "，根朝向对着本机相机差 " + Vector3.SignedAngle(toObs.normalized, rootFwd.normalized, Vector3.up).ToString("F1") + "°";
            }
        }

        // 3) 当前动画片段名
        string clip = "";
        Animator a = GetComponentInChildren<Animator>(true);
        if (a != null && a.layerCount > 0)
        {
            AnimatorClipInfo[] infos = a.GetCurrentAnimatorClipInfo(0);
            if (infos != null && infos.Length > 0 && infos[0].clip != null) clip = "，动画=" + infos[0].clip.name;
        }

        string who = isLocal ? "本地玩家" : "远程玩家（你看到的别人的模型）";
        Debug.Log("[模型朝向·动态] " + name + "（" + who + "）：" + leg + rel + clip, this);
    }

    // 找"另一个玩家"（联机时就是对面那个）：遍历所有 PlayerLook，排除自己
    private static Transform FindOtherPlayer(NetworkBehaviour mine)
    {
        PlayerLook[] all = FindObjectsOfType<PlayerLook>();
        for (int i = 0; i < all.Length; i++)
            if (all[i].netIdentity != null && all[i].netIdentity != mine.netIdentity) return all[i].transform;
        return null;
    }

    // 找"本机观察相机"：联机 = NetworkClient.localPlayer（本机玩家）身上的相机；单机 = 自己身上的相机
    private Transform FindObserverCamera()
    {
        if (NetworkClient.localPlayer != null)
        {
            Camera cam = NetworkClient.localPlayer.GetComponentInChildren<Camera>(true);
            if (cam != null) return cam.transform;
        }
        Camera own = GetComponentInChildren<Camera>(true);
        return own != null ? own.transform : null;
    }

    // 自动解析"要修正的节点"：
    // 1) 静态容器：直接子物体中，动画根骨骼（Hips 等）是它的后代 → 转一次就生效（Armature 类结构）
    // 2) 动画骨骼：没有容器时直接用动画根骨骼本身（骨架直接挂在角色根下的结构）→ 逐帧叠加
    // 3) 兜底：子物体里的 Animator / 含蒙皮网格的子物体（静态）
    private Transform ResolveTargetNode(out bool animated)
    {
        animated = false;

        Transform hips = FindHipsBone();
        if (hips != null)
        {
            foreach (Transform child in transform)
                // 注意排除 child == hips 自己：Unity 的 IsChildOf 对自己也返回 true（会把自己误判成"容器"）
                if (child != hips && hips.IsChildOf(child)) return child;    // 找到包住骨骼的静态层（Armature 之类）
            animated = true;                                 // 骨骼直接挂在角色根下 → 只能逐帧叠加
            return hips;
        }

        Animator anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.transform != transform) return anim.transform;
        foreach (Transform child in transform)
            if (child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return child;
        return null;
    }

    // 找"动画根"骨骼：优先 Humanoid 的 Hips 映射；通用骨架按命名（Hips/Pelvis/センター/上半身）
    private Transform FindHipsBone()
    {
        Animator anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.avatar != null && anim.avatar.isHuman)
        {
            Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) return hips;
        }
        return FindByName(new[] { "hips", "pelvis", "センター", "上半身" });
    }

    // 判断节点是不是"动画根骨骼"（是 → 必须逐帧叠加；只写一次会被动画覆盖）
    private bool IsAnimatedBone(Transform node)
    {
        Transform hips = FindHipsBone();
        if (hips != null && node == hips) return true;
        string n = node.name.ToLowerInvariant();
        return n.Contains("hips") || n.Contains("pelvis") || n.Contains("センター") || n.Contains("上半身");
    }

    // 在角色子树里按关键词（不区分大小写、优先级从高到低）找节点
    private Transform FindByName(string[] keys)
    {
        Transform[] all = transform.GetComponentsInChildren<Transform>(true);
        for (int k = 0; k < keys.Length; k++)
        {
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.ToLowerInvariant().Contains(keys[k])) return all[i];
        }
        return null;
    }

    // 量"模型正面相对角色正前"的偏角（度，俯视：正前 = 0，向东/右侧为正）
    // 【静态检测（Awake，T-pose）和动态检测（站立，动画中）共用同一套算法 → 两次数值可直接对比】
    // 做法：找"左/右"成对骨骼，用两点连线的水平分量 = 模型自己的"右方向"，
    //       右方向 × 上 = 正面方向（右手系），再换算成水平角度
    private bool MeasureFacingYaw(out float yawDeg)
    {
        yawDeg = 0f;

        Transform left, right;
        if (!FindSideBones(out left, out right)) return false;

        // 两点连线转进角色本地空间（排除根物体自身朝向的干扰），取水平分量
        Vector3 sideDir = transform.InverseTransformDirection(right.position - left.position);
        sideDir.y = 0f;
        if (sideDir.sqrMagnitude < 0.0001f) return false;   // 两点重合/竖直 → 判断不了

        Vector3 forward = Vector3.Cross(sideDir.normalized, Vector3.up);   // 右 × 上 = 正面
        yawDeg = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        return true;
    }

    // 找"左/右"成对骨骼：优先 Humanoid 映射（大腿 → 上臂），退化到命名查找
    // （优先大腿：不管模型是 T-pose、待机还是瞄准姿态，左右大腿/髋关节都在身体两侧，最不容易误判）
    private bool FindSideBones(out Transform left, out Transform right)
    {
        left = null;
        right = null;

        Animator anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.avatar != null && anim.avatar.isHuman)
        {
            left = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            right = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (left == null || right == null)
            {
                left = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                right = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            }
        }

        // 通用骨架：按常见命名找（英文 Left/Right、MMD 的日文 左/右 + 足/腕 等）
        if (left == null || right == null)
        {
            string[] leftKeys = { "左", "Left" };
            string[] rightKeys = { "右", "Right" };
            string[] partKeys = { "大腿", "UpLeg", "UpperLeg", "腿", "Leg", "足", "Foot", "腕", "Arm", "臂", "肩", "Shoulder" };
            left = FindBone(leftKeys, partKeys);
            right = FindBone(rightKeys, partKeys);
        }

        return left != null && right != null;
    }

    // 按"部位关键词（优先级从高到低）+ 侧向关键词"从角色子树里找一条骨骼
    private Transform FindBone(string[] sideKeys, string[] partKeys)
    {
        Transform[] all = transform.GetComponentsInChildren<Transform>(true);
        for (int p = 0; p < partKeys.Length; p++)
        {
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (!n.Contains(partKeys[p])) continue;      // 部位不对，看下一个
                for (int s = 0; s < sideKeys.Length; s++)
                    if (n.Contains(sideKeys[s])) return all[i];   // 侧别也对上了
            }
        }
        return null;
    }
}