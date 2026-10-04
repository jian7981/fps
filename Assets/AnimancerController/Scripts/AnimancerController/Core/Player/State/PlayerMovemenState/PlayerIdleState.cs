using UnityEngine.InputSystem;
public class PlayerIdleState : PlayerMovementState
{
    PlayerIdleData idleData;
    public PlayerIdleState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        idleData = playerSO.playerMovementData.PlayerIdleData;
    }
    public override void OnEnter()
    {
        base.OnEnter();
        reusableData.currentCrouchIdleIndex = -1;
        reusableData.currentStandIdleIndex = -1;
        reusableLogic.InitIldeState();
        reusableLogic.PlayNextState();
    }
    protected override void AddEventListening()
    {
        base.AddEventListening();
        inputServer.inputMap.Player.Move.started += MoveStart;
        inputServer.inputMap.Player.Jump.started += OnJumpStart;
        inputServer.inputMap.Player.Crouch.started += OnCrouch;
        player.isOnGround.ValueChanged += OnCheckFall;
        //ËøµÐÊÂ¼þ
        reusableData.lockValueParameter.Parameter.OnValueChanged += LockValueChange;
    }
    private void LockValueChange(float obj)
    {
       if (obj == 1||obj==0)//Ë÷µÐ
       {
            playerStateMachine.ChangeState(playerStateMachine.idleState);
       }
    }
    protected override void RemoveEventListening()
    {
        base.RemoveEventListening();
        inputServer.inputMap.Player.Move.started -= MoveStart;
        inputServer.inputMap.Player.Jump.started -= OnJumpStart;
        inputServer.inputMap.Player.Crouch.started -= OnCrouch;
        player.isOnGround.ValueChanged -= OnCheckFall;
        reusableData.lockValueParameter.Parameter.OnValueChanged -= LockValueChange;
    }
    private void MoveStart(InputAction.CallbackContext context)
    {
        playerStateMachine.ChangeState(playerStateMachine.moveStartState);
    }

    public override void OnUpdate()
    {
        base.OnUpdate();
        UpdateCashVelocity(player.AnimationVelocity);
        UpdateSpeed();
    }


}
