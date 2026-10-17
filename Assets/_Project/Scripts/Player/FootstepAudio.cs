using UnityEngine;
using Mirror;   // NetworkBehaviour / NetworkServer / NetworkClient（判断"这是不是本机玩家自己"用）

// ============ 脚步声（3D，听声辨位）【2.5-音效C】============
// 每个角色（本机/远程玩家/Bot）根上挂一份：
//   - 按"实际走过的水平距离"累积触发脚步（跑得快 → 步频自然变快；不读动画状态，任何角色通用）
//   - 3D 音源放在脚底 → 其他玩家能听到脚步声的方位与远近
//   - 音量随移动速度在"走路轻 / 跑步响"之间过渡；每步轻微随机音高，避免机械重复
// 注意：远程玩家的位置由网络插值驱动，位移同样有效；不依赖 CharacterController.isGrounded
//      （远程副本不会跑 Move，isGrounded 不可信），只用水平位移判定"在移动"。
// 防瞬移：单帧位移过大（复活/换出生点）→ 当作传送，不触发也不累积。
public class FootstepAudio : MonoBehaviour
{
    [Header("脚步音（拖 2~4 个不同脚步随机轮播；只放 1 个也能用；留空静默）")]
    [SerializeField] private AudioClip[] stepSounds;

    [Header("步频 / 音量")]
    [Tooltip("每走多少米一步（跑步步幅大，整体调它最顺手）")]
    [SerializeField] private float stepDistance = 2.4f;
    [Tooltip("最快步频限制（秒/步）：防止抖动连响")]
    [SerializeField] private float minStepInterval = 0.2f;
    [Tooltip("低于这个速度（米/秒）不算在移动")]
    [SerializeField] private float minMoveSpeed = 0.8f;
    [Tooltip("速度 → 音量 的过渡区间：这个速度以下按走路音量，以上按跑步音量")]
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float runSpeed = 5f;
    [Range(0f, 1f)] [SerializeField] private float walkVolume = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float runVolume = 1f;

    [Header("自己 / 他人 音量（主次：自己的脚步通常最清晰）")]
    [Tooltip("自己脚步的音量倍率")]
    [Range(0f, 1f)] [SerializeField] private float ownVolumeScale = 1f;
    [Tooltip("其他人（远程玩家 / Bot）脚步的音量倍率：想拉开主次就把它调低")]
    [Range(0f, 1f)] [SerializeField] private float otherVolumeScale = 0.6f;

    [Header("3D 音源")]
    [SerializeField] private float minDistance = 2f;    // 2 米内最大音量
    [SerializeField] private float maxDistance = 40f;   // 40 米外听不见
    [Tooltip("每步随机音高范围（±）")]
    [SerializeField] private float pitchRandom = 0.08f;

    [Header("防瞬移")]
    [Tooltip("单帧水平位移超过它（米）当作传送（复活/出生点切换），不触发脚步")]
    [SerializeField] private float teleportDistance = 2f;

    private AudioSource source;
    private NetworkBehaviour netBehaviour;   // 判断"这份角色是不是本机玩家"
    private Vector3 lastPos;
    private float distanceAccum;      // 已累积的"待消耗"距离
    private float lastStepTime = -10f;
    private int lastClipIndex = -1;

    private void Awake()
    {
        GameObject go = new GameObject("FootstepAudio(3D音源)");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.1f, 0f);   // 脚底
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;   // 纯 3D
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;

        netBehaviour = GetComponent<NetworkBehaviour>();
        lastPos = transform.position;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || source == null) return;

        Vector3 delta = transform.position - lastPos;
        lastPos = transform.position;
        delta.y = 0f;

        // 瞬移保护：复活/换出生点这种大跳直接跳过（不累积、不出声）
        if (delta.magnitude > teleportDistance)
        {
            distanceAccum = 0f;
            return;
        }

        float speed = delta.magnitude / dt;
        if (speed < minMoveSpeed) return;   // 没在动（含原地跳/下落：水平位移 ≈ 0）

        distanceAccum += delta.magnitude;
        while (distanceAccum >= stepDistance)
        {
            distanceAccum -= stepDistance;
            PlayStep(speed);
        }
    }

    private void PlayStep(float speed)
    {
        if (stepSounds == null || stepSounds.Length == 0) return;
        if (Time.time - lastStepTime < minStepInterval) return;   // 步频上限
        lastStepTime = Time.time;

        int index = Random.Range(0, stepSounds.Length);
        if (stepSounds.Length > 1 && index == lastClipIndex)      // 避免连续两步同一素材
            index = (index + 1) % stepSounds.Length;
        lastClipIndex = index;

        AudioClip clip = stepSounds[index];
        if (clip == null) return;

        float t = Mathf.InverseLerp(walkSpeed, runSpeed, speed);   // 0=走路 1=跑步
        float vol = Mathf.Lerp(walkVolume, runVolume, t);

        // 自己 / 他人 音量主次：自己的脚步保持清晰，其他人的按倍率压低
        vol *= IsOwnCharacter() ? ownVolumeScale : otherVolumeScale;

        source.pitch = 1f + Random.Range(-pitchRandom, pitchRandom);
        source.PlayOneShot(clip, vol);   // 音量按次传，避免影响"正在播放的上一步"
    }

    // 这份角色是不是"本机玩家自己"
    // 联机：只有本机拥有的玩家 isLocalPlayer 为 true（Host 自己的玩家也算）；
    // 纯单机：没有联机会话时，非 Bot 的角色就是自己（Bot 身上带 BotController）
    private bool IsOwnCharacter()
    {
        if (netBehaviour != null && netBehaviour.isLocalPlayer) return true;
        if (!NetworkServer.active && !NetworkClient.active)
            return GetComponent<BotController>() == null;
        return false;
    }
}