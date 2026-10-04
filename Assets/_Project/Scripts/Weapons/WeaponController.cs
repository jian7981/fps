using System;                  // Action（事件）
using System.Collections;      // IEnumerator（协程）
using UnityEngine;             // Unity 核心

// 武器核心：射击（从 Shooter 并入）+ 弹药 + 换弹 + 切枪
// 铁律2 输入与执行分离：Update 只负责"检测输入 → 调用方法"，方法本身不读输入
// 铁律3 伤害走统一入口：命中后只调用 target.TakeDamage
// 联网后（阶段2）：TryFire/Reload/SwitchWeapon 内部改成发 [Command]，FireRay 变成服务端判定
public class WeaponController : MonoBehaviour
{
    [Header("武器列表（拖 WeaponData 资产进来，顺序 = 数字键顺序）")]
    [SerializeField] private WeaponData[] weapons;

    [Header("引用")]
    [SerializeField] private Camera aimCamera;         // 留空 = 自动找子物体里的相机
    [SerializeField] private LayerMask hitMask = ~0;   // 射线检测层（~0 = 全部）

    [Header("调试（步骤1.3 做完 HUD 后把这个勾掉）")]
    [SerializeField] private bool debugLogAmmo = true; // 在 Console 打印弹药/换弹过程，方便现在验证

    // ---------- 对外只读状态（HUD 以后读这些）----------
    public WeaponData CurrentWeapon => (weapons != null && weapons.Length > 0) ? weapons[currentIndex] : null;
    public int CurrentMag => (magAmmo != null && magAmmo.Length > 0) ? magAmmo[currentIndex] : 0;          // 当前弹匣余弹
    public int CurrentReserve => (reserveAmmo != null && reserveAmmo.Length > 0) ? reserveAmmo[currentIndex] : 0; // 当前备弹
    public bool IsReloading { get; private set; }       // 是否正在换弹（换弹中不能开火）
    public bool CanFire => Time.time >= nextFireTime;   // 射速冷却好了没

    // ---------- 事件：只广播，不播表现 ----------
    public event Action OnFired;                            // 开火（枪口火焰/音效/后坐力）
    public event Action<RaycastHit, Health> OnHitTarget;    // 命中目标（准星变红）
    public event Action<RaycastHit> OnHitSurface;           // 命中墙面（弹孔/火花）
    public event Action OnAmmoChanged;                      // 弹药数变化（HUD）
    public event Action<int, WeaponData> OnWeaponChanged;   // 切枪（HUD 换名字/图标）
    public event Action<float> OnReloadStart;               // 开始换弹，参数 = 总时长秒数（HUD 画进度条）
    public event Action OnReloadEnd;                        // 换弹结束
    public event Action<Health> OnKilled;   // 【新增】击杀事件：只有"这一枪把对方打死"时才触发

    private int currentIndex = 0;          // 当前武器下标
    private int[] magAmmo;                 // 每把枪的弹匣余弹（和 weapons 一一对应，唯一数据源）
    private int[] reserveAmmo;             // 每把枪的备弹
    private float nextFireTime;            // 下次允许开火的时间点
    private Coroutine reloadCoroutine;     // 换弹协程句柄（切枪时用它打断换弹）
    private Health selfHealth;             // 自己的 Health（死了不能开火）

    private void Awake()
    {
        if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();
        selfHealth = GetComponent<Health>();

        if (weapons == null || weapons.Length == 0)
        {
            Debug.LogError("[WeaponController] 武器列表是空的，请在 Inspector 里拖入 WeaponData 资产");
            return;
        }

        // 初始化：每把枪都是满弹匣 + 满备弹
        magAmmo = new int[weapons.Length];
        reserveAmmo = new int[weapons.Length];
        for (int i = 0; i < weapons.Length; i++)
        {
            magAmmo[i] = weapons[i].magSize;
            reserveAmmo[i] = weapons[i].reserveAmmo;
        }
    }

    private void Start()
    {
        // 初始武器用事件广播一次，放 Start 是为了保证 HUD 订阅完之后一定能收到
        if (CurrentWeapon != null) OnWeaponChanged?.Invoke(currentIndex, CurrentWeapon);
    }

    private void Update()
    {
        // 【输入与执行分离】这里只做"检测输入 → 调方法"
        if (Input.GetMouseButton(0)) TryFire();                        // 左键：按住连发
        if (Input.GetKeyDown(KeyCode.R)) Reload();                     // R：手动换弹
        if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchWeapon(0);         // 1/2/3：切枪
        if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchWeapon(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchWeapon(2);

        float scroll = Input.GetAxis("Mouse ScrollWheel");             // 滚轮：循环切枪
        if (scroll > 0.01f) SwitchWeapon(currentIndex - 1);
        else if (scroll < -0.01f) SwitchWeapon(currentIndex + 1);
    }

    // ★ 开火入口（不读输入）
    public bool TryFire()
    {
        WeaponData weapon = CurrentWeapon;
        if (weapon == null) return false;
        if (selfHealth != null && selfHealth.IsDead) return false;   // 死了不能开火
        if (IsReloading) return false;                               // 换弹中不能开火
        if (!CanFire) return false;                                  // 射速冷却中
        if (aimCamera == null) return false;

        if (CurrentMag <= 0)                                         // 弹匣打空
        {
            if (weapon.autoReloadWhenEmpty) Reload();                // 自动换弹
            return false;
        }

        magAmmo[currentIndex]--;                                     // 消耗一发子弹
        nextFireTime = Time.time + 1f / weapon.fireRate;             // 记录下次可开火时间
        RaiseAmmoChanged();                                          // 通知 HUD + 调试打印
        OnFired?.Invoke();                                           // 广播"开火了"

        FireRay(aimCamera.transform.position, aimCamera.transform.forward, weapon);
        return true;
    }

    // 命中判定核心（阶段2 变成服务端权威）：从相机沿正前方发射线
    private void FireRay(Vector3 origin, Vector3 direction, WeaponData weapon)
    {
        // QueryTriggerInteraction.Ignore：忽略触发器碰撞体，不让它挡子弹
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, weapon.range, hitMask, QueryTriggerInteraction.Ignore))
            return;   // 射程内什么都没打到

        // 从命中的碰撞体往上找 Health（Health 挂在角色根物体上）
        Health target = hit.collider.GetComponentInParent<Health>();

        if (target != null && target.gameObject != gameObject)
        {
            bool wasAlive = !target.IsDead;                 // 【新增】记录"打之前"是否活着
            target.TakeDamage(weapon.damage, gameObject);   // ★ 统一伤害入口
            OnHitTarget?.Invoke(hit, target);               // 命中反馈（准星闪红）照常

            if (wasAlive && target.IsDead)                  // 【新增】活着 → 死 = 这一枪是击杀
                OnKilled?.Invoke(target);
            return;
        }

        if (target == null) OnHitSurface?.Invoke(hit);            // 打到墙/地面
    }

    // 换弹
    public void Reload()
    {
        WeaponData weapon = CurrentWeapon;
        if (weapon == null || IsReloading) return;      // 已经在换弹
        if (CurrentMag >= weapon.magSize) return;       // 弹匣是满的
        if (CurrentReserve <= 0) return;                // 备弹为 0

        reloadCoroutine = StartCoroutine(ReloadRoutine(weapon));
    }

    private IEnumerator ReloadRoutine(WeaponData weapon)
    {
        IsReloading = true;                                  // 标记换弹中（TryFire 会拦住开火）
        OnReloadStart?.Invoke(weapon.reloadTime);            // 广播换弹开始
        if (debugLogAmmo) Debug.Log($"[武器] 开始换弹：{weapon.weaponName}，耗时 {weapon.reloadTime} 秒");

        yield return new WaitForSeconds(weapon.reloadTime);  // 等待换弹时间

        int need = weapon.magSize - CurrentMag;              // 还差多少发装满
        int load = Mathf.Min(need, CurrentReserve);          // 备弹不够就只装这么多
        magAmmo[currentIndex] += load;                       // 装弹
        reserveAmmo[currentIndex] -= load;                   // 扣备弹

        IsReloading = false;                                 // 解除换弹状态
        reloadCoroutine = null;                              // 清空协程句柄
        RaiseAmmoChanged();                                  // 通知弹药变化
        OnReloadEnd?.Invoke();                               // 广播换弹结束
        if (debugLogAmmo) Debug.Log($"[武器] 换弹完成：{weapon.weaponName}");
    }

    // 切枪：index 越界会自动循环（-1 → 最后一把，等于长度 → 第一把）
    public void SwitchWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0) return;

        if (index < 0) index = weapons.Length - 1;       // 往前越界 → 绕到最后一把
        if (index >= weapons.Length) index = 0;          // 往后越界 → 绕回第一把
        if (index == currentIndex) return;               // 就是当前这把，不做任何事

        CancelReload();                                  // 切枪打断换弹（否则换弹会隔着武器"偷偷完成"）

        currentIndex = index;                            // 切换下标
        nextFireTime = Time.time;                        // 允许立刻开火（不受上一把枪的射速冷却影响）

        OnWeaponChanged?.Invoke(currentIndex, CurrentWeapon);   // 广播切枪
        RaiseAmmoChanged();                                     // 广播新枪的弹药数
    }

    // 打断换弹：停协程 + 复位状态
    private void CancelReload()
    {
        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            reloadCoroutine = null;
        }
        IsReloading = false;
    }

    // 弹药变化的统一出口：先广播事件，再（可选）打印调试信息
    private void RaiseAmmoChanged()
    {
        OnAmmoChanged?.Invoke();
        if (debugLogAmmo && CurrentWeapon != null)
            Debug.Log($"[武器] {CurrentWeapon.weaponName} 弹匣 {CurrentMag}/{CurrentWeapon.magSize}，备弹 {CurrentReserve}");
    }
}