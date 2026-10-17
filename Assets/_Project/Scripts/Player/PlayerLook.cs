using UnityEngine;
using Mirror;   // 【阶段2.1】NetworkBehaviour

// 第一人称视角：鼠标转视角 + 开镜灵敏度缩放 + 【1.7d】镜头后坐力
public class PlayerLook : NetworkBehaviour
{
    [Header("视角参数")]
    public Camera cam;
    public float mouseSensitivity = 200f;
    public float maxLookAngle = 85f;   // 上下视角上限（抬头低头幅度）
    [Tooltip("后坐力累计上限（度）：连发时视角最多被后坐力顶起这么多，到顶停住。" +
             "保证玩家永远拉得回来（压枪不会失效），也保证视角不会越过正上方翻转")]
    public float maxRecoilOffset = 60f;   // 【修复】后坐力偏移限位

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
        if (GameplaySettingsUI.IsOpen) return;         // 【新功能】设置面板打开时不转视角
        if (health != null && health.IsDead) return;   // 死亡时不能转视角
        if (cam == null) return;                       // 没拖相机时安全退出

        // 1. 【新功能】三档灵敏度（实时读存档，设置里改完立刻生效）：
        //    腰射 = 全局灵敏度；步枪开镜 = 步枪开镜灵敏度；狙击开镜 = 狙击枪开镜灵敏度
        float sens = SettingsManager.MouseSensitivity;
        if (weapons != null && weapons.IsAiming && weapons.CurrentWeapon != null)
        {
            sens = (weapons.CurrentWeapon.weaponType == WeaponType.Sniper)
                ? SettingsManager.SniperScopeSensitivity
                : SettingsManager.RifleScopeSensitivity;
        }

        float mouseX = Input.GetAxis("Mouse X") * sens * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * sens * Time.deltaTime;

        // 【修复】鼠标"反向拉动"先把后坐力偏移抵消掉，多余的才作用到基础视角上 —— 压枪真实生效：
        // 视角拉到最低点开枪后，鼠标往下拉能立刻把被后坐力顶高的部分拉回来。
        // （旧逻辑鼠标只能改 xRotation，它顶到上下限后拉不动了 → 后坐力抬到哪、最低视角就被限制到哪，
        //   只能干等自动恢复；这也是"鼠标控制不了往下"的原因）
        float pitchInput = -mouseY;                             // 正 = 往下看；单位与 xRotation 一致（度）
        ConsumeRecoil(ref recoilOffset.x, ref pitchInput);      // 往下拉 → 先抵消"往上"的偏移
        ConsumeRecoil(ref recoilOffset.y, ref mouseX);          // 左右反向 → 先抵消水平偏移

        xRotation += pitchInput;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);

        // 2. 【1.7d】后坐力偏移随时间恢复（往 0 走）
        recoilOffset = Vector2.MoveTowards(recoilOffset, Vector2.zero, recoilRecoverSpeed * Time.deltaTime);

        // 3. 最终视角 = 鼠标角度 + 后坐力偏移（注意：不写回 xRotation，瞄准基准不被污染）
        // 【修复】这里再夹一次上下限：保证"鼠标角 + 后坐力"的最终值永远不超过 ±maxLookAngle，
        // 视角不可能越过正上方/正下方（此前没有任何夹紧，后坐力累加多了会翻转看到身后）
        float finalPitch = Mathf.Clamp(xRotation + recoilOffset.x, -maxLookAngle, maxLookAngle);
        cam.transform.localRotation = Quaternion.Euler(finalPitch, recoilOffset.y, 0f);
        transform.Rotate(Vector3.up * mouseX);   // 玩家的水平转身（旋转玩家的Y轴）
    }

    // 【修复】用鼠标输入抵消后坐力偏移：只有"方向相反"才抵消（最多把偏移抵到 0，抵不完的差额留给视角转动）
    //   例：视角被后坐力顶高了 2°（offset = -2），鼠标往下拉 3° → 抵消 2°，剩下 1° 才真正往下转视角；
    //       视角被顶高时继续往上推鼠标（同向）不抵消，避免"边抬边吃"的怪手感。
    // 抵消和"自动恢复"的区别：抵消是玩家主动把后坐力拽回来（不产生多余位移，压枪真实有效），
    // 自动恢复是没人管时自己慢慢回落（recoilRecover）；两者同时存在、互不冲突。
    private static void ConsumeRecoil(ref float offset, ref float input)
    {
        if (offset == 0f || input == 0f) return;
        if (Mathf.Sign(offset) == Mathf.Sign(input)) return;      // 同向：不抵消

        float eat = Mathf.Min(Mathf.Abs(offset), Mathf.Abs(input));   // 最多抵到 0
        offset += (offset > 0f) ? -eat : eat;                         // 偏移朝 0 走
        input += (input > 0f) ? -eat : eat;                           // 输入扣掉已抵消的部分
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

        // 【修复】连发越久，偏移累加越大（recoilRecover = 0 时几乎不回落）→ 必须限位：
        // 原来没有限位，偏移能涨到 -200° 以上 —— 鼠标拉满 +85° 也拉不回来（压不了枪），
        // 再大就直接越过 90° 让视角翻转。限位后最多顶到 maxRecoilOffset，永远在能拉回的范围内
        recoilOffset.x = Mathf.Max(recoilOffset.x, -maxRecoilOffset);
        recoilOffset.y = Mathf.Clamp(recoilOffset.y, -maxRecoilOffset, maxRecoilOffset);

        recoilRecoverSpeed = w.recoilRecover;    // 恢复速度（腰射/开镜共用）
    }
}