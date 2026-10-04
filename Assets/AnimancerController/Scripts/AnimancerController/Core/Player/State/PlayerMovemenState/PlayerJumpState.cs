using UnityEngine;

public class PlayerJumpState : PlayerMovementState
{
    PlayerJumpFallAndLandData jumpFallAndLandData;

    
    public PlayerJumpState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        jumpFallAndLandData = playerSO.playerMovementData.PlayerJumpFallAndLandData;
    }
    public override void OnEnter()
    {
        base.OnEnter();
        reusableData.currentInertialVelocity = GetInertialVelocity();
        Debug.Log("惯性速度：" + reusableData.currentInertialVelocity / Time.deltaTime);
        reusableData.currentInertialVelocity.y = 0;

        player.ChangeVerticalSpeed(ToolFunction.GetJumpInitVelocity(0.8f, player.gravity));

        //禁用动画y位移
        player.ignoreRootMotionY = false;    
        //播放原地跳跃动画
        if (inputServer.Move == Vector2.zero)
        {
            animancer.Play(jumpFallAndLandData.placeJumpStart).Events(player).OnEnd = OnEnterFall;
            reusableData.isInPlaceJump = true;
        }
        else
        {
            animancer.Play(jumpFallAndLandData.forwardJumpStart).Events(player).OnEnd = OnEnterFall;
            reusableData.isInPlaceJump = false;
        }
    }


    protected override void AddEventListening()
    {
        base.AddEventListening();
        //检测着陆
        player.isOnGround.ValueChanged += OnFallToLand;
    }
    protected override void RemoveEventListening()
    {
        base.RemoveEventListening();
        player.isOnGround.ValueChanged -= OnFallToLand;
        reusableData.inputInterruptionCB = null;
    }
    public override void OnUpdate()
    {
        base.OnUpdate();
        InAirMove();
        UpdateRotation(false,0,true,2);
    }

 

    public override void OnExit()
    {
        base.OnExit();
        player.ignoreRootMotionY = false;
    }
  
}