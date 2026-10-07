using UnityEngine;
using Mirror;   // 【阶段2.1】NetworkBehaviour

[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : NetworkBehaviour
{
    [Header("移动参数")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 9f; // 冲刺速度
    public float gravity = -18f;
    public float jumpHeight = 1.2f;



    [Header("动画（没有 Animator 也不会报错）")]
    [SerializeField] private Animator animator;               // 【新增】留空 = Awake 自动找同物体上的 Animator
    [SerializeField] private string speedParameter = "Speed"; // 【新增】参数名，要和 Animator Controller 里一致
    [SerializeField] private float animDamp = 10f;            // 【新增】速度平滑，避免动作抽搐
    // 【新增，1.7b】跳跃动画参数名（要和 Animator Controller 里的参数名对得上）
    [SerializeField] private string groundedParameter = "isGrounded";   // 是否在地面
    [SerializeField] private string jumpingParameter = "jumping";       // 是否处于"跳跃中"

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private bool isSprint = false; // 冲刺开关
    private Vector3 airMoveVelocity; // 保存空中滑行的速度
    private bool wasGroundedLastFrame;   // 【新增，1.7b】上一帧是否在地面（用来检测"落地那一瞬间"）
    private Vector3 prevPos;   // 【阶段2.1修复】上一帧末尾的位置（远端位置由 NetworkTransform 驱动，帧内取不到位移）



    private Health health; // 自己的血量组件，用来判断死亡

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>(); // 取不到也不影响（下面都判空了）
        if (animator == null) animator = GetComponentInChildren<Animator>();   // 改成 InChildren，往下找
        prevPos = transform.position;   // 初始化，避免第一帧出现巨大位移
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

        if (localControl)
        {
            CheckGround();
            SprintToggle();
            MovePlayer();
            ApplyGravity();
        }
        else if (animator != null)
        {
            // 【阶段2.1】远程玩家：不跑物理，给动画补一个"在地面"状态，
            // 避免 Animator 卡在滞空动画里（跳跃动画的完整同步留到后续打磨）
            animator.SetBool(groundedParameter, true);
        }

        // 用"本帧实际位移 ÷ 帧时长"算水平速度（比 CharacterController.velocity 可靠）
        Vector3 delta = transform.position - posBefore;
        Vector3 planarDelta = new Vector3(delta.x, 0f, delta.z);       // 只取水平位移
        float planarSpeed = planarDelta.magnitude / Time.deltaTime;    // 速度的"大小"（不分方向）

        // 【新增】后退判定：位移方向和自己面朝的方向相反 → 速度取负
        // 负值会去混合树左边的档位：-5 = 后退走，-9 = 后退跑
        if (Vector3.Dot(planarDelta, transform.forward) < 0f)
        {
            planarSpeed = -planarSpeed;
        }

        SetAnimatorSpeed(planarSpeed);
        // 【新增，1.7b】把地面/跳跃状态写给 Animator（驱动 起跳 → 滞空 → 落地 三段动画）
        // 【阶段2.1】跳跃状态只对本地玩家评估：远程玩家只保留走/跑动画（由上面的位移驱动）
        if (localControl) UpdateJumpAnimator();
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

    // 【新增】平滑地把速度写进 Animator 参数
    void SetAnimatorSpeed(float targetSpeed)
    {
        if (animator == null) return;
        float current = animator.GetFloat(speedParameter);
        animator.SetFloat(speedParameter, Mathf.Lerp(current, targetSpeed, animDamp * Time.deltaTime));
        // 【新增，1.7b】复活时复位跳跃状态，防止"跳跃中"卡死在动画里
        if (animator != null) animator.SetBool(jumpingParameter, false);
        wasGroundedLastFrame = false;
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
            animator.SetBool(jumpingParameter, false);
        }

        // 3. 记下这一帧的地面状态，供下一帧对比
        wasGroundedLastFrame = isGrounded;
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
            float currentSpeed = isSprint ? sprintSpeed : moveSpeed;
            controller.Move(inputDir * currentSpeed * Time.deltaTime);
        }
        else
        {
            // 在空中：如果现在还按着方向，更新滑行速度；不按键就保持旧速度继续滑
            if (inputDir.magnitude > 0.01f)
            {
                float currentSpeed = isSprint ? sprintSpeed : moveSpeed;
                airMoveVelocity = inputDir * currentSpeed;
            }
            controller.Move(airMoveVelocity * Time.deltaTime);
        }
    }

    // 重力 + 跳跃
    void ApplyGravity()
    {
        if (Input.GetKey(KeyCode.Space) && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            // 【新增，1.7b】标记"进入跳跃中"，Animator 会切到起跳动画
            if (animator != null) animator.SetBool(jumpingParameter, true);
        }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}