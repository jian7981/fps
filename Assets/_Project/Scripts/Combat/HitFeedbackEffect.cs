using UnityEngine;

// 【新功能】命中敌人反馈特效（参考"和平精英"：命中处冒一小团烟）
// 订阅 WeaponController.OnHitTarget（只在本机自己的射击反馈链路上触发）→ 在敌人被命中的位置生成烟雾
// 手感原则：明显但不挡视线 —— 小尺寸、短寿命（默认 0.8 秒）、无碰撞
// 挂载：挂 Camera 上（和 WeaponImpact 同物体）
public class HitFeedbackEffect : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private GameObject hitSmokePrefab;      // 命中敌人的烟雾特效预制体

    [Header("命中音效（打中敌人的\"叮\"；留空静默）")]
    [SerializeField] private AudioClip hitSound;             // 2D 播（自己的命中反馈，不吃空间衰减）
    [Range(0f, 1f)] [SerializeField] private float hitVolume = 1f;   // 【调试】音量在 Inspector 调

    [Header("表现")]
    [SerializeField] private float effectLifetime = 0.8f;    // 特效存活秒数（到点自动回收）
    [SerializeField] private float spawnOffset = 0.05f;      // 从命中点沿表面法线偏一点，避免埋进模型里
    [SerializeField] private int maxEffects = 20;            // 【阶段2.6】场上最多同时存在几个烟雾（超出覆盖最旧的）

    private WeaponController weaponController;
    private AudioSource audioSource;   // 命中音用的音源（自动获取/自动添加）
    private SimplePool smokePool;      // 【阶段2.6】命中烟雾对象池（替代每次命中 Instantiate/Destroy）

    private void Awake()
    {
        weaponController = GetComponentInParent<WeaponController>();

        // 音源：没拖就自动找/自动加；配置成 2D（自己的命中反馈，不受位置影响）
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;   // 0 = 2D

        // 【阶段2.6】命中烟雾改走对象池（预热：避免对枪最激烈时才去创建实例）
        smokePool = new SimplePool(hitSmokePrefab, maxEffects, effectLifetime);
        smokePool.Prewarm();
    }

    // 【阶段2.6】每帧回收到期烟雾（池化后不再用 Destroy(go, lifetime) 定时销毁）
    private void Update()
    {
        smokePool.ReleaseExpired();
    }

    private void OnEnable()
    {
        if (weaponController == null) return;
        weaponController.OnHitTarget += SpawnHitSmoke;   // 命中敌人 → 冒烟
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnHitTarget -= SpawnHitSmoke;   // 配对取消订阅
    }

    // 命中敌人：先播"叮"（配了才响），再在命中点生成烟雾（法线朝射手一侧，稍微抬出来一点）
    private void SpawnHitSmoke(RaycastHit hit, Health target)
    {
        if (hitSound != null && audioSource != null)
        {
            // 【修复】音量按次传（PlayOneShot 第二参），不再改音源总音量——
            // 相机上 WeaponFeedback / HitFeedbackEffect 共用同一个 AudioSource，
            // 改总音量会把"正在播放的枪声"一起放大（打中人时枪声突然变响的 bug）
            audioSource.PlayOneShot(hitSound, hitVolume);
        }

        // 【阶段2.6】改走对象池：位置从命中点沿法线抬出一点，池满时覆盖最旧的
        //   （prefab 没配时 Get 返回 null，不会报错，等同于"没配就不生成"）
        Vector3 pos = hit.point + hit.normal * spawnOffset;
        smokePool.Get(pos, Quaternion.identity);
    }
}