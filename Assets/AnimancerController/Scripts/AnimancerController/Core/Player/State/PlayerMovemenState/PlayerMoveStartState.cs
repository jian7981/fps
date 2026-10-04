using Animancer;
using UnityEngine.InputSystem;
/**************************************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 玩家起步状态
**************************************************************************/
public class PlayerMoveStartState : PlayerMovementState
{
    PlayerMoveStartData moveStartData;
    float targetAngle;
    bool isForwardMove;
    int tid;
    AnimancerState state = null;
    public PlayerMoveStartState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        moveStartData = playerSO.playerMovementData.PlayerMoveStartData;
    }
    public override void OnEnter()
    {
        base.OnEnter();
        if (reusableData.lockValueParameter.TargetValue == 1)
        {
            playerStateMachine.ChangeState(playerStateMachine.moveLoopState);
            return;
        }
        targetAngle = UpdateRotation();
        if (targetAngle < 22.5 &&targetAngle >=0|| targetAngle >= -22.5&&targetAngle <= 0)
        {
            state = animancer.Play(moveStartData.moveStart_F);
            isForwardMove = true;
        }
        else if (targetAngle >= 22.5 && targetAngle < 67.5)
        {
            state = animancer.Play(moveStartData.moveStart_R45);
        }
        else if (targetAngle >= 67.5 && targetAngle < 112.5)
        {
            state = animancer.Play(moveStartData.moveStart_R90);
        }
        else if (targetAngle >= 112.5 && targetAngle < 157.5)
        {
            state = animancer.Play(moveStartData.moveStart_R135);
        }
        else if (targetAngle >= 157.5 || targetAngle < -157.5)
        {
            state = animancer.Play(moveStartData.moveStart_R180);
        }
        else if (targetAngle >= -157.5 && targetAngle < -112.5)
        {
            state = animancer.Play(moveStartData.moveStart_L135);
        }
        else if (targetAngle >= -112.5 && targetAngle < -67.5)
        {
            state = animancer.Play(moveStartData.moveStart_L90);
        }
        else if (targetAngle >= -67.5 && targetAngle < -22.5)
        {
            state = animancer.Play(moveStartData.moveStart_L45);
        }
        state.Events(player).OnEnd = OnMoveStartEnd;
    }
    protected override void AddEventListening()
    {
        base.AddEventListening();
        inputServer.inputMap.Player.Jump.started += OnJumpStart;
        inputServer.inputMap.Player.Move.canceled += OnCheckMoveEnd;
        inputServer.inputMap.Player.Crouch.started += OnCrouch;
        player.isOnGround.ValueChanged += OnCheckFall;
    }
    protected override void RemoveEventListening()
    {
        base.RemoveEventListening();
        inputServer.inputMap.Player.Jump.started -= OnJumpStart;
        inputServer.inputMap.Player.Move.canceled -= OnCheckMoveEnd;
        inputServer.inputMap.Player.Crouch.started -= OnCrouch;
        player.isOnGround.ValueChanged -= OnCheckFall;
    }
    private void OnCheckMoveEnd(InputAction.CallbackContext context)
    {
        OnCheckInput();
    }

    private void OnCheckInput()
    {
        if (inputServer.Move != UnityEngine.Vector2.zero)
        {
            return;
        }
        playerStateMachine.ChangeState(playerStateMachine.moveEndState);
    }
    public override void OnExit()
    {
        base.OnExit();
        timerServer.RemoveTimer(tid);
        isForwardMove = false;
    }
    private void OnMoveStartEnd()
    {
        playerStateMachine.ChangeState(playerStateMachine.moveLoopState);
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        UpdateCashVelocity(player.AnimationVelocity);
        if (state.NormalizedTime > 0.4f|| isForwardMove)
        {
            UpdateRotation(false, 0.7f, true, 1.8f);
        }
        UpdateSpeed();
    }
}