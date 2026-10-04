using System;
using UnityEngine;

[RequireComponent(typeof(Animator),typeof(CharacterController))]
public class CharacterBase : MonoBehaviour
{
    public CharacterController controller { get; private set; }
    public  Animator animator { get; private set; }
    //重力的配置
    [Header("重力设置")]
    [SerializeField] public float gravity = -12;
    [SerializeField] public Vector2 velocityLimit = new Vector2(-20, 60);
    [SerializeField] public LayerMask whatIsGround;
    [SerializeField] private float groundDetectedOffset = -0.06f;
    [SerializeField] private float groundRadius = 1.2f;
    private Vector3 detectedOrigin;
    public BindableProperty<bool> isOnGround { set; get; } = new BindableProperty<bool>();
    //角色垂直速度
    public float verticalSpeed { get; set; }
    private Vector3 verticalVelocity;
    //角色的水平速度:不包含动画位移
    private Vector3 horizontalVelocityInAir;
    private Vector3 animationVelocity;
    public Vector3 AnimationVelocity => animationVelocity;
    //角色的运动
    private Vector3 moveDir;
    public Vector3 animatorDeltaPositionOffset{ get; set; }
    public bool applyFullRootMotion { get; set; } = false;
    [SerializeField,Range(0.1f,10)] public float moveSpeedMult =1;
    public bool disEnableRootMotion { get; set; }//不采用任何根运动信息，禁用OnAnimatorMove方法
    public bool ignoreRootMotionY { get; set; } = false;//忽视根运动的Y量
    public bool disEnableGravity { get; set; } = false;//是否禁用程序重力
    public bool ignoreRotationRootMotion { get; set; } = false;//是否忽略根运动的转向
    protected virtual void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
    }
    protected virtual void Update()
    {
        CheckOnGround();
        CharacterGravity();
        CharacterVerticalVelocity();
        ResetHorizontalVelocity();
    }

  

    #region 重力的处理
    /// <summary>
    /// 地面检测
    /// </summary>
    private bool CheckOnGround()
    {
        detectedOrigin = transform.position - groundDetectedOffset * Vector3.up;
        var isHit = Physics.CheckSphere(detectedOrigin, groundRadius, whatIsGround, QueryTriggerInteraction.Ignore);
        isOnGround.Value = isHit && verticalSpeed < 0;
        return isOnGround.Value;
    }
    private void CharacterGravity()
    {
        if (disEnableGravity)
        {
            return;
        }
        if (isOnGround.Value)
        {
            verticalSpeed = -2;
        }
        else
        {
            verticalSpeed += Time.deltaTime * gravity;
            verticalSpeed = Mathf.Clamp(verticalSpeed, velocityLimit.x, velocityLimit.y);
        }
        verticalVelocity = new Vector3(0, verticalSpeed, 0);
    }

    #endregion

    #region 玩家移动
    private void ResetHorizontalVelocity()
    {
        if (isOnGround.Value)
        {
            if (horizontalVelocityInAir!= Vector3.zero)
            {
                horizontalVelocityInAir = Vector3.zero;
            }
        }
    }
    //程序上控制重力速度和水平速度
    private void CharacterVerticalVelocity()
    {
        if (disEnableGravity)
        {
            verticalVelocity = Vector3.zero;
        }
        if (controller.enabled)
        {
            controller.Move((verticalVelocity + horizontalVelocityInAir) * Time.deltaTime);
        }

    }
    protected virtual void OnAnimatorMove()//在播放动画时调用次方法,没有动画不会执行
    {
        if (disEnableRootMotion)
        {
            return;
        }

        if (applyFullRootMotion) //开启角色的根运动，重力默认为角色自带的向下的动画位移量
        {
            animator.ApplyBuiltinRootMotion();
        }
        else//不启用根运动，但是采样的也是角色根运动信息(位移)
        {
            Vector3 animationMovement = animator.deltaPosition+ animatorDeltaPositionOffset;
            if (ignoreRootMotionY)
            {
                animationMovement.y = 0;
            }
            moveDir = SetDirOnSlop(animationMovement) * moveSpeedMult;
            UpdateCharacterMove(moveDir,animator.deltaRotation);
        }
    }
    public void UpdateCharacterMove(Vector3 deltaDir,Quaternion deltaRotation)
    {
        if (!ignoreRotationRootMotion)
        {
            if (deltaRotation != Quaternion.identity)
            {
                transform.rotation = deltaRotation * transform.rotation;
            }
        }
        //每帧移动Dir个单位
        if (controller.enabled == true)
        {
            animationVelocity = deltaDir;
            controller.Move(deltaDir);
        }
      
    }
    public float ChangeVerticalSpeed(float verticalSpeed)
    {
        return this.verticalSpeed = verticalSpeed;
    }
    public void AddHorizontalVelocityInAir(Vector3 vector3)
    {
        horizontalVelocityInAir = new Vector3(vector3.x,0, vector3.z);
    }
    public void ClearHorizontalVelocity()
    {
        horizontalVelocityInAir = Vector3.zero;
    }

    #endregion

    #region 斜坡的处理
    private Vector3 SetDirOnSlop(Vector3 dir)
    {
        if (Physics.Raycast(transform.position, Vector3.down, out var hitInfo, 1))
        {
            if (Vector3.Dot(hitInfo.normal, Vector3.up) != 1)
            {
                return Vector3.ProjectOnPlane(dir, hitInfo.normal);
            }
        }
        return dir;
    }
    #endregion

    private void OnDrawGizmos()
    {
        if (CheckOnGround())
        {
            Gizmos.color = Color.green;
        }
        else
        {
            Gizmos.color = Color.red;
        }

        Gizmos.DrawWireSphere(transform.position - groundDetectedOffset * Vector3.up, groundRadius);
    }
}