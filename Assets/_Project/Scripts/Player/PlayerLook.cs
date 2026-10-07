using UnityEngine;
using Mirror;   // 【阶段2.1】NetworkBehaviour

// 第一人称视角：鼠标转视角 + 开镜灵敏度缩放 + 【1.7d】镜头后坐力
public class PlayerLook : NetworkBehaviour
{
    [Header("视角参数")]
    public Camera cam;
    public float mouseSensitivity = 200f;
    public float maxLookAngle = 85f;   // 上下视角上限（抬头低头幅度）

    private float xRotation = 0f;      // 基准俯仰角（只被鼠标改，不被后坐力污染）
    private Health health;             // 血量组件（死亡时锁视角）
    private WeaponController weapons;  // 武器控制器（灵敏度缩放 + 后坐力数据来源）

    // 【新增，1.7d】镜头后坐力 = 一个"临时偏移层"：
    // x = 上下偏移（度，负=抬头），y = 左右偏移（度）
    // 它只加在最终视角上、每帧朝 0 衰减 —— 所以会自然回到你原本的瞄准点，瞄准基准永不破坏
    private Vector2 recoilOffset;
    private float recoilRecoverSpeed = 10f;   // 恢复速度（度/秒），开火时会被武器数据覆盖

    void Awake()
    {
        health = GetComponent<Health>();
        weapons = GetComponent<WeaponController>();
    }

    void OnEnable()   // 复活时把视角角度复位
    {
        if (health != null) health.OnRespawned += ResetLook;
        if (weapons != null) weapons.OnFired += HandleFired;   // 【新增，1.7d】开火 → 加后坐力
    }

    void OnDisable()
    {
        if (health != null) health.OnRespawned -= ResetLook;   // 配对取消订阅
        if (weapons != null) weapons.OnFired -= HandleFired;

        // 解锁鼠标（原样保留）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 复活后视角回到水平：不写的话会保持死亡瞬间低着/抬着头
    void ResetLook()
    {
        xRotation = 0f;
        recoilOffset = Vector2.zero;   // 【1.7d】后坐力偏移也清零
        if (cam != null) cam.transform.localRotation = Quaternion.identity;
    }

    // 【阶段2.1】网络生成时分支：远程玩家（别人）身上的相机整个关掉
    // HUD Canvas / 枪模 / AudioListener 都挂在相机下面，会一起被关掉 —— 各端只保留自己的第一人称视角
    public override void OnStartClient()
    {
        if (NetUtil.IsLocalControl(this)) return;   // 自己的玩家：什么都不动
        if (cam != null) cam.gameObject.SetActive(false);
    }

    void Start()
    {
        // 锁鼠标（原样保留）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (cam != null)
            cam.nearClipPlane = 0.01f; // 防止贴墙时看到墙内部（透视）
        // 把存档里的鼠标灵敏度盖到自己的字段上（没存过档就是默认 200）
        mouseSensitivity = SettingsManager.MouseSensitivity;
    }

    void Update()
    {
        if (!NetUtil.IsLocalControl(this)) return;     // 【阶段2.1】远程玩家不响应本机鼠标
        if (health != null && health.IsDead) return;   // 死亡时不能转视角
        if (cam == null) return;                       // 没拖相机时安全退出

        // 1. 【1.7d】开镜时降低灵敏度（狙击开镜后才瞄得稳）
        float sensScale = 1f;
        if (weapons != null && weapons.IsAiming && weapons.CurrentWeapon != null)
            sensScale = weapons.CurrentWeapon.aimSensitivityScale;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * sensScale * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * sensScale * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);

        // 2. 【1.7d】后坐力偏移随时间恢复（往 0 走）
        recoilOffset = Vector2.MoveTowards(recoilOffset, Vector2.zero, recoilRecoverSpeed * Time.deltaTime);

        // 3. 最终视角 = 鼠标角度 + 后坐力偏移（注意：不写回 xRotation，瞄准基准不被污染）
        cam.transform.localRotation = Quaternion.Euler(xRotation + recoilOffset.x, recoilOffset.y, 0f);
        transform.Rotate(Vector3.up * mouseX);   // 玩家的水平转身（旋转玩家的Y轴）
    }

    // 【新增，1.7d】开火时加镜头后坐力：垂直固定上跳 + 水平随机
    void HandleFired()
    {
        if (weapons == null || weapons.CurrentWeapon == null) return;
        WeaponData w = weapons.CurrentWeapon;
        bool aiming = weapons.IsAiming;

        // 【改，1.7d】腰射 / 开镜 各用一组独立参数（在 WeaponData 资产里分开调手感）
        float pitch = aiming ? w.aimRecoilPitch : w.recoilPitch;             // 垂直上跳（度）
        float yawRange = aiming ? w.aimRecoilYawRandom : w.recoilYawRandom;  // 水平随机范围（度）

        recoilOffset.x -= pitch;                 // 负值 = 抬头（和"鼠标上移"同方向）
        recoilOffset.y += Random.Range(-yawRange, yawRange);   // 左右随机
        recoilRecoverSpeed = w.recoilRecover;    // 恢复速度（腰射/开镜共用）
    }
}