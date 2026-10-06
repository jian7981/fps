using UnityEngine;   // MonoBehaviour / ParticleSystem / AudioSource / AudioClip / Light

// ============ 武器打击反馈（枪口火光 + 开火音效）============
// 订阅 WeaponController.OnFired：开火瞬间播当前枪的火光 + 枪声
// 订阅 OnWeaponChanged：切枪时清掉上一把枪残留的火光
// 挂载：挂 Camera 上（和 WeaponViewModel 同物体）
//
// 【修复记录】
// 1. 强制关闭粒子的"循环/自动播放"——WarFX 预制体默认 looping = 开、Play On Awake = 开，
//    不关的话：进游戏就自动循环播、开一枪后永远停不下来
// 2. 停火清尾：松开左键超过 stopAfterFireDelay 秒没再开火 → 立刻把火光清干净，不留拖尾
// 3. 枪口的灯整个关闭，不再参与开火表现——开火时点光照亮附近墙面观感差，直接不用；
//    灯所在物体整体失活，WarFX 自带的 WFX_LightFlicker 脚本（如果没删）也会随之彻底失效
public class WeaponFeedback : MonoBehaviour
{
    [Header("枪口火光（顺序必须和 WeaponController 的 Weapons 数组一致）")]
    [SerializeField] private Transform[] muzzleEffects;   // 每把枪的枪口火光实例（0=步枪，1=狙击）

    [Header("开火音效（顺序同上；还没配素材的格子留空即可）")]
    [SerializeField] private AudioClip[] fireSounds;

    [Header("音源（留空 = 自动获取，没有就自动加一个）")]
    [SerializeField] private AudioSource audioSource;

    [Header("火光时长")]
    [Tooltip("停火后多久把火光清干净（秒）。想更干脆就调小；但别小于连发间隔，否则连发会一闪一闪")]
    [SerializeField] private float stopAfterFireDelay = 0.15f;

    private WeaponController weaponController;   // 同物体父级链上的武器控制器
    private int currentIndex = 0;                // 当前武器下标（切枪时更新）

    private ParticleSystem[][] muzzleSystems;    // 每个火光下的所有粒子系统（Awake 里缓存）

    private float lastFireTime = -1f;            // 最后一次开火的时间（-1 = 没有待清理的火光）

    private void Awake()
    {
        // 脚本挂在 Camera 上，往上找到 WeaponController（和 WeaponViewModel 一样的拿法）
        weaponController = GetComponentInParent<WeaponController>();

        // 音源：没拖就自动找/自动加；配置成 2D（自己的枪声直接听，不受位置影响）
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;    // 进游戏不自动播
        audioSource.spatialBlend = 0f;      // 0 = 2D 音效

        // 缓存每个枪口火光下的粒子系统，并统一复位
        int count = (muzzleEffects != null) ? muzzleEffects.Length : 0;
        muzzleSystems = new ParticleSystem[count][];
        for (int i = 0; i < count; i++)
        {
            if (muzzleEffects[i] == null)
            {
                muzzleSystems[i] = new ParticleSystem[0];   // 空槽给个空数组，后面少判一次空
                continue;
            }

            // ---- 粒子：强制"不循环、不自动播"（不管预制体里怎么勾的）----
            muzzleSystems[i] = muzzleEffects[i].GetComponentsInChildren<ParticleSystem>(true);
            for (int j = 0; j < muzzleSystems[i].Length; j++)
            {
                ParticleSystem ps = muzzleSystems[i][j];
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;   // 主模块（struct，改完自动写回系统）
                main.loop = false;          // 不循环：播一次就停
                main.playOnAwake = false;   // 不自动播：只能被本脚本 Play
            }
            StopMuzzle(i);   // 初始清一次，保证进游戏时枪口是干净的

            // ---- 灯：整个关掉，不参与开火表现 ----
            Light[] lights = muzzleEffects[i].GetComponentsInChildren<Light>(true);
            for (int j = 0; j < lights.Length; j++)
            {
                if (lights[j] == null) continue;
                lights[j].enabled = false;   // 组件先禁用（双保险）
                // 灯一般是独立子物体（比如 Light 物体）：连物体一起关掉。
                // 物体失活后，上面挂的 WFX_LightFlicker 之类脚本的协程也会停止，且不会再被激活
                // （如果灯和粒子在同一个物体上，就只禁用组件，避免连累粒子）
                if (lights[j].gameObject.GetComponent<ParticleSystem>() == null)
                    lights[j].gameObject.SetActive(false);
            }
        }
    }

    private void Update()
    {
        // 停火清尾：开过火、但已经 stopAfterFireDelay 秒没再开火 → 把所有枪口清干净
        if (lastFireTime > 0f && Time.time - lastFireTime > stopAfterFireDelay)
        {
            if (muzzleSystems != null)
                for (int i = 0; i < muzzleSystems.Length; i++) StopMuzzle(i);
            lastFireTime = -1f;   // 清完了就重置，避免每帧重复清
        }
    }

    private void OnEnable()
    {
        if (weaponController == null) return;
        weaponController.OnFired += HandleFired;                 // 开火
        weaponController.OnWeaponChanged += HandleWeaponChanged; // 切枪
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnFired -= HandleFired;                 // 配对取消订阅
        weaponController.OnWeaponChanged -= HandleWeaponChanged;
    }

    // 开火：播当前枪的火光 + 音效
    private void HandleFired()
    {
        PlayMuzzle(currentIndex);
        PlaySound(currentIndex);
        lastFireTime = Time.time;   // 记录开火时刻，停火后用它判断何时清尾
    }

    // 切枪：清掉所有火光残留
    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        currentIndex = index;
        if (muzzleSystems == null) return;
        for (int i = 0; i < muzzleSystems.Length; i++) StopMuzzle(i);
        lastFireTime = -1f;   // 刚清过，重置停火计时
    }

    // 重播某个枪口的火光：先清掉上一发的残影，再从头播
    private void PlayMuzzle(int index)
    {
        if (muzzleSystems == null || index < 0 || index >= muzzleSystems.Length) return;
        ParticleSystem[] systems = muzzleSystems[index];
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            systems[i].Stop();    // 停止发射
            systems[i].Clear();   // 清掉残留粒子（连发时保证每枪都是"新的一闪"）
            systems[i].Play();    // 从头播放（loop 已在 Awake 强制关掉）
        }
    }

    // 停止某个枪口的火光
    private void StopMuzzle(int index)
    {
        if (muzzleSystems == null || index < 0 || index >= muzzleSystems.Length) return;
        ParticleSystem[] systems = muzzleSystems[index];
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] == null) continue;
            systems[i].Stop();
            systems[i].Clear();
        }
    }

    // 播开火音效（没配素材就静默跳过，不报错）
    private void PlaySound(int index)
    {
        if (fireSounds == null || index < 0 || index >= fireSounds.Length) return;
        AudioClip clip = fireSounds[index];
        if (clip == null) return;              // 空槽跳过
        audioSource.PlayOneShot(clip);         // 一发一声，连发时自动叠放
    }
}