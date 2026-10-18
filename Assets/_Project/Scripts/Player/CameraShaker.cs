using System.Collections.Generic;
using UnityEngine;

// 【阶段4-B】相机抖动（多抖源叠加）
// 用法：CameraShaker.Shake(强度, 时长, 粗糙度) —— 谁想抖谁调，多个来源互不干扰
//
// ★ 关键设计：抖动**不写相机自己**，而是在 Awake 时于"相机"与"它的父级"之间**运行时插入一个空节点**，
//   所有偏移只写这个节点。为什么必须这样：
//   1) PlayerLook 每帧对 cam.localRotation 做**绝对赋值** —— 写到相机上的偏移下一帧就被顶掉；
//      若靠"这帧加上、下帧撤销"绕过，一旦 PlayerLook 因死亡 / 设置面板 / 远程玩家而提前 return，
//      偏移就会被反复叠加、越滚越大（视角直接飘走）
//   2) DeathCamera 死亡时也在写同一个相机的机位（协程 lerp），再插一脚就是互相打架
//   写入独立节点后：既天然是"叠加层"（不覆盖任何人的值），也不依赖脚本执行顺序；
//   插入的节点是单位变换 → 相机的 localPosition / localRotation 数值分毫不变、世界位姿不变
[DisallowMultipleComponent]
public class CameraShaker : MonoBehaviour
{
    [Header("幅度（强度 = 1 时的最大偏移）")]
    [SerializeField] private float positionAmplitude = 0.05f;   // 位置：米
    [SerializeField] private float rotationAmplitude = 1.6f;    // 旋转：度

    [Header("上限")]
    [Tooltip("同时最多几个抖源（连续中弹会叠；到上限就丢最早的那个）")]
    [SerializeField] private int maxProfiles = 12;

    [Header("调试")]
    [SerializeField] private bool logShakes = false;

    // 一次抖动 = 一个 profile：自己带时间轴与包络，各自独立衰减
    private class ShakeProfile
    {
        public float strength;    // 0~1（1 = 用满幅度）
        public float duration;    // 总时长（秒）
        public float roughness;   // 噪声频率：越大越"细碎"
        public float fadeIn;      // 淡入时长（秒）
        public float fadeOut;     // 淡出时长（秒）
        public float time;        // 已存活时长（秒）
        public Vector3 seed;      // 每个抖源一份随机相位 → 多个抖源不会同步得很假
    }

    private readonly List<ShakeProfile> profiles = new List<ShakeProfile>();
    private Transform offsetNode;   // 运行时插入的空节点：所有抖动只写它

    private void Awake()
    {
        CreateOffsetNode();
    }

    // 在"相机"和"它的父级"之间插一个空节点当叠加层（单位变换 → 谁的数值都不变）
    private void CreateOffsetNode()
    {
        Transform cam = transform;
        if (cam.parent == null)
        {
            Debug.LogWarning("[镜头抖动] 相机没有父物体 —— 不插入叠加层，抖动不生效（请确认相机在玩家层级下）", this);
            return;
        }

        GameObject node = new GameObject("CameraShakeOffset(运行时插入)");
        Transform nodeT = node.transform;
        nodeT.SetParent(cam.parent, false);
        nodeT.localPosition = Vector3.zero;
        nodeT.localRotation = Quaternion.identity;
        nodeT.localScale = Vector3.one;

        cam.SetParent(nodeT, true);   // worldPositionStays = true → 相机的 local 值原样保留
        offsetNode = nodeT;
    }

    // 触发一次抖动：strength 0~1；duration 秒；roughness 噪声频率（越大越细碎）
    public void Shake(float strength, float duration = 0.25f, float roughness = 20f)
    {
        if (offsetNode == null || strength <= 0f || duration <= 0f) return;

        if (profiles.Count >= maxProfiles) profiles.RemoveAt(0);   // 满了丢最早的，避免无限叠加

        ShakeProfile p = new ShakeProfile();
        p.strength = Mathf.Clamp01(strength);
        p.duration = duration;
        p.roughness = roughness;
        p.fadeIn = duration * 0.15f;    // 起手极快：挨打要立刻有反馈
        p.fadeOut = duration * 0.65f;   // 收尾占比更大：回落干净、不留拖尾
        p.seed = new Vector3(Random.value * 100f, Random.value * 100f, Random.value * 100f);
        profiles.Add(p);

        if (logShakes)
            Debug.Log($"[镜头抖动] 强度 {p.strength:0.00} / 时长 {duration:0.00}s / 同时进行 {profiles.Count} 个");
    }

    // 立刻停掉所有抖动并把叠加层归零
    public void StopAll()
    {
        profiles.Clear();
        ClearOffset();
    }

    private void LateUpdate()
    {
        if (offsetNode == null) return;

        float dt = Time.deltaTime;
        Vector3 pos = Vector3.zero;
        Vector3 rot = Vector3.zero;

        for (int i = profiles.Count - 1; i >= 0; i--)
        {
            ShakeProfile p = profiles[i];
            p.time += dt;
            if (p.time >= p.duration) { profiles.RemoveAt(i); continue; }   // 这个抖源走完了

            // 每帧归零后"累加"：多抖源叠加的落点 —— 各自衰减、互不打断
            float amp = p.strength * Envelope(p);
            float t = p.time * p.roughness;
            Vector3 n = new Vector3(Noise(p.seed.x + t), Noise(p.seed.y + t), Noise(p.seed.z + t));
            pos += n * (positionAmplitude * amp);
            rot += n * (rotationAmplitude * amp);
        }

        offsetNode.localPosition = pos;
        offsetNode.localRotation = Quaternion.Euler(rot);
    }

    // 包络：淡入 + 淡出（都取 min，所以两端不会互相覆盖）
    private static float Envelope(ShakeProfile p)
    {
        float e = 1f;
        if (p.fadeIn > 0f) e = Mathf.Min(e, p.time / p.fadeIn);
        float remain = p.duration - p.time;
        if (p.fadeOut > 0f) e = Mathf.Min(e, remain / p.fadeOut);
        return Mathf.Clamp01(e);
    }

    // 平滑噪声（-1~1）：Perlin 天生连续 → 看起来是"抖"，而不是逐帧跳随机
    private static float Noise(float t) => Mathf.PerlinNoise(t, 0.37f) * 2f - 1f;

    private void OnDisable()
    {
        StopAll();   // 视图关掉（远程玩家的相机被禁用）时把叠加层归零，避免残留偏移
    }

    private void ClearOffset()
    {
        if (offsetNode == null) return;
        offsetNode.localPosition = Vector3.zero;
        offsetNode.localRotation = Quaternion.identity;
    }
}
