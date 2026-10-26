﻿﻿﻿using UnityEngine;
using Mirror;   // 【阶段2.1】NetworkBehaviour

[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : NetworkBehaviour
{
    [Header("移动参数")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 9f; // 冲刺速度
    public float gravity = -18f;
    public float jumpHeight = 1.4f;

    [Header("开火减速")]
    [Tooltip("正在开火时的移动速度倍率（0.5 = 慢一半）。1 = 不减速")]
    [SerializeField] private float firingSpeedScale = 0.5f;



    [Header("动画（没有 Animator 也不会报错）")]
    [SerializeField] private Animator animator;               // 【新增】留空 = Awake 自动找同物体上的 Animator
    [SerializeField] private string speedParameter = "Speed"; // 【新增】参数名，要和 Animator Controller 里一致
    [SerializeField] private string moveXParameter = "MoveX"; // 2D 混合树：本地右方向（-1 ~ 1）
    [SerializeField] private string moveYParameter = "MoveY"; // 2D 混合树：本地前方向（-1 ~ 1）
    [SerializeField] private float animDamp = 10f;            // 【新增】速度平滑，避免动作抽搐
    // 【新增，1.7b】跳跃动画参数名（要和 Animator Controller 里的参数名对得上）
    [SerializeField] private string groundedParameter = "isGrounded";   // 是否在地面
    [SerializeField] private string jumpingParameter = "jumping";       // 是否处于"跳跃中"

    [Header("蹲下")]
    [SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;   // 【新增】按住蹲下、松开站起（可在 Inspector 换键）
    [SerializeField] private float crouchHeight = 1.2f;                 // 【新增】蹲下时的胶囊高度（站立高度自动读 CharacterController）
    [SerializeField] private float crouchSpeedScale = 0.5f;             // 【新增】蹲下移动速度倍率（相对走路速度；蹲下不能冲刺）
    [SerializeField] private float crouchLerpSpeed = 4f;                // 【新增】蹲下/站起的高度与相机过渡速度（越大越干脆）
    [SerializeField] private string crouchParameter = "isCrouched";     // 【新增】蹲下参数名（要和 playerA.controller 里的参数名一致）
    [SerializeField] private Transform camT;                            // 【新增】第一人称相机（留空 = 自动找子物体的 Camera；蹲下时压低视角）

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private bool isSprint = false; // 冲刺开关
    private Vector3 airMoveVelocity; // 保存空中滑行的速度
    private bool wasGroundedLastFrame;   // 【新增，1.7b】上一帧是否在地面（用来检测"落地那一瞬间"）
    private Vector3 prevPos;   // 【阶段2.1修复】上一帧末尾的位置（远端位置由 NetworkTransform 驱动，帧内取不到位移）



    private Health health; // 自己的血量组件，用来判断死亡

    // ---------- 【新增】蹲下 ----------
    [SyncVar(hook = nameof(HookNetCrouchedChanged))] private bool netCrouched;  // 服务器记录（同步给所有人：远端玩家也要看到你蹲着）
    private bool crouched;          // 本地当前蹲下状态（本地输入立即生效；远端玩家由 hook 写入）
    private float standHeight;      // 站立胶囊高度（Awake 读取）
    private Vector3 standCenter;    // 站立胶囊中心（Awake 读取）
    private float standCamLocalY;   // 站立时相机局部 Y（Awake 读取）

    // ---------- 【新增】跳跃状态同步（远端玩家的跳跃动画） ----------
    [SyncVar] private bool netGrounded = true;   // 服务器记录：是否在地面（远端动画用；默认在地面）
    [SyncVar] private bool netJumping;           // 服务器记录：是否跳跃中（远端动画用）
    private bool jumpingNow;                     // 本地"跳跃中"状态（本地动画 + 上报同步用）
    private bool lastSentGrounded = true;        // 上次上报的地面状态（防止等待回显期间每帧重复上报）
    private bool lastSentJumping;

    // 【新增，4-D5】开火减速：开火时移动变慢。引用取不到 = Bot（没有 WeaponController）→ 不减速
    private WeaponController weapon;

    // 【新增】对外只读：是不是蹲着（WeaponController 用它算"蹲下收紧散布"）
    public bool IsCrouched => crouched;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>(); // 取不到也不影响（下面都判空了）
        weapon = GetComponent<WeaponController>();   // 【新增，4-D5】Bot 没有 → 取不到，不减速
        if (animator == null) animator = GetComponentInChildren<Animator>();   // 改成 InChildren，往下找
        prevPos = transform.position;   // 初始化，避免第一帧出现巨大位移

        // 【新增】记住站立基准：胶囊高度/中心 + 相机局部 Y（蹲下过渡都从它们出发）
        standHeight = controller.height;
        standCenter = controller.center;
        if (camT == null)
        {
            Camera c = GetComponentInChildren<Camera>();   // 本地玩家的第一人称相机（远端玩家的会被 PlayerLook 关掉，取不到也不影响）
            if (c != null) camT = c.transform;
        }
        if (camT != null) standCamLocalY = camT.localPosition.y;
    }

    void OnEnable()   // 订阅复活事件
    {
        if (health != null) health.OnRespawned += ResetVelocity;
    }

    void OnDisable()  // 配对取消订阅
    {
        if (health != null) health.OnRespawned -= ResetVelocity;
    }

    // 复活时清零速度
    void ResetVelocity()
    {
        velocity = Vector3.zero;
        airMoveVelocity = Vector3.zero;
        SetAnimatorSpeed(0f);   // 【新增】复活瞬间让动画回到 Idle
        if (animator != null) animator.SetBool(jumpingParameter, false);   // 【修复】复活复位跳跃（原来误写在每帧路径里，会导致起跳动画播不出来）
        jumpingNow = false;
        wasGroundedLastFrame = false;

        // 【新增】复活回到站立：本地表现立即复位；服务器端把 SyncVar 一起复位（客户端等同步）
        crouched = false;
        if (NetworkServer.active) netCrouched = false;
    }

    void Update()
    {
        if (health != null && health.IsDead)
        {
            SetAnimatorSpeed(0f); // 死了让动画回 Idle
            return;
        }

        Vector3 posBefore = prevPos;   // 【阶段2.1修复】改为"上一帧末尾位置"：远端才能测到跨帧位移

        // 【阶段2.1】联机时只有"自己的玩家对象"能响应键鼠、跑物理；
        // 远程玩家（别人）的位置由 NetworkTransform 同步，这里不再驱动它移动
        bool localControl = NetUtil.IsLocalControl(this);

        // 【新功能】设置面板（G）打开时：冻结移动输入（世界照常运行，不算暂停），
        // 但下面的动画衰减照旧跑 —— 停下来会自然回到 Idle
        if (localControl && !GameplaySettingsUI.IsOpen)
        {
            CheckGround();
            HandleCrouchInput();   // 【新增】蹲下输入（按住 Ctrl 蹲、松开站；头顶被挡时站不起来）
            SprintToggle();
            MovePlayer();
            ApplyGravity();
        }
        else if (animator != null)
        {
            // 【改】远端玩家：跳跃/落地状态改由服务器同步（不再写死"在地面"）——远端的跳跃动画也能播了
            animator.SetBool(groundedParameter, netGrounded);
            animator.SetBool(jumpingParameter, netJumping);
        }

        // 用"本帧实际位移 ÷ 帧时长"算水平速度（比 CharacterController.velocity 可靠）
        Vector3 delta = transform.position - posBefore;
        Vector3 planarDelta = new Vector3(delta.x, 0f, delta.z);       // 只取水平位移
        float planarSpeed = planarDelta.magnitude / Time.deltaTime;    // 速度的"大小"（不分方向）

        UpdateMoveAnimator(planarDelta, planarSpeed);
        // 【新增，1.7b】把地面/跳跃状态写给 Animator（驱动 起跳 → 滞空 → 落地 三段动画）
        // 【改】跳跃状态本机评估并同步给服务器：远端玩家也能看到跳跃/落地动画
        if (localControl)
        {
            UpdateJumpAnimator();
            SyncAirState();   // 【新增】跳跃/落地状态变化 → 上报服务器（远端玩家的跳跃动画）
        }
        UpdateCrouchPosture();   // 【新增】蹲下姿态过渡（胶囊高度+相机高度；远端玩家的蹲下也靠它表现）
        prevPos = transform.position;   // 【阶段2.1修复】记下本帧末尾位置，供下一帧算位移
    }

    // 【新增】读 CharacterController 的真实水平速度，驱动 Idle / 走 / 跑 混合树
    void UpdateAnimator()
    {
        if (animator == null) return;

        Vector3 v = controller.velocity;                          // 角色这一帧的真实速度
        float planarSpeed = new Vector3(v.x, 0f, v.z).magnitude;  // 只取水平方向（上下不算）
        SetAnimatorSpeed(planarSpeed);
    }

    // 把移动方向写进 Animator：
    //   MoveX = 本地右方向，MoveY = 本地前方向（带速度层级：走路≈0.55，跑步=1；蹲走=1）
    //   Speed 仍保留，兼容旧的 1D 混合树 / 其他逻辑。
    // 侧移时只靠 Speed 会因 dot(forward)≈0 来回翻符号 → 动画在前进/后退档之间跳；这里彻底拆开方向。
    void UpdateMoveAnimator(Vector3 planarDelta, float planarSpeed)
    {
        if (animator == null) return;

        float moveX = 0f;
        float moveY = 0f;
        float signedSpeed = 0f;

        if (planarDelta.sqrMagnitude > 0.000001f && planarSpeed > 0.05f)
        {
            Vector3 localDir = transform.InverseTransformDirection(planarDelta.normalized);
            float radialMax = crouched
                ? Mathf.Max(0.01f, moveSpeed * crouchSpeedScale)
                : Mathf.Max(0.01f, sprintSpeed);
            float tier = Mathf.Clamp01(planarSpeed / radialMax);

            moveX = localDir.x * tier;
            moveY = localDir.z * tier;

            // 旧 1D 混合树兼容：只有明显后退才取负；纯侧移保持正速度，避免 0 附近来回跳档
            float forwardDot = Vector3.Dot(planarDelta.normalized, transform.forward);
            signedSpeed = forwardDot < -0.35f ? -planarSpeed : planarSpeed;
        }

        SetAnimatorLocomotion(signedSpeed, moveX, moveY);
    }

    // 【新增】平滑地把速度写进 Animator 参数
    void SetAnimatorSpeed(float targetSpeed)
    {
        SetAnimatorLocomotion(targetSpeed, 0f, 0f);
    }

    void SetAnimatorLocomotion(float targetSpeed, float targetMoveX, float targetMoveY)
    {
        if (animator == null) return;

        float t = animDamp * Time.deltaTime;
        animator.SetFloat(speedParameter, Mathf.Lerp(animator.GetFloat(speedParameter), targetSpeed, t));
        animator.SetFloat(moveXParameter, Mathf.Lerp(animator.GetFloat(moveXParameter), targetMoveX, t));
        animator.SetFloat(moveYParameter, Mathf.Lerp(animator.GetFloat(moveYParameter), targetMoveY, t));
    }


    // 【新增，1.7b】驱动跳跃动画：把"是否在地面 / 是否跳跃中"写进 Animator
    // Animator 里的转移逻辑（在 playerA.controller 里配）：
    //   移动 →(jumping=true) 起跳Jump →(播一半) 滞空Fall →(isGrounded=true) 回移动
    //   没跳跃直接走出平台：isGrounded=false 且 jumping=false → 也会进滞空（AnyState 转移）
    void UpdateJumpAnimator()
    {
        if (animator == null) return;

        // 1. 每帧同步"是否在地面"（滞空 → 落地 的切换条件）
        animator.SetBool(groundedParameter, isGrounded);

        // 2. 落地那一瞬间（上一帧还在空中、这一帧踩到地面）→ 退出"跳跃中"
        //    特意用"边缘检测"而不是直接判断 isGrounded：
        //    因为起跳后 isGrounded 会延迟一两帧才变 false，
        //    直接判断会把刚设置的"跳跃中"立刻误清掉
        if (isGrounded && !wasGroundedLastFrame)
        {
            jumpingNow = false;
            animator.SetBool(jumpingParameter, false);
        }

        // 3. 记下这一帧的地面状态，供下一帧对比
        wasGroundedLastFrame = isGrounded;
    }

    // ================= 【新增】蹲下 =================

    // 蹲下输入（只有本地玩家跑）：按住 Ctrl 蹲下、松开站起
    // 联机：本地立即生效（表现不等网络），同时上报服务器记账 → 同步给其他人
    void HandleCrouchInput()
    {
        bool want = Input.GetKey(crouchKey);
        if (want == crouched) return;                 // 状态没变：什么都不做

        if (!want && CeilingBlocked()) return;        // 想站起来但头顶被挡 → 保持蹲着（防止穿模）

        crouched = want;                              // 本地立即生效
        if (NetworkServer.active) netCrouched = want;         // 主机/服务器：直接记账（权威侧不会回调 hook，本地上面已应用）
        else if (NetworkClient.active) CmdSetCrouch(want);    // 客机：上报（服务器记账后广播给所有端）
    }

    [Command]
    private void CmdSetCrouch(bool value)
    {
        netCrouched = value;   // 服务器记账（SyncVar → 各端 hook）
    }

    // 远端玩家的蹲下状态：服务器同步过来 → 写入本地字段，后续由 UpdateCrouchPosture 统一表现
    private void HookNetCrouchedChanged(bool oldValue, bool newValue)
    {
        if (NetUtil.IsLocalControl(this)) return;   // 自己那端：本地输入已经先应用了，不覆盖
        crouched = newValue;
    }

    // 站起来前的头顶检查：从"蹲姿胶囊上沿"往上打球形检测，被挡就返回 true
    bool CeilingBlocked()
    {
        Bounds b = controller.bounds;
        Vector3 start = new Vector3(b.center.x, b.max.y - controller.radius, b.center.z);
        float dist = standHeight - crouchHeight + 0.02f;
        RaycastHit[] hits = Physics.SphereCastAll(start, controller.radius * 0.95f, Vector3.up, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;   // 自己身上的东西不算
            return true;
        }
        return false;
    }

    // 蹲下姿态过渡（所有人跑：本地玩家 + 远端玩家都靠它表现蹲下）
    // ① 胶囊高度/中心：脚底不动，只降上半身 ② 相机跟着身高同步下降 ③ 写 Animator 的 isCrouched
    void UpdateCrouchPosture()
    {
        float targetHeight = crouched ? crouchHeight : standHeight;
        float newHeight = Mathf.MoveTowards(controller.height, targetHeight, crouchLerpSpeed * Time.deltaTime);
        controller.height = newHeight;
        controller.center = new Vector3(standCenter.x, standCenter.y - (standHeight - newHeight) * 0.5f, standCenter.z);

        if (camT != null)
        {
            Vector3 p = camT.localPosition;
            float targetCamY = standCamLocalY - (standHeight - newHeight);   // 相机和身高同步下降（不会出现"人蹲了视角没蹲"）
            camT.localPosition = new Vector3(p.x, Mathf.MoveTowards(p.y, targetCamY, crouchLerpSpeed * Time.deltaTime), p.z);
        }

        if (animator != null) animator.SetBool(crouchParameter, crouched);
    }

    // 【新增】当前移动速度：蹲下时用走路速度×倍率（蹲下不能冲刺）
    // 【4-D5】再加"开火减速"：正在开火时整体再乘 firingSpeedScale（蹲下 / 冲刺状态都会一起变慢）
    float CrouchAwareSpeed()
    {
        float speed = crouched ? moveSpeed * crouchSpeedScale : (isSprint ? sprintSpeed : moveSpeed);
        if (weapon != null && weapon.IsFiring) speed *= firingSpeedScale;
        return speed;
    }

    // ================= 【新增】跳跃状态同步（远端玩家的跳跃动画） =================

    // 本地跳跃/落地状态变化 → 上报服务器（SyncVar 广播）；远端在 Update 的 else 分支读取播放
    void SyncAirState()
    {
        if (lastSentGrounded == isGrounded && lastSentJumping == jumpingNow) return;   // 没变化：不重复上报
        lastSentGrounded = isGrounded;
        lastSentJumping = jumpingNow;

        if (NetworkServer.active)       // 主机/服务器：直接记账
        {
            netGrounded = isGrounded;
            netJumping = jumpingNow;
        }
        else if (NetworkClient.active)  // 客机：上报
        {
            CmdSetAirState(isGrounded, jumpingNow);
        }
    }

    [Command]
    private void CmdSetAirState(bool grounded, bool jumping)
    {
        netGrounded = grounded;
        netJumping = jumping;
    }

    // ↓↓↓ 以下四个方法一字未改 ↓↓↓

    // Shift切换冲刺状态：按住冲刺，松开恢复正常速度
    void SprintToggle()
    {
        isSprint = Input.GetKey(KeyCode.LeftShift);
    }

    // 检测是否在地面
    void CheckGround()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }
    }

    // WASD移动逻辑
    void MovePlayer()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = transform.right * horizontal + transform.forward * vertical;
        inputDir.Normalize();

        if (isGrounded)
        {
            // 在地面：直接使用输入，松手立刻停，重置空中速度
            airMoveVelocity = Vector3.zero;
            float currentSpeed = CrouchAwareSpeed();
            controller.Move(inputDir * currentSpeed * Time.deltaTime);
        }
        else
        {
            // 在空中：如果现在还按着方向，更新滑行速度；不按键就保持旧速度继续滑
            if (inputDir.magnitude > 0.01f)
            {
                float currentSpeed = CrouchAwareSpeed();
                airMoveVelocity = inputDir * currentSpeed;
            }
            controller.Move(airMoveVelocity * Time.deltaTime);
        }
    }

    // 重力 + 跳跃
    void ApplyGravity()
    {
        if (Input.GetKey(KeyCode.Space) && isGrounded && !crouched)   // 【新增】蹲下时不能跳（先站起来）
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            // 【新增，1.7b】标记"进入跳跃中"，Animator 会切到起跳动画
            jumpingNow = true;
            if (animator != null) animator.SetBool(jumpingParameter, true);
        }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}