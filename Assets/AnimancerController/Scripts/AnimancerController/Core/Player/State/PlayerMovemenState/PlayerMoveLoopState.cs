using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMoveLoopState : PlayerMovementState
{
    PlayerMoveLoopData moveLoopData;
    int tid;
    public PlayerMoveLoopState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        moveLoopData = playerSO.playerMovementData.PlayerMoveLoopData;
    }
    public override void OnEnter()
    {
        base.OnEnter();
        animancer.Play(moveLoopData.moveLoop);
        OnCheckInput();
        reusableData.rotationValueParameter.CurrentValue = 0;
    }


    public override void OnUpdate()
    {
        base.OnUpdate();
        UpdateCashVelocity(player.AnimationVelocity);
        if (reusableData.lockValueParameter.TargetValue == 1)
        {
            UpdateRotation(true, 0.5f, false);
        }
        else
        {
            UpdateRotation(true, 0.4f, true, 1.4f);
        }
        UpdateSpeed();
        if (reusableData.speedValueParameter.CurrentValue <= 1)
        {
            reusableData.checkWallDistance = 0.6f;
        }
        else
        {
            reusableData.checkWallDistance = 0.4f*reusableData.speedValueParameter.CurrentValue;
        }
         
        
        //检测前方有没有障碍物
        Debug.DrawLine(player.transform.position + Vector3.up, player.transform.position + Vector3.up + player.transform.forward* reusableData.checkWallDistance, Color.yellow, 0.05f);
        if (Physics.Raycast(player.transform.position + Vector3.up, player.transform.forward,out var hitInfo, reusableData.checkWallDistance, player.whatIsGround))
        {
            if (Mathf.Abs(ToolFunction.GetDeltaAngle(player.transform.forward, -hitInfo.normal)) < 40)
            {
                playerStateMachine.ChangeState(playerStateMachine.moveEndState);
            }
         
        }
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
    public override void OnExit()
    {
        base.OnExit();
        timerServer.RemoveTimer(tid);
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
}
