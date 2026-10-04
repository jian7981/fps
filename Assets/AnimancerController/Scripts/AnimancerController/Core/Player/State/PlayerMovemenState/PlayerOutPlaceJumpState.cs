using System;
using UnityEngine.InputSystem;

public class PlayerOutPlaceJumpState : PlayerMovementState
{
    PlayerJumpFallAndLandData jumpFallAndLandData;
    public PlayerOutPlaceJumpState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        jumpFallAndLandData = playerSO.playerMovementData.PlayerJumpFallAndLandData;
    }

    public override void OnEnter()
    {
        base.OnEnter();
        //CC跳跃
        player.ChangeVerticalSpeed(ToolFunction.GetJumpInitVelocity(0.8f, player.gravity));
        //禁用动画y位移
        player.ignoreRootMotionY = true;
        //播放向前跳跃动画
        animancer.Play(jumpFallAndLandData.forwardJumpStart);
    }
    protected override void AddEventListening()
    {
        base.AddEventListening();
        //检测着陆
        player.isOnGround.ValueChanged += OnLandGround;
    }
    protected override void RemoveEventListening()
    {
        base.RemoveEventListening();
        player.isOnGround.ValueChanged -= OnLandGround;
        inputServer.inputMap.Player.Jump.started -= OnJumpStart ;
        inputServer.inputMap.Player.Move.started -= OnMoveStart;
    }
    public override void OnUpdate()
    {
        base.OnUpdate();
        //UpdateRotation(false,0.7f,true,0.5f);
    }
    public override void OnExit()
    {
        base.OnExit();
        player.ignoreRootMotionY = false;
    }
    protected void OnLandGround(bool onGround)
    {
        if (onGround)
        {
            var state = animancer.Play(jumpFallAndLandData.forwardJumpLand[0]);
            state.Events(player).SetCallback(playerSO.playerParameterData.moveInterruptEvent, OnInputInterruption);
            state.Events(player).OnEnd = OnStateDefaultEnd;
        }
    }
}