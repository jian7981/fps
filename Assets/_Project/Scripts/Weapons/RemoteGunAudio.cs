using UnityEngine;
using Mirror;   // NetworkBehaviour

// ============ 远端枪声【2.5-音效B】============
// 解决：双开时别人开火你这边没有声音（对方的 Camera 被禁用，Camera 上的 WeaponFeedback 不工作）。
// 做法：在自己角色根物体上挂一个"3D 音源"，订阅 WeaponController.OnFired：
//   - 玩家：服务器校验通过后经 RpcFireFeedback 广播 → 各端在"远程玩家"的控制器副本上触发 OnFired → 这里播 3D 枪声
//   - 自己那一端跳过（本地 2D 枪声已由 Camera 上的 WeaponFeedback 播，避免叠加）
//   - Bot：没有 WeaponController，由 BotController.FireAt 直接调用 PlayShot(0)
// 3D 衰减 → 能听声辨位（走近变响、走远变小、左右有方位感）
// 挂载：角色预制体根物体（Player.prefab / Bot.prefab）；槽位顺序和武器列表一致（0=步枪，1=狙击）
public class RemoteGunAudio : MonoBehaviour
{
    [Header("枪声（顺序必须和 WeaponController 的 Weapons 数组一致；留空静默）")]
    [SerializeField] private AudioClip[] shotSounds;   // 0=步枪，1=狙击

    [Header("3D 音源")]
    [Tooltip("音源相对角色根的位置（角色根在脚下，1.4 ≈ 枪口高度）")]
    [SerializeField] private Vector3 sourceLocalOffset = new Vector3(0f, 1.4f, 0.2f);
    [SerializeField] private float minDistance = 3f;    // 3 米内最大音量
    [SerializeField] private float maxDistance = 80f;   // 80 米外听不见
    [Range(0f, 1f)] [SerializeField] private float volume = 1f;
    [Tooltip("每发随机音高范围（±），避免连发机械感；0 = 不变调")]
    [SerializeField] private float pitchRandom = 0.06f;

    private WeaponController weapons;         // 玩家有、Bot 没有（Bot 走外部直接调用）
    private NetworkBehaviour netBehaviour;    // 用来判断"这份对象是不是本机玩家"
    private AudioSource source;
    private int currentIndex;

    private void Awake()
    {
        weapons = GetComponent<WeaponController>();
        netBehaviour = GetComponent<NetworkBehaviour>();

        // 3D 音源：建在"枪口高度"的子物体上（方位感来自源点相对耳朵的位置关系）
        GameObject go = new GameObject("RemoteGunAudio(3D音源)");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = sourceLocalOffset;
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;                              // 1 = 纯 3D
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.volume = volume;
    }

    private void OnEnable()
    {
        if (weapons == null) return;   // Bot：没有 WeaponController，只由 BotController 直接调用
        weapons.OnFired += HandleFired;
        weapons.OnWeaponChanged += HandleWeaponChanged;
    }

    private void OnDisable()
    {
        if (weapons == null) return;
        weapons.OnFired -= HandleFired;
        weapons.OnWeaponChanged -= HandleWeaponChanged;
    }

    private void HandleWeaponChanged(int index, WeaponData weapon) { currentIndex = index; }

    private void HandleFired()
    {
        // 本机玩家自己的开火跳过（自己那份由 Camera 上的 WeaponFeedback 播 2D 声）
        if (netBehaviour != null && NetUtil.IsLocalControl(netBehaviour)) return;
        PlayShot(currentIndex);
    }

    // 对外：直接播一声（BotController.FireAt 用它；index = 枪声槽下标，Bot 固定 0）
    public void PlayShot(int index)
    {
        if (source == null || shotSounds == null || index < 0 || index >= shotSounds.Length) return;
        AudioClip clip = shotSounds[index];
        if (clip == null) return;
        source.pitch = 1f + Random.Range(-pitchRandom, pitchRandom);   // 每发轻微变调，连发更自然
        source.PlayOneShot(clip);
    }
}