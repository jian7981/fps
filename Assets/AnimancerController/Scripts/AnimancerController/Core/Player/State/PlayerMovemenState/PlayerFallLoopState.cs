using UnityEngine;

public class PlayerFallLoopState : PlayerMovementState
{
    PlayerJumpFallAndLandData jumpFallAndLandData;
    public PlayerFallLoopState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        jumpFallAndLandData = playerSO.playerMovementData.PlayerJumpFallAndLandData;
    }
    public override void OnEnter()
    {
        base.OnEnter();
        //this.Log("惯性速度：" + reusableData.currentInertialVelocity / Time.deltaTime);

        player.ignoreRootMotionY = false;
        animancer.Play(jumpFallAndLandData.fallStart).Events(player).OnEnd = OnFallLoop;
    }

    private void OnFallLoop()
    {
       animancer.Play(jumpFallAndLandData.fall);
    }

    public override void OnExit()
    {
        base.OnExit();
        player.ignoreRootMotionY = false;
    }
    public override void OnUpdate()
    {
        base.OnUpdate();
        reusableLogic.InAirMoveCheck(GetTargetDir());
        InAirMove();
        UpdateRotation(false, 0, true, 2);
        
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
        //检测着陆
        player.isOnGround.ValueChanged -= OnFallToLand;
        reusableData.inputInterruptionCB = null;
    }

}