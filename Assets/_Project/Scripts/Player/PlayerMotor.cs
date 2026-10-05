using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
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

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private bool isSprint = false; // 冲刺开关
    private Vector3 airMoveVelocity; // 保存空中滑行的速度

    private Health health; // 自己的血量组件，用来判断死亡

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>(); // 取不到也不影响（下面都判空了）
        if (animator == null) animator = GetComponentInChildren<Animator>();   // 改成 InChildren，往下找
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

        Vector3 posBefore = transform.position;   // 【新增】记录移动前的位置

        CheckGround();
        SprintToggle();
        MovePlayer();
        ApplyGravity();

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
        // 空格跳跃
        if (Input.GetKey(KeyCode.Space) && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}