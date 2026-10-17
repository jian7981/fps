using UnityEngine;

// ============ 弹孔生成器【新增，1.7d】============
// 订阅 WeaponController.OnHitSurface：在命中点生成一个弹孔贴片
// 生命周期管理：① 单个弹孔 holeLifetime 秒后自动回收；② 场上最多 maxHoles 个，超了覆盖最旧的
// 【阶段2.6 改动】生成/回收改走 SimplePool（对象池）：
//   原来每发都 Instantiate + Destroy(hole, 20f)，同时用 activeHoles 列表做"上限"记账 ——
//   到点被销毁后，列表里那条引用只是变成 null、仍然占着一个名额，导致"上限 40"实际不按存活数生效，
//   还可能提前销毁还活着的弹孔。改成池化后：实例只创建一次反复复用，上限 = 池容量，账目始终准确。
// 挂载：挂 Camera 上（和 WeaponFeedback 同物体）
public class WeaponImpact : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private GameObject bulletHolePrefab;   // 弹孔贴片预制体（自制，见搭建步骤）

    [Header("弹着音效（打在墙/地面上；留空静默）")]
    [SerializeField] private AudioClip impactSound;         // 3D：直接在命中点播，能听出声源方位
    [Range(0f, 1f)] [SerializeField] private float impactVolume = 1f;   // 【调试】音量在 Inspector 调

    [Header("生命周期")]
    [SerializeField] private float holeLifetime = 20f;      // 单个弹孔存活几秒
    [SerializeField] private int maxHoles = 40;             // 场上最多同时存在几个（超出覆盖最旧的）

    private WeaponController weaponController;
    private SimplePool holePool;      // 【阶段2.6】弹孔对象池（上限 = maxHoles，到期自动回收）

    private void Awake()
    {
        weaponController = GetComponentInParent<WeaponController>();
        holePool = new SimplePool(bulletHolePrefab, maxHoles, holeLifetime);
        holePool.Prewarm();   // 预热：避免对枪最激烈时才去创建实例
    }

    private void OnEnable()
    {
        if (weaponController == null) return;
        weaponController.OnHitSurface += SpawnBulletHole;   // 命中墙面/地面 → 生成弹孔
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnHitSurface -= SpawnBulletHole;   // 配对取消订阅
    }

    // 【阶段2.6】每帧回收到期弹孔（池化后不再用 Destroy(go, lifetime) 定时销毁）
    private void Update()
    {
        holePool.ReleaseExpired();
    }

    // 命中表面：生成弹孔
    private void SpawnBulletHole(RaycastHit hit)
    {
        // 弹着音：直接在命中点播（临时音源 + 3D 衰减 → 能听出声源方位）
        if (impactSound != null) AudioSource.PlayClipAtPoint(impactSound, hit.point, impactVolume);

        // 1. 位置：命中点沿法线"抬"出 0.01 米 —— 防止和墙面完全重叠（z-fighting 闪烁）
        Vector3 pos = hit.point + hit.normal * 0.01f;

        // 2. 朝向：贴片的正面朝向法线方向（材质设了双面，所以即使有偏差也看得见）
        Quaternion rot = Quaternion.LookRotation(hit.normal);

        // 3. 从池里取一个（池满时自动覆盖最旧的那个）+ 随机自转（弹孔看起来不重复）
        //    prefab 没配时 Get 返回 null → 等同于"没配就不生成"
        GameObject hole = holePool.Get(pos, rot);
        if (hole == null) return;
        hole.transform.Rotate(0f, 0f, Random.Range(0f, 360f), Space.Self);
    }
}