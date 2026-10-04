using UnityEngine;

// 第一人称枪模管理 + 后坐力表现（挂在 Camera 上）
// 订阅 OnWeaponChanged → 只显示当前武器对应的枪模
// 订阅 OnFired → 给当前枪模一个抖动，然后自动回弹
public class WeaponViewModel : MonoBehaviour
{
    [Header("枪模列表（顺序必须和 WeaponController 的 Weapons 数组一致）")]
    [SerializeField] private Transform[] gunModels;   // 1=Rifle(AK74)，2=Pistol(M1911)，3=Sniper(M107)

    [Header("后坐力参数")]
    [SerializeField] private Vector3 recoilKickPosition = new Vector3(0f, 0.01f, -0.06f); // -Z = 枪往后缩
    [SerializeField] private Vector3 recoilKickRotation = new Vector3(-4f, 0f, 0f);       // X 轴 = 枪口上下抬
    [SerializeField] private float recoilRecoverSpeed = 12f;                              // 回弹速度

    private WeaponController weaponController;   // 父级链上的武器控制器
    private int currentIndex = 0;                // 当前显示的枪模下标

    private Vector3[] originPositions;           // 每把枪的原始本地坐标（回弹目标）
    private Quaternion[] originRotations;        // 每把枪的原始本地旋转（回弹目标）

    private Vector3 positionOffset;              // 当前待回弹的位移偏移
    private Vector3 rotationOffset;              // 当前待回弹的旋转偏移

    private void Awake()
    {
        // 脚本挂在 Camera 上，父级就是玩家胶囊 → 直接往上找 WeaponController
        weaponController = GetComponentInParent<WeaponController>();

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

        // 回弹：偏移量每帧往 0 收敛
        positionOffset = Vector3.Lerp(positionOffset, Vector3.zero, recoilRecoverSpeed * Time.deltaTime);
        rotationOffset = Vector3.Lerp(rotationOffset, Vector3.zero, recoilRecoverSpeed * Time.deltaTime);

        // 只作用在当前显示的那把枪上：原始姿态 + 当前偏移
        Transform gun = gunModels[currentIndex];
        gun.localPosition = originPositions[currentIndex] + positionOffset;
        gun.localRotation = originRotations[currentIndex] * Quaternion.Euler(rotationOffset);
    }

    // 切枪：只激活对应的枪模，并清掉上一把枪残留的后坐力
    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        if (gunModels == null || index < 0 || index >= gunModels.Length) return;

        for (int i = 0; i < gunModels.Length; i++)
        {
            if (gunModels[i] == null) continue;
            gunModels[i].gameObject.SetActive(i == index);   // 只有当前武器的枪模显示
        }

        currentIndex = index;
        positionOffset = Vector3.zero;                       // 清零，避免新枪一出来就是歪的
        rotationOffset = Vector3.zero;
    }

    private void HandleFired()
    {
        // 直接赋值（不累加）+ 一点随机：连发时每次只抖一下，不会越飘越远
        positionOffset = recoilKickPosition + new Vector3(Random.Range(-0.01f, 0.01f), Random.Range(-0.01f, 0.01f), 0f);
        rotationOffset = recoilKickRotation + new Vector3(Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), 0f);
    }

    private void OnEnable()
    {
        // OnEnable 早于所有 Start，能保证收到 WeaponController 在 Start 里发的初始武器事件
        if (weaponController == null) return;
        weaponController.OnWeaponChanged += HandleWeaponChanged;
        weaponController.OnFired += HandleFired;
    }

    private void OnDisable()
    {
        if (weaponController == null) return;
        weaponController.OnWeaponChanged -= HandleWeaponChanged;
        weaponController.OnFired -= HandleFired;
    }

    // 【新增】死亡镜头用：临时隐藏/恢复所有枪模
    public void SetModelsVisible(bool visible)
    {
        if (gunModels == null) return;
        for (int i = 0; i < gunModels.Length; i++)
        {
            if (gunModels[i] == null) continue;
            // 恢复时只显示当前武器，避免三把枪同时冒出来
            gunModels[i].gameObject.SetActive(visible && i == currentIndex);
        }
    }
}