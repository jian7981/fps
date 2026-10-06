using UnityEngine;

// 第一人称枪模管理 + 后坐力表现 + 【1.7d】开镜姿态与 FOV + 【1.8a】贴墙压低枪（防穿模）
// 订阅 OnWeaponChanged → 只显示当前武器对应的枪模
// 订阅 OnFired → 给当前枪模一个抖动，然后自动回弹
// 订阅 OnAimChanged → 按"武器类型"决定开镜表现：
//   步枪：枪模平滑挪到屏幕中心 + FOV 缩小
//   狙击：枪模直接隐藏（画面切换成镜筒 UI）+ FOV 大倍率缩小
public class WeaponViewModel : MonoBehaviour
{
    [Header("枪模列表（顺序必须和 WeaponController 的 Weapons 数组一致）")]
    [SerializeField] private Transform[] gunModels;   // 0=Rifle(AK74)，1=Sniper(M107)

    [Header("后坐力参数")]
    [SerializeField] private Vector3 recoilKickPosition = new Vector3(0f, 0.01f, -0.06f); // -Z = 枪往后缩
    [SerializeField] private Vector3 recoilKickRotation = new Vector3(-4f, 0f, 0f);       // X 轴 = 枪口上下抬
    [SerializeField] private float recoilRecoverSpeed = 12f;                              // 回弹速度

    [Header("开镜姿态（顺序同 Gun Models）")]
    [SerializeField] private Vector3[] aimPositions;      // 步枪开镜位置偏移（可留空=用内置默认值；调好后填这里覆盖）
    [SerializeField] private Vector3[] aimRotations;      // 开镜旋转偏移（角度，通常留空）
    [SerializeField] private float fovChangeSpeed = 120f; // FOV 过渡速度（度/秒）

    [Header("贴墙压低枪（防穿模，无动画纯程序化）")]
    [Tooltip("贴墙时枪模的压低偏移（局部坐标）：向下 + 向相机收一点（检测逻辑在 WeaponController 上）")]
    [SerializeField] private Vector3 wallLowerOffset = new Vector3(0f, -0.18f, -0.05f);

    // 【1.7d】内置默认的"步枪开镜偏移"：按 AK74 的实际腰射坐标 (0.235, -0.268, 0.147) 估算
    // 狙击枪用不到它：狙击开镜直接切镜筒，枪会隐藏
    private static readonly Vector3 defaultRifleAimOffset = new Vector3(-0.2f, 0.2f, -0.05f);

    private WeaponController weaponController;   // 父级链上的武器控制器
    private Camera viewCamera;                   // 自己身上的相机（改 FOV 用）
    private float defaultFOV = 60f;              // 初始 FOV（收镜时回到它）

    private int currentIndex = 0;                // 当前显示的枪模下标
    private Vector3[] originPositions;           // 每把枪的原始本地坐标（回弹目标）
    private Quaternion[] originRotations;        // 每把枪的原始本地旋转（回弹目标）
    private Vector3 positionOffset;              // 当前待回弹的位移偏移（后坐力）
    private Vector3 rotationOffset;              // 当前待回弹的旋转偏移（后坐力）

    private bool wantAim;                        // 目标状态：是否开镜（由 OnAimChanged 更新）
    private float aimLerp;                       // 开镜进度 0~1（每帧朝目标平滑插值）

    private void Awake()
    {
        // 脚本挂在 Camera 上，父级就是玩家胶囊 → 直接往上找 WeaponController
        weaponController = GetComponentInParent<WeaponController>();
        viewCamera = GetComponent<Camera>();
        if (viewCamera != null) defaultFOV = viewCamera.fieldOfView;   // 记下初始 FOV

        // 记录每把枪的原始姿态（此时还没被改动过，记下来就是"回弹目标"）
        int count = (gunModels != null) ? gunModels.Length : 0;
        originPositions = new Vector3[count];
        originRotations = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            if (gunModels[i] == null) continue;              // 空槽跳过，防止报错
            originPositions[i] = gunModels[i].localPosition;
            originRotations[i] = gunModels[i].localRotation;
        }
    }

    private void Update()
    {
        if (gunModels == null || currentIndex < 0 || currentIndex >= gunModels.Length) return;
        if (gunModels[currentIndex] == null) return;

        // 1. 后坐力回弹：偏移量每帧往 0 收敛
        positionOffset = Vector3.Lerp(positionOffset, Vector3.zero, recoilRecoverSpeed * Time.deltaTime);
        rotationOffset = Vector3.Lerp(rotationOffset, Vector3.zero, recoilRecoverSpeed * Time.deltaTime);

        // 2. 开镜进度：朝 0（腰射）或 1（开镜）平滑推进
        float aimSpeed = (weaponController != null && weaponController.CurrentWeapon != null)
            ? weaponController.CurrentWeapon.aimSpeed : 12f;   // 没配武器数据时的兜底速度
        aimLerp = Mathf.MoveTowards(aimLerp, wantAim ? 1f : 0f, aimSpeed * Time.deltaTime);

        // 3. 【改，1.8b】收枪程度从 WeaponController 读：检测和平滑都在逻辑层（那边还用它禁止开火），
        //    表现层只负责按这个值把枪压低（下面乘 (1-aimLerp)：开镜时压低自动淡出）
        float wallAmount = (weaponController != null) ? weaponController.WallAmount : 0f;

        // 4. 取当前枪的开镜偏移：步枪=配置/内置默认；狙击=0（不位移，直接切镜筒）
        Vector3 aimPos = GetAimPosition(currentIndex);
        Vector3 aimRot = GetAimRotation(currentIndex);

        // 5. 最终姿态 = 原始姿态 + 后坐力偏移 + 开镜偏移 + 贴墙压低
        //    （贴墙压低乘以 (1-aimLerp)：开镜时压低自动淡出，不会和瞄准姿态打架）
        Transform gun = gunModels[currentIndex];
        gun.localPosition = originPositions[currentIndex] + positionOffset
                            + aimPos * aimLerp
                            + wallLowerOffset * wallAmount * (1f - aimLerp);
        gun.localRotation = originRotations[currentIndex] * Quaternion.Euler(rotationOffset + aimRot * aimLerp);

        // 6. 相机 FOV：开镜缩小视野（画面放大 = 倍率），收镜回到默认
        if (viewCamera != null)
        {
            float targetFOV = defaultFOV;
            if (wantAim && weaponController != null && weaponController.CurrentWeapon != null)
                targetFOV = weaponController.CurrentWeapon.aimFOV;
            viewCamera.fieldOfView = Mathf.MoveTowards(viewCamera.fieldOfView, targetFOV, fovChangeSpeed * Time.deltaTime);
        }

        // 7. 狙击开镜：枪模直接隐藏 —— 画面整块交给镜筒 UI（"只有瞄准镜"的效果）
        bool shouldShowGun = !(wantAim && IsCurrentSniper());
        if (gun.gameObject.activeSelf != shouldShowGun) gun.gameObject.SetActive(shouldShowGun);
    }

    // 【1.7d】当前武器是不是狙击枪（决定开镜表现走哪套模板）
    private bool IsCurrentSniper()
    {
        return weaponController != null && weaponController.CurrentWeapon != null
               && weaponController.CurrentWeapon.weaponType == WeaponType.Sniper;
    }

    // 【1.7d】取开镜位置偏移：
    // 1) Inspector 数组里配了就优先用（想精调覆盖默认值时填它）
    // 2) 狙击：不位移（会直接切镜筒）
    // 3) 其余（步枪类）：用内置默认偏移
    private Vector3 GetAimPosition(int index)
    {
        if (aimPositions != null && index >= 0 && index < aimPositions.Length) return aimPositions[index];
        if (IsCurrentSniper()) return Vector3.zero;
        return defaultRifleAimOffset;
    }

    // 【1.7d】取开镜旋转偏移：没配就是 0（不旋转）
    private Vector3 GetAimRotation(int index)
    {
        if (aimRotations != null && index >= 0 && index < aimRotations.Length) return aimRotations[index];
        return Vector3.zero;
    }

    // 切枪：只激活对应的枪模，并清掉残留偏移
    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        if (gunModels == null || index < 0 || index >= gunModels.Length) return;

        for (int i = 0; i < gunModels.Length; i++)
        {
            if (gunModels[i] == null) continue;
            gunModels[i].gameObject.SetActive(i == index);   // 只有当前武器的枪模显示
        }

        currentIndex = index;
        positionOffset = Vector3.zero;   // 清零，避免新枪一出来就是歪的
        rotationOffset = Vector3.zero;
        aimLerp = 0f;                    // 新枪直接以腰射姿态出现
    }

    private void HandleFired()
    {
        // 直接赋值（不累加）+ 一点随机：连发时每次只抖一下，不会越飘越远
        positionOffset = recoilKickPosition + new Vector3(Random.Range(-0.01f, 0.01f), Random.Range(-0.01f, 0.01f), 0f);
        rotationOffset = recoilKickRotation + new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), 0f);
    }

    // 开镜状态变化：只记录目标，实际姿态在 Update 里平滑过渡
    private void HandleAimChanged(bool aiming)
    {
        wantAim = aiming;
    }

    private void OnEnable()
    {
        // OnEnable 早于所有 Start，能保证收到 WeaponController 在 Start 里发的初始武器事件
        if (weaponController == null) return;
        weaponController.OnWeaponChanged += HandleWeaponChanged;
        weaponController.OnFired += HandleFired;
        weaponController.OnAimChanged += HandleAimChanged;
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnWeaponChanged -= HandleWeaponChanged;
        weaponController.OnFired -= HandleFired;
        weaponController.OnAimChanged -= HandleAimChanged;
    }

    // 死亡镜头用：临时隐藏/恢复所有枪模
    public void SetModelsVisible(bool visible)
    {
        if (gunModels == null) return;
        for (int i = 0; i < gunModels.Length; i++)
        {
            if (gunModels[i] == null) continue;
            // 恢复时只显示当前武器，避免多把枪同时冒出来
            gunModels[i].gameObject.SetActive(visible && i == currentIndex);
        }
    }
}