using System;                  // Action（事件）
using System.Collections;      // IEnumerator（协程）
using UnityEngine;             // Unity 核心
using Mirror;                  // 【阶段2.1】NetworkBehaviour
using Random = UnityEngine.Random;   // 【修复】System 里也有 Random，加别名消除歧义（明确用 Unity 的）

// 武器核心：射击（从 Shooter 并入）+ 弹药 + 换弹 + 切枪 + 【1.7d】开镜 + 腰射散布
// 铁律2 输入与执行分离：Update 只负责"检测输入 → 调用方法"，方法本身不读输入
// 铁律3 伤害走统一入口：命中后只调用 target.TakeDamage
// 联网后（阶段2）：TryFire/Reload/SwitchWeapon 内部改成发 [Command]，FireRay 变成服务端判定
public class WeaponController : NetworkBehaviour
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
    public int CurrentMag => currentMag;                // 【改，2.3-A】读同步值（服务器回写）
    public int CurrentReserve => currentReserve;        // 【改，2.3-A】读同步值（服务器回写）
    public bool IsReloading => isReloading;             // 【改，2.3-A】属性 → [SyncVar] 字段
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

    // ---------- 【阶段2.3-A】网络同步的"当前状态"（服务器权威：服务器改，各端自动收到）----------
    [SyncVar(hook = nameof(HookWeaponIndexChanged))] private int currentIndex;   // 当前武器下标
    [SyncVar(hook = nameof(HookAmmoChanged))] private int currentMag;            // 当前武器弹匣余弹（给 HUD）
    [SyncVar(hook = nameof(HookAmmoChanged))] private int currentReserve;        // 当前武器备弹（给 HUD）
    [SyncVar(hook = nameof(HookReloadingChanged))] private bool isReloading;     // 是否换弹中

    private int[] magAmmo;                 // 每把枪的弹匣余弹（服务器侧真实数据；客户端只读同步值）
    private int[] reserveAmmo;             // 每把枪的备弹（同上）
    private float nextFireTime;            // 下次允许开火的时间点
    private Coroutine reloadCoroutine;     // 换弹协程句柄（切枪时用它打断换弹）
    private Health selfHealth;             // 自己的 Health（死了不能开火）
    private float currentSpread;           // 【新增，1.7d】当前散布：开枪变大、随时间恢复
    private float lastFireTime = -10f;     // 【新增，1.7d】最近一次开火的时间（判断"是否正在连发"用）
    private bool aimLocked;                // 【新增，1.7d】射击后的开镜锁定（狙击"一枪一镜"）：松开右键前不能再开镜
    private float scopeOutTime = -1f;      // 【新增，1.7d】待收镜的时间点（-1 = 没有等待中的收镜）
    private bool matchOverAimLock;         // 【修复】对局已结束（结算弹出后禁止再开镜，镜筒会挡结算）
    private float serverNextFireTime;   // 【阶段2.2】服务器侧的射速校验（防连发宏）

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
        // 【阶段2.3-A】初始化同步值：真实数据先打底（客户端 spawn 时会收到服务器的值）
        currentMag = magAmmo[currentIndex];
        currentReserve = reserveAmmo[currentIndex];
        currentSpread = CurrentWeapon != null ? CurrentWeapon.hipSpread : 0f;   // 【新增，1.7d】初始散布=腰射基础值
    }


    // ---------- 【阶段2.3-A】SyncVar 变化 → 触发原有事件（各端表现层零改动）----------
    private void HookAmmoChanged(int oldValue, int newValue) => OnAmmoChanged?.Invoke();

    private void HookWeaponIndexChanged(int oldValue, int newValue)
    {
        OnWeaponChanged?.Invoke(newValue, CurrentWeapon);   // HUD 换枪名/弹药
        OnAmmoChanged?.Invoke();
    }

    private void HookReloadingChanged(bool oldValue, bool newValue)
    {
        if (newValue) OnReloadStart?.Invoke(CurrentWeapon != null ? CurrentWeapon.reloadTime : 0f);   // HUD"换弹中…"
        else OnReloadEnd?.Invoke();
    }

    // 服务器把"当前这把枪的真实弹药"发布成 SyncVar（扣弹 / 装弹 / 切枪后都要调一次）
    private void PublishCurrentAmmo()
    {
        currentMag = magAmmo[currentIndex];           // 走 SyncVar：各端自动收到
        currentReserve = reserveAmmo[currentIndex];
        RaiseAmmoChanged();                           // 权威侧显式补一次事件（幂等）
    }


    private void Start()
    {
        // 初始武器用事件广播一次，放 Start 是为了保证 HUD 订阅完之后一定能收到
        if (CurrentWeapon != null) OnWeaponChanged?.Invoke(currentIndex, CurrentWeapon);
    }

    private void Update()
    {
        // 【阶段2.1】联机时远程玩家（别人）不响应本机的键鼠输入
        if (!NetUtil.IsLocalControl(this)) return;

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

        // 【阶段2.3-A 改动】不在本机扣弹：上报服务器，由服务器扣减 + SyncVar 回写（HUD 靠事件自动刷新）
        nextFireTime = Time.time + 1f / weapon.fireRate;             // 本机射速节奏（手感，保持不动）

        // 【改，1.7d】顺序很重要：先算"本发的方向"（用开枪前的散布），再增加散布
        // 这样第一发永远最准：狙击开镜指哪打哪；散布增量只影响后续子弹（腰射连发才越打越散）
        Vector3 fireDirection = GetSpreadDirection();
        currentSpread = Mathf.Min(weapon.maxSpread, currentSpread + weapon.spreadPerShot);   // 散布变大（只影响后续子弹）
        lastFireTime = Time.time;                                    // 记住开火时刻（连发期间暂停恢复）

        OnFired?.Invoke();                                           // 广播"开火了"

        // 【阶段2.2】联机：上报服务器裁决伤害；纯单机：本地直接执行（等价原行为）
        Vector3 fireOrigin = aimCamera.transform.position;
        if (NetworkServer.active || NetworkClient.active)
            CmdFire(fireOrigin, fireDirection, currentIndex);      // 上报（方向里已含散布）
        else
            ServerFire(fireOrigin, fireDirection, currentIndex);   // 纯单机直通

        // 本机自己的命中反馈（准星闪红/弹孔）仍用本地射线，即时无延迟（伤害由服务器算）
        FireRayLocalEffects(fireOrigin, fireDirection, weapon);

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
        if (hit.collider.GetComponentInParent<Health>() != null)
            return 0f;    // 命中的是角色（自己或敌人，含其移动胶囊/部位）→ 不算墙
        return Mathf.Clamp01(1f - hit.distance / wallCheckDistance);   // 越近越接近 1
    }

    // 【新增，1.7d】开镜状态切换（唯一入口：输入/切枪/死亡/狙击收镜都走它，状态永远不会乱）
    // 【新增，1.7d】开镜状态切换（唯一入口：输入/切枪/死亡/狙击收镜都走它，状态永远不会乱）
    public void SetAiming(bool wantAim)
    {
        // 死了、这把枪不支持开镜、或对局已结束 → 强制不开
        bool canAimNow = !matchOverAimLock && wantAim && CurrentWeapon != null && CurrentWeapon.canAim
                         && (selfHealth == null || !selfHealth.IsDead);
        if (canAimNow == IsAiming) return;      // 状态没变：什么都不做

        IsAiming = canAimNow;
        if (IsAiming && CurrentWeapon != null)
            currentSpread = Mathf.Min(currentSpread, CurrentWeapon.aimSpread);   // 开镜瞬间直接收紧散布

        OnAimChanged?.Invoke(IsAiming);         // 广播：枪模/FOV/准星/灵敏度都在听
    }

    // 【修复】对局结束（结算面板弹出）时自动收镜：
    // 最后一杀如果开着镜，狙击镜筒会一直开着挡住结算战绩；
    // 这里强制收镜并上锁（否则玩家还按着右键，下一帧又会自动开回去）
    public void CloseAimForMatchOver()
    {
        matchOverAimLock = true;    // 上锁：SetAiming 会拦掉后续所有开镜请求
        aimLocked = false;          // 清掉狙击"一枪一镜"的锁定，避免状态残留
        scopeOutTime = -1f;         // 清掉"待收镜"
        SetAiming(false);           // 立即收镜（触发 OnAimChanged → 镜筒隐藏、准星/枪模恢复）
    }

    // 命中判定核心（阶段2 变成服务端权威）：从相机沿给定方向发射线
    // 【改，1.8a】改用 RaycastAll + 过滤：角色加了"部位碰撞体"后，射线可能先打到自己身上的部位
    //   （手臂/胸口），直接 Raycast 会被自己挡住 → 表现成"子弹打不出去"
    // ================= 【阶段2.2】射击拆分：服务器算伤害，本机算表现 =================

    // 公共：从射线结果里挑出第一个"有效命中"（跳过自己身上的部位、没有 HitBox 的触发器等）
    private bool PickValidHit(Vector3 origin, Vector3 direction, WeaponData weapon, out RaycastHit hit)
    {
        hit = default;
        RaycastHit[] hits = Physics.RaycastAll(origin, direction, weapon.range, hitMask, QueryTriggerInteraction.Collide);
        if (hits.Length == 0) return false;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Health h = hits[i].collider.GetComponentInParent<Health>();
            if (h != null && h == selfHealth) continue;   // 命中自己身上的部位 → 跳过
            if (hits[i].collider.isTrigger && hits[i].collider.GetComponent<HitBox>() == null) continue;
            hit = hits[i];
            return true;
        }
        return false;
    }

    // ① 服务器侧：真正的伤害判定（射线 + 部位倍率 + TakeDamage + 击杀通知）
    //    不用 [Server] 特性：纯单机也要能直通执行，所以手动判"是否服务器侧"
    private void ServerFire(Vector3 origin, Vector3 direction, int weaponIndex)
    {
        if (!NetUtil.IsServerSide()) return;   // 联机中只有服务器执行（纯单机恒为 true）
        if (weapons == null || currentIndex < 0 || currentIndex >= weapons.Length) return;

        // 【阶段2.3-A】不信任上报的 weaponIndex（客户端可伪造）→ 一律用服务器记录的当前武器
        weaponIndex = currentIndex;
        WeaponData weapon = weapons[weaponIndex];

        // 射速校验（防连发宏）：服务器自己记冷却；0.02 秒容差避免正常连发被误拒
        if (Time.time < serverNextFireTime - 0.02f) return;
        serverNextFireTime = Time.time + 1f / weapon.fireRate;

        // 【阶段2.3-A】弹药 / 换弹校验（服务器权威）：打空枪、换弹中开枪的违规请求直接拒绝
        if (isReloading) return;
        if (magAmmo[weaponIndex] <= 0) return;
        magAmmo[weaponIndex]--;                // 服务器扣弹
        PublishCurrentAmmo();                  // 同步给各端（本机 HUD 也刷新）

        if (!PickValidHit(origin, direction, weapon, out RaycastHit hit)) return;

        Health target = hit.collider.GetComponentInParent<Health>();
        if (target == null || target.gameObject == gameObject) return;   // 打到墙/自己 → 没有伤害

        bool wasAlive = !target.IsDead;                                  // 记录"打之前"是否活着
        HitBox hitBox = hit.collider.GetComponent<HitBox>();
        float multiplier = (hitBox != null) ? hitBox.DamageMultiplier : 1f;
        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(weapon.damage * multiplier));

        target.TakeDamage(finalDamage, gameObject);   // ★ 统一伤害入口（服务器执行）

        if (wasAlive && target.IsDead)                // 活着 → 死 = 这一枪是击杀
            NotifyKilledToShooter(target);
    }

    // 击杀通知：联机走 TargetRpc（只发给这把枪的主人）；纯单机直接本地触发
    private void NotifyKilledToShooter(Health victim)
    {
        if (NetworkServer.active || NetworkClient.active) RpcYouKilled(victim);
        else OnKilled?.Invoke(victim);
    }

    [Command]
    private void CmdFire(Vector3 origin, Vector3 direction, int weaponIndex)
    {
        ServerFire(origin, direction, weaponIndex);
    }

    // TargetRpc：在服务器调用、在"这个对象的主人（开枪者）"那台机器上执行
    [TargetRpc]
    private void RpcYouKilled(Health victim)
    {
        OnKilled?.Invoke(victim);   // 加击杀数 / 播"你 击杀了 X"
    }

    // ② 本机表现：命中反馈（准星闪红 / 弹孔火花）——只在开枪者本机跑，不走网络
    private void FireRayLocalEffects(Vector3 origin, Vector3 direction, WeaponData weapon)
    {
        if (!PickValidHit(origin, direction, weapon, out RaycastHit hit)) return;

        Health target = hit.collider.GetComponentInParent<Health>();
        if (target != null && target.gameObject != gameObject)
            OnHitTarget?.Invoke(hit, target);    // 命中敌人反馈（准星闪红）
        else if (target == null)
            OnHitSurface?.Invoke(hit);           // 命中墙面反馈（弹孔/火花）
    }

    // 换弹
    // 换弹：玩家输入 → 只发"请求"，由服务器校验并执行（【阶段2.3-A】）
    public void Reload()
    {
        if (NetworkServer.active || NetworkClient.active) CmdReload();   // 联机：上报
        else ServerReload();                                             // 纯单机：直通
    }

    [Command]
    private void CmdReload() => ServerReload();

    // 服务器侧换弹：先校验（不在换弹、弹匣不满、有备弹）再开始计时
    private void ServerReload()
    {
        if (!NetUtil.IsServerSide()) return;
        WeaponData weapon = CurrentWeapon;
        if (weapon == null || isReloading) return;              // 已经在换弹（客户端重复请求也会被这里挡住）
        if (magAmmo[currentIndex] >= weapon.magSize) return;    // 弹匣是满的
        if (reserveAmmo[currentIndex] <= 0) return;             // 备弹为 0

        reloadCoroutine = StartCoroutine(ServerReloadRoutine(weapon));
    }

    // 换弹计时完全在服务器跑（客户端只等 SyncVar：isReloading 的 hook 驱动"换弹中…"和结束刷新）
    private IEnumerator ServerReloadRoutine(WeaponData weapon)
    {
        isReloading = true;                                  // 走 SyncVar：各端 HookReloadingChanged → OnReloadStart
        OnReloadStart?.Invoke(weapon.reloadTime);            // 权威侧显式补一次（单机/主机）
        if (debugLogAmmo) Debug.Log($"[武器] 开始换弹：{weapon.weaponName}，耗时 {weapon.reloadTime} 秒");

        yield return new WaitForSeconds(weapon.reloadTime);  // 等待换弹时间

        int need = weapon.magSize - magAmmo[currentIndex];       // 还差多少发装满
        int load = Mathf.Min(need, reserveAmmo[currentIndex]);   // 备弹不够就只装这么多
        magAmmo[currentIndex] += load;                       // 装弹
        reserveAmmo[currentIndex] -= load;                   // 扣备弹

        isReloading = false;                                 // 走 SyncVar：各端 HookReloadingChanged → OnReloadEnd
        reloadCoroutine = null;                              // 清空协程句柄
        PublishCurrentAmmo();                                // 装弹后的弹药同步（HUD 刷新）
        OnReloadEnd?.Invoke();                               // 权威侧显式补一次
        if (debugLogAmmo) Debug.Log($"[武器] 换弹完成：{weapon.weaponName}");
    }

    // 切枪：index 越界会自动循环（-1 → 最后一把，等于长度 → 第一把）
    // 切枪：index 越界会自动循环（-1 → 最后一把，等于长度 → 第一把）
    // 【阶段2.3-A 改动】联机：只发"请求"，由服务器切换（SyncVar 回写后各端一起换）
    public void SwitchWeapon(int index)
    {
        if (weapons == null || weapons.Length == 0) return;

        // 【修复】开镜中禁止切枪（含狙击开火后的"延迟收镜"窗口；松镜后自然恢复）
        if (IsAiming) return ;

        if (index < 0) index = weapons.Length - 1;       // 往前越界 → 绕到最后一把
        if (index >= weapons.Length) index = 0;          // 往后越界 → 绕回第一把
        if (index == currentIndex) return;               // 就是当前这把，不做任何事

        // 本机手感立刻生效（开镜/锁定/射速冷却都是本地表现，不等网络往返）
        SetAiming(false);                                // 切枪自动收镜
        aimLocked = false;                               // 切枪清掉开镜锁定
        scopeOutTime = -1f;                              // 切枪清掉"待收镜"
        nextFireTime = Time.time;                        // 允许立刻开火（不受上一把枪的射速冷却影响）

        if (NetworkServer.active || NetworkClient.active) CmdSwitchWeapon(index);   // 联机：上报
        else ServerSwitchWeapon(index);                                              // 纯单机：直通
    }

    [Command]
    private void CmdSwitchWeapon(int index) => ServerSwitchWeapon(index);

    // 服务器侧切枪：打断换弹 + 改 SyncVar（各端 hook → 枪模/HUD 刷新）+ 发布新枪弹药
    private void ServerSwitchWeapon(int index)
    {
        if (!NetUtil.IsServerSide()) return;
        if (weapons == null || index < 0 || index >= weapons.Length) return;
        if (index == currentIndex) return;

        CancelReload();                                  // 切枪打断换弹（否则换弹会隔着武器"偷偷完成"）
        currentIndex = index;                            // 走 SyncVar → 各端 HookWeaponIndexChanged
        serverNextFireTime = Time.time;                  // 服务器侧同样放开射速冷却
        OnWeaponChanged?.Invoke(currentIndex, CurrentWeapon);   // 权威侧显式补发
        PublishCurrentAmmo();                            // 新枪弹药同步 + 本机事件
    }

    // 打断换弹：停协程 + 复位状态（【阶段2.3-A】只由服务器侧调用）
    private void CancelReload()
    {
        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            reloadCoroutine = null;
        }
        isReloading = false;                             // 走 SyncVar：各端解除换弹状态
    }

    // 弹药变化的统一出口：先广播事件，再（可选）打印调试信息
    private void RaiseAmmoChanged()
    {
        OnAmmoChanged?.Invoke();
        if (debugLogAmmo && CurrentWeapon != null)
            Debug.Log($"[武器] {CurrentWeapon.weaponName} 弹匣 {CurrentMag}/{CurrentWeapon.magSize}，备弹 {CurrentReserve}");
    }
}