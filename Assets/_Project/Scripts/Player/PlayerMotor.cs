using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [Header("移动参数")]
    public float moveSpeed = 5f;
    public float sprintSpeed = 9f; // 冲刺速度
    public float gravity = -18f;
    public float jumpHeight = 1.2f;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private bool isSprint = false; // 冲刺开关
    private Vector3 airMoveVelocity; // 保存空中滑行的速度


    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        CheckGround();
        SprintToggle(); // 检测Shift切换冲刺
        MovePlayer();
        ApplyGravity();
    }

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
            // 不按任何键：airMoveVelocity保持不变，继续滑行
            controller.Move(airMoveVelocity * Time.deltaTime);
        }
    }



    // 重力 + 跳跃
    void ApplyGravity()
    {
        // 空格跳跃
        if (Input.GetButtonDown("Jump") && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
