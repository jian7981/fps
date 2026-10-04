using Animancer;
using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerLedgeClimbState : PlayerMovementState
{
    private int drawID;
    PlayerHangWallData HangWallData;
    BindableProperty<bool> isHandingRotation = new BindableProperty<bool>();
    Action handRotaionTask;
    Action climbUpTask;

    Vector3 startDetectionPos;

    bool isInitMatchTargeting;
    bool isClimbUp;
    bool isClimbUpCancel;
    bool isHangOut;//离开攀爬
  
    float handHight;
    float detectionOffset = -0.02f;
    float CCRadiusMult = 0.5f;

    Vector3 targetPoint;
    float maxError = 0.08f;//高度最大误差值

    CapsuleCollider capsuleCollider;
    Rigidbody rigidbody;
    public PlayerLedgeClimbState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        HangWallData = playerSO.playerMovementData.PlayerHangWallData;
    }

    public override void OnEnter()
    {
        //禁用重力和CC
        player.controller.enabled = false;
        player.applyFullRootMotion = true;
        player.disEnableGravity = true;
        //添加更小的碰撞体
        if (!player.transform.TryGetComponent(out capsuleCollider))
        {
            capsuleCollider = player.AddComponent<CapsuleCollider>();
            capsuleCollider.radius = player.controller.radius * CCRadiusMult;
            capsuleCollider.height = player.controller.height/2f ;
            capsuleCollider.center = player.controller.center;
        }
        if (!player.transform.TryGetComponent(out rigidbody))
        {
            rigidbody = player.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotationZ;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotationX;
        }
        capsuleCollider.enabled = true;
        rigidbody.isKinematic = false;

        //获取开始阶段的匹配设置
        isInitMatchTargeting = true;
        isHandingRotation.Value = false;
        isClimbUp =false;
        isHangOut =false;

        handHight = Mathf.Abs(HangWallData.hightAndForwardOffSet.x) + detectionOffset;
        targetPoint = reusableData.hit.point + Vector3.up * HangWallData.hightAndForwardOffSet.x+ reusableData.hit.normal * (HangWallData.hightAndForwardOffSet.y);
        base.OnEnter();
    }
    protected override void AddEventListening()
    {
        base.AddEventListening();
        inputServer.inputMap.Player.Move.started += OnMove;
        inputServer.inputMap.Player.Move.canceled += MoveEnd;
        inputServer.inputMap.Player.Jump.started += OnJump;
        isHandingRotation.ValueChanged += HandRotaion;
    }

  
    protected override void RemoveEventListening()
    {
        base.RemoveEventListening();
        inputServer.inputMap.Player.Move.started -= OnMove;
        inputServer.inputMap.Player.Move.canceled -= MoveEnd;
        inputServer.inputMap.Player.Jump.started -= OnJump;
        isHandingRotation.ValueChanged -= HandRotaion;

        reusableLogic.RemoveClimbTarget_Y_Task();
    }
    public override void OnExit()
    {
        base.OnExit();
        isHangOut = false;

        player.disEnableGravity = false;
        player.controller.enabled = true;
        player.applyFullRootMotion = false;

        capsuleCollider.enabled = false;
        //这里不得不将Rb移除，不然爬墙就会受到影响
        GameObject.Destroy(rigidbody);
    }
    private void HangWallStartEnd()
    {
        if (inputServer.Move == Vector2.zero)
        {
            animancer.Play(HangWallData.hang_wall_idle);
        }
    }

   
    private void OnMove(InputAction.CallbackContext context)
    {
      if (isInitMatchTargeting) { return; }
      if (isClimbUpCancel) { return; }
      if (isClimbUp) { return; }

      float angle = GetTargetAngle();

        if (angle < 45 && angle >= -45)
        {
            //TODO检测能不能爬上去
            isClimbUp = true;
           AnimancerState animancerState = animancer.Play(HangWallData.hang_wall_climb_up);
            ClimbTargetMatchInfo climbTargetMatchInfo = new ClimbTargetMatchInfo(reusableData.hit.point + Vector3.up * 0.25f);
            reusableLogic.SetClimbTarget_Y_Task(animancerState,ref climbTargetMatchInfo, 0.6f, 1);
            animancerState.Events(player).SetCallback(playerSO.playerParameterData.moveInterruptEvent, OnInputInterruption);
            animancerState.Events(player).OnEnd = OnStateDefaultEnd;
            climbUpTask = () =>
            {
                if (animancerState.NormalizedTime > 0.6f)
                {
                    climbUpTask = null;                   
                }
                if (inputServer.Move == Vector2.zero)
                {
                    animancer.Play(HangWallData.hang_wall_idle_inertia_01);
                    //计算下落的目标位置
                    targetPoint = reusableData.hit.point + Vector3.up * HangWallData.hightAndForwardOffSet.x + reusableData.hit.normal * (HangWallData.hightAndForwardOffSet.y);
                    reusableLogic.RemoveAnimationMotionCompensationTask();

                    climbUpTask = null;
                    isClimbUp = false;
                    isClimbUpCancel = true;
                }
            };
        }
        else if (angle < 135 && angle >= 45)
        {
            //向右手边走
            animancer.Play(HangWallData.hand_wallMove_Mixer);
            reusableData.rotationValueParameter.TargetValue = 90 * Mathf.Deg2Rad;
        }
        else if (angle < -45 && angle >= -135)
        {
            //向左手边走
            animancer.Play(HangWallData.hand_wallMove_Mixer);
            reusableData.rotationValueParameter.TargetValue = -90 * Mathf.Deg2Rad;
        }
        else
        {
            Debug.Log("看后面");
        }
    }


    private void OnJump(InputAction.CallbackContext context)
    {
        float angle = GetTargetAngle();
        if (inputServer.Move == Vector2.zero)
        {
            HangJumpOut();
            reusableData.currentInertialVelocity = Vector3.zero;
            animancer.Play(HangWallData.hang_wall_idle_jump_out_up).Events(player).OnEnd = OnJumpFall;

        }
        else if (angle < -45 && angle >= -135)
        {
            HangJumpOut();
            reusableData.currentInertialVelocity = 1.5f * GetTargetDir().normalized*Time.deltaTime;
            animancer.Play(HangWallData.hang_wall_idle_jump_out_left).Events(player).OnEnd = OnJumpFall;
        }
        else if (angle < 135 && angle >= 45)
        {
            HangJumpOut();
            reusableData.currentInertialVelocity = 1.5f * GetTargetDir().normalized * Time.deltaTime;
            animancer.Play(HangWallData.hang_wall_idle_jump_out_right).Events(player).OnEnd = OnJumpFall;
        }

    }

    private void HangJumpOut()
    {
        isHangOut = true;
        player.controller.enabled = true;
        player.applyFullRootMotion = false;
        player.disEnableGravity = true;
        rigidbody.isKinematic = true;
        
        RemoveEventListening();
    }

    private void OnJumpFall()
    {
        player.verticalSpeed = -2;
        OnLandToFall();
    }

    private void MoveEnd(InputAction.CallbackContext context)
    {
        if (isClimbUp) { return; }
        if (isInitMatchTargeting) { return; }
        if (isClimbUpCancel) {  return; }

        if (reusableData.rotationValueParameter.CurrentValue >0)
        {
            animancer.Play(HangWallData.hang_wall_idle_right_inertia).Events(player).OnEnd = HangWallStartEnd;
        }
       else
        {
            animancer.Play(HangWallData.hang_wall_idle_left_inertia).Events(player).OnEnd = HangWallStartEnd;
        }
        reusableData.rotationValueParameter.TargetValue = 0;
    }

    
    public override void OnUpdate()
    {
        base.OnUpdate();
        climbUpTask?.Invoke();
        handRotaionTask?.Invoke();
        if (isClimbUp) { return; }
        if (isHangOut) { return; }

        //攀爬匹配

        if (isInitMatchTargeting )//抓上去匹配或者取消攀爬时的重新匹配
        {
            player.transform.position = Vector3.SmoothDamp(player.transform.position, targetPoint, ref matchSpeed, 0.05f, 200);
            if (Vector3.Distance(player.transform.position, targetPoint) < 0.02f)
            {
                animancer.Play(HangWallData.hang_wall_idle_frond).Events(player).OnEnd = HangWallStartEnd;
                isInitMatchTargeting = false;
            }
        }
        else if (isClimbUpCancel)//取消上爬下落时
        {
            player.transform.position = Vector3.SmoothDamp(player.transform.position, targetPoint, ref matchSpeed, 0.08f, 80);
            if (Vector3.Distance(player.transform.position, targetPoint) < 0.01f)
            {
                isClimbUpCancel = false;
            }

        }
        else//爬匹配
        {
            // 计算起始位置
            startDetectionPos = (player.transform.position + Vector3.up * handHight - player.transform.forward * 0.2f);
            // 发射射线检测地面
            if (Physics.Raycast(startDetectionPos, player.transform.forward, out var ray, 1.0f, player.whatIsGround) )
            {
                // 计算目标位置
                //再发射一个射线来向上修正角色Y的实际高度：适应不同高度墙面、适应曲面墙面
                if (Physics.Raycast(startDetectionPos + Vector3.up * maxError, player.transform.forward, out var hitInfo, 0.5f, player.whatIsGround)&&!isHandingRotation.Value)
                {
                    reusableData.hit = hitInfo;
                    Debug.DrawLine(startDetectionPos + Vector3.up * maxError, startDetectionPos + Vector3.up * maxError + player.transform.forward * 1f, Color.blue, 0.05f);
                }
                else
                {
                    reusableData.hit = ray;
                }
                Vector3 target = reusableData.hit.point + (Vector3.up * HangWallData.hightAndForwardOffSet.x) + reusableData.hit.normal * (HangWallData.hightAndForwardOffSet.y);

                if (Vector3.Distance(player.transform.position, target) > 0.04f)
                {
                    player.transform.position = Vector3.Lerp(player.transform.position, target, Time.deltaTime * 8f);
                }

                Debug.DrawLine(startDetectionPos, startDetectionPos + player.transform.forward * 1.0f, Color.red, 0.05f);
            }
            else
            {
                if (Physics.Raycast(startDetectionPos + Vector3.down * maxError, player.transform.forward, out var hitInfo, 0.5f, player.whatIsGround)&&!isHandingRotation.Value)
                {
                    reusableData.hit = hitInfo;

                    Vector3 target = reusableData.hit.point + (Vector3.up * HangWallData.hightAndForwardOffSet.x) + reusableData.hit.normal * (HangWallData.hightAndForwardOffSet.y);
                    if (Vector3.Distance(player.transform.position, target) > 0.04f)
                    {
                        player.transform.position = Vector3.Lerp(player.transform.position, target, Time.deltaTime * 8);

                        Debug.DrawLine(startDetectionPos + Vector3.down * maxError, startDetectionPos + Vector3.up * maxError + player.transform.forward * 0.8f, Color.blue, 0.05f);
                    }
                  
                }
                else
                {
                    isHandingRotation.Value = true;
                }
            }
        }
        // 更新角色朝向
        UpdateLockRotation(8f, -reusableData.hit.normal);

    }
    private void HandRotaion(bool noWall)
    {
        if (isInitMatchTargeting) { return; }
        if (isClimbUp) { return; }
        if (isClimbUpCancel) { return; }
        if (isHangOut) { return; }

        if (noWall)
        {
            //可能需要转向，检测转向位置是否可攀爬
            startDetectionPos += player.transform.forward*0.5f ;
            Vector3 detectionDir = reusableData.rotationValueParameter.CurrentValue >= 0 ? -player.transform.right : player.transform.right;
            Debug.DrawLine(startDetectionPos,startDetectionPos + detectionDir *0.8f,Color.green,5f);
            if (Physics.Raycast(startDetectionPos, detectionDir, out var hitInfo, 0.8f, player.whatIsGround))
            {
                reusableData.hit = hitInfo;
                Vector3 target = reusableData.hit.point + (Vector3.up * HangWallData.hightAndForwardOffSet.x) + reusableData.hit.normal * (HangWallData.hightAndForwardOffSet.y);
                handRotaionTask = () =>
                {
                    // 计算目标位置
                    player.transform.position = Vector3.Lerp(player.transform.position, target, Time.deltaTime * 6f);

                    if (Vector3.Distance(player.transform.position, target) < 0.1f)
                    {
                        isHandingRotation.Value = false;
                        handRotaionTask = null;
                    }
                };
            }
            else
            {
                Debug.Log("没有检测到可以攀爬的物体");
                OnEnterFall();
            }
        }
    }
    Vector3 matchSpeed =Vector3.zero;
}