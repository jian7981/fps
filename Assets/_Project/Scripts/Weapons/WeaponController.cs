using System;                  // Action（事件）
using System.Collections;      // IEnumerator（协程）
using UnityEngine;             // Unity 核心
using Random = UnityEngine.Random;   // 【修复】System 里也有 Random，加别名消除歧义（明确用 Unity 的）

// 武器核心：射击（从 Shooter 并入）+ 弹药 + 换弹 + 切枪 + 【1.7d】开镜 + 腰射散布
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

    [Header("散布手感")]
    [Tooltip("腰射连发时暂停散布恢复的时长（秒）：停火超过这个时间，散布才开始回落")]
    [SerializeField] private float sprayHoldTime = 0.25f;   // 【新增，1.7d】越大 → 连发越打越散

    [Header("狙击手感")]
    [Tooltip("狙击开火后延迟多久收镜（秒）：给镜头留出“枪响”的时间；想让收镜更干脆就调小（比如 0.15）")]
    [SerializeField] private float scopeOutDelay = 0.25f;   // 【新增，1.7d】狙击延迟收镜时长

    [Header("贴墙收枪（防穿模 + 收枪时禁止开火）")]
    [Tooltip("向前探测墙的距离（米）：墙越近，收枪越深")]
    [SerializeField] private float wallCheckDistance = 0.7f;
    [Tooltip("收枪到什么程度就不能开火（0~1）：0.35 = 枪压到 35% 就不允许射击")]
    [SerializeField] private float blockFireThreshold = 0.35f;
    [SerializeField] private float wallLowerSpeed = 8f;   // 【新增，1.8b】收枪/回正的过渡速度（越大越干脆）

    // ---------- 对外只读状态（HUD 以后读这些）----------
    public WeaponData CurrentWeapon => (weapons != null && weapons.Length > 0) ? weapons[currentIndex] : null;
    public int CurrentMag => (magAmmo != null && magAmmo.Length > 0) ? magAmmo[currentIndex] : 0;          // 当前弹匣余弹
    public int CurrentReserve => (reserveAmmo != null && reserveAmmo.Length > 0) ? reserveAmmo[currentIndex] : 0; // 当前备弹
    public bool IsReloading { get; private set; }       // 是否正在换弹（换弹中不能开火）
    public bool CanFire => Time.time >= nextFireTime;   // 射速冷却好了没
    public bool IsAiming { get; private set; }        // 【新增，1.7d】是否正在开镜
    public float CurrentSpread => currentSpread;      // 【新增，1.7d】当前散布（度）——准星读它画大小
    public float WallAmount { get; private set; }     // 【新增，1.8b】贴墙收枪程度 0~1（表现层拿去做压低姿态）
    public bool IsWeaponBlocked { get; private set; } // 【新增，1.8b】收枪中（枪被墙挡住，不能开火）

    // ---------- 事件：只广播，不播表现 ----------
    public event Action OnFired;                            // 开火（枪口火焰/音效/后坐力）
    public event Action<RaycastHit, Health> OnHitTarget;    // 命中目标（准星变红）
    public event Action<RaycastHit> OnHitSurface;           // 命中墙面（弹孔/火花）
    public event Action OnAmmoChanged;                      // 弹药数变化（HUD）
    public event Action<int, WeaponData> OnWeaponChanged;   // 切枪（HUD 换名字/图标）
    public event Action<float> OnReloadStart;               // 开始换弹，参数 = 总时长秒数（HUD 画进度条）
    public event Action OnReloadEnd;                        // 换弹结束
    public event Action<Health> OnKilled;   // 【新增】击杀事件：只有"这一枪把对方打死"时才触发
    public event Action<bool> OnAimChanged;   // 【新增，1.7d】开镜/收镜（true = 进入开镜）

    private int currentIndex = 0;          // 当前武器下标
    private int[] magAmmo;                 // 每把枪的弹匣余弹（和 weapons 一一对应，唯一数据源）
    private int[] reserveAmmo;             // 每把枪的备弹
    private float nextFireTime;            // 下次允许开火的时间点
    private Coroutine reloadCoroutine;     // 换弹协程句柄（切枪时用它打断换弹）
    private Health selfHealth;             // 自己的 Health（死了不能开火）
    private float currentSpread;           // 【新增，1.7d】当前散布：开枪变大、随时间恢复
    private float lastFireTime = -10f;     // 【新增，1.7d】最近一次开火的时间（判断"是否正在连发"用）
    private bool aimLocked;                // 【新增，1.7d】射击后的开镜锁定（狙击"一枪一镜"）：松开右键前不能再开镜
    private float scopeOutTime = -1f;      // 【新增，1.7d】待收镜的时间点（-1 = 没有等待中的收镜）

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
        currentSpread = CurrentWeapon != null ? CurrentWeapon.hipSpread : 0f;   // 【新增，1.7d】初始散布=腰射基础值
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

        // 【改，1.7d】右键按住=开镜；狙击开火后有"延迟收镜 + 锁定"（见下）
        bool rightHeld = Input.GetMouseButton(1);
        if (!rightHeld) aimLocked = false;             // 松开右键 → 解除射击锁定

        // 狙击延迟收镜：开火后先保持开镜 scopeOutDelay 秒（让"枪响"播完），时间到再收镜
        if (scopeOutTime > 0f && Time.time >= scopeOutTime)
        {
            scopeOutTime = -1f;        // 清掉"待收镜"标志
            SetAiming(false);          // 执行收镜
            aimLocked = true;          // 收镜后仍需松开右键才能重开（防止按住连狙）
        }

        // "待收镜"期间忽略输入（镜保持开着）；其余时间按输入正常开镜/收镜
        if (scopeOutTime <= 0f) SetAiming(rightHeld && !aimLocked);
        UpdateSpread();                                // 每帧让散布自然恢复（视觉上=准星收拢）
        UpdateWallLower();                             // 【新增，1.8b】每帧检测贴墙收枪（并决定能否开火）

        // 数字键 1~9：按几就切第几把（武器不够 9 把时，按到多余的键不响应）
        for (int i = 0; i < 9; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;           // 这个数字键没按，检查下一个
            if (weapons != null && i < weapons.Length) SwitchWeapon(i);    // 有这把武器才切
            break;   // 一帧只处理一个数字键，处理完就不再往后查
        }

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
        if (IsWeaponBlocked) return false;                           // 【新增，1.8b】贴墙收枪中：不能开火

        if (CurrentMag <= 0)                                         // 弹匣打空
        {
            if (weapon.autoReloadWhenEmpty) Reload();                // 自动换弹
            return false;
        }

        magAmmo[currentIndex]--;                                     // 消耗一发子弹
        nextFireTime = Time.time + 1f / weapon.fireRate;             // 记录下次可开火时间

        // 【改，1.7d】顺序很重要：先算"本发的方向"（用开枪前的散布），再增加散布
        // 这样第一发永远最准：狙击开镜指哪打哪；散布增量只影响后续子弹（腰射连发才越打越散）
        Vector3 fireDirection = GetSpreadDirection();
        currentSpread = Mathf.Min(weapon.maxSpread, currentSpread + weapon.spreadPerShot);   // 散布变大（只影响后续子弹）
        lastFireTime = Time.time;                                    // 记住开火时刻（连发期间暂停恢复）

        RaiseAmmoChanged();                                          // 通知 HUD + 调试打印
        OnFired?.Invoke();                                           // 广播"开火了"

        FireRay(aimCamera.transform.position, fireDirection, weapon);   // 用刚才算好的方向打

        // 【新增，1.7d】狙击"一枪一镜"：开镜状态下开火 → 延迟 scopeOutDelay 秒后强制收镜
        // 延迟期间镜头保持在镜里（能看到枪响/火光/命中），到点自动收镜；
        // 锁定 = 松开右键前不能再开镜（所以按住右键也连不起来）
        if (weapon.weaponType == WeaponType.Sniper && IsAiming)
        {
            aimLocked = true;                             // 先锁定开镜
            scopeOutTime = Time.time + scopeOutDelay;     // 延迟收镜（-1 以外 = 有等待中的收镜）
        }

        return true;
    }

    // 【新增，1.7d】在开火方向上叠加随机散布
    // 关键：半径取 sqrt(random)，否则随机点会往圆心堆，散布看起来"不够散"
    private Vector3 GetSpreadDirection()
    {
        if (currentSpread <= 0.001f) return aimCamera.transform.forward;   // 没有散布 → 直射
        float angle = Random.Range(0f, Mathf.PI * 2f);                     // 随机方向（弧度）
        float radius = Mathf.Sqrt(Random.value) * currentSpread;           // 随机半径（单位：度）
        Quaternion offset = Quaternion.Euler(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, 0f);
        return offset * aimCamera.transform.forward;                       // 相机局部小角度旋转后的方向
    }

    // 【新增，1.7d】散布恢复：每帧朝"当前状态的基础散布"收敛
    // 腰射连发时"暂停恢复"（重要）：否则恢复速度会吃掉每发的增长，连发永远散不起来
    //   例：射速 8 发/秒 × 每发 +0.5 度 = 每秒增长 4 度，而恢复每秒 5 度 → 散布根本升不上去
    // 开镜时恢复照常进行：连发也能保持精准（开镜更稳的手感就是这么来的）
    private void UpdateSpread()
    {
        WeaponData weapon = CurrentWeapon;
        if (weapon == null) return;

        // 腰射 + 刚开过火（sprayHoldTime 秒内）→ 暂停恢复，让散布一路累积（越打越散）
        if (!IsAiming && Time.time - lastFireTime < sprayHoldTime) return;

        float baseSpread = IsAiming ? weapon.aimSpread : weapon.hipSpread;
        currentSpread = Mathf.MoveTowards(currentSpread, baseSpread, weapon.spreadRecover * Time.deltaTime);
    }

    // 【新增，1.8b】贴墙收枪检测：相机正前方 + 右前方 45° 各一条短射线（枪在右下角，这两个方向最容易被墙挡）
    // 一份数据管两件事：① 枪模压低（表现层读 WallAmount）② 收枪中禁止开火（IsWeaponBlocked）
    private void UpdateWallLower()
    {
        float target = 0f;
        if (aimCamera != null)
        {
            float front = WallCheck(aimCamera.transform.forward);   // 正前方
            Vector3 diagonal = (aimCamera.transform.forward + aimCamera.transform.right).normalized;
            float rightFront = WallCheck(diagonal);                 // 右前方 45°
            target = Mathf.Max(front, rightFront);                  // 哪个方向更贴近用哪个
        }

        WallAmount = Mathf.MoveTowards(WallAmount, target, wallLowerSpeed * Time.deltaTime);

        // 收枪超过阈值 → 禁止开火；开镜时枪已抬到画面中央，不受此限制
        IsWeaponBlocked = !IsAiming && WallAmount >= blockFireThreshold;
    }

    // 单条射线：命中墙返回"贴近程度"（越近越接近 1）；没东西 / 命中的是自己身上的部位 → 0
    private float WallCheck(Vector3 direction)
    {
        Vector3 origin = aimCamera.transform.position;
        if (!Physics.Raycast(origin, direction, out RaycastHit hit, wallCheckDistance, ~0, QueryTriggerInteraction.Ignore))
            return 0f;    // 这个方向上没东西（触发器被 Ignore，部位碰撞体不会误判）
        if (selfHealth != null && hit.collider.GetComponentInParent<Health>() == selfHealth)
            return 0f;    // 打到自己身上的部位（HitBox）→ 不算墙
        return Mathf.Clamp01(1f - hit.distance / wallCheckDistance);   // 越近越接近 1
    }

    // 【新增，1.7d】开镜状态切换（唯一入口：输入/切枪/死亡/狙击收镜都走它，状态永远不会乱）
    public void SetAiming(bool wantAim)
    {
        // 死了、或这把枪不支持开镜 → 强制不开
        bool canAimNow = wantAim && CurrentWeapon != null && CurrentWeapon.canAim
                         && (selfHealth == null || !selfHealth.IsDead);
        if (canAimNow == IsAiming) return;      // 状态没变：什么都不做

        IsAiming = canAimNow;
        if (IsAiming && CurrentWeapon != null)
            currentSpread = Mathf.Min(currentSpread, CurrentWeapon.aimSpread);   // 开镜瞬间直接收紧散布

        OnAimChanged?.Invoke(IsAiming);         // 广播：枪模/FOV/准星/灵敏度都在听
    }

    // 命中判定核心（阶段2 变成服务端权威）：从相机沿给定方向发射线
    // 【改，1.8a】改用 RaycastAll + 过滤：角色加了"部位碰撞体"后，射线可能先打到自己身上的部位
    //   （手臂/胸口），直接 Raycast 会被自己挡住 → 表现成"子弹打不出去"
    private void FireRay(Vector3 origin, Vector3 direction, WeaponData weapon)
    {
        // Collide：允许命中"触发器"——部位碰撞体勾了 Is Trigger（这样不会阻挡 CharacterController 移动），
        // 而射线默认会忽略触发器，所以要显式允许
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, weapon.range, hitMask, QueryTriggerInteraction.Collide);
        if (hits.Length == 0) return;   // 射程内什么都没打到

        // RaycastAll 不保证顺序 → 按距离从近到远排序（先处理近的，跳过自己）
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        RaycastHit hit = default;   // 最终选中的那个命中
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Health h = hits[i].collider.GetComponentInParent<Health>();
            if (h != null && h == selfHealth) continue;   // 命中自己身上的部位 → 跳过，继续往后找

            // 跳过"没有 HitBox 标签的触发器"（场景里的触发区域不该挡子弹）
            if (hits[i].collider.isTrigger && hits[i].collider.GetComponent<HitBox>() == null) continue;

            hit = hits[i];
            found = true;
            break;
        }
        if (!found) return;   // 全是自己/无关触发器 → 当作没命中

        // 从命中的碰撞体往上找 Health（Health 挂在角色根物体上）
        Health target = hit.collider.GetComponentInParent<Health>();

        if (target != null && target.gameObject != gameObject)
        {
            bool wasAlive = !target.IsDead;                 // 记录"打之前"是否活着

            // 【新增，1.8a】部位伤害：命中带 HitBox 标签的部位就按倍率算（头 2 / 四肢 0.75 / 躯干 1）
            HitBox hitBox = hit.collider.GetComponent<HitBox>();
            float multiplier = (hitBox != null) ? hitBox.DamageMultiplier : 1f;
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(weapon.damage * multiplier));   // 至少 1 点

            target.TakeDamage(finalDamage, gameObject);   // ★ 统一伤害入口
            OnHitTarget?.Invoke(hit, target);             // 命中反馈（准星闪红）照常

            if (wasAlive && target.IsDead)                // 活着 → 死 = 这一枪是击杀
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
        SetAiming(false);                                // 【新增，1.7d】切枪自动收镜
        aimLocked = false;                               // 【新增，1.7d】切枪清掉开镜锁定
        scopeOutTime = -1f;                              // 【新增，1.7d】切枪清掉"待收镜"

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