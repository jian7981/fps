/*************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 平台跳跃状态，强化运动手感
*************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
public class PlayerPlatformerUpState : PlayerMovementState
{
    PlayerJumpFallAndLandData jumpFallAndLandData;
    public PlayerPlatformerUpState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        jumpFallAndLandData = playerSO.playerMovementData.PlayerJumpFallAndLandData;
    }
    public override void OnEnter()
    {
        base.OnEnter();
        player.ChangeVerticalSpeed(ToolFunction.GetJumpInitVelocity(reusableData.jumpExternalForce, player.gravity));

        player.ignoreRootMotionY = true;

        animancer.Play(jumpFallAndLandData.platformerUpStart).Events(player).OnEnd = OnUpStartEnd;

        reusableData.currentInertialVelocity = UnityEngine.Vector3.zero;

        reusableData.currentMidInAirMultiplier = 2;

        reusableData.isInPlaceJump =false;

    }

    private void OnUpStartEnd()
    {
        animancer.Play(jumpFallAndLandData.platformerUpLoop);
    }

    public override void OnExit()
    {
        base.OnExit();
        player.ignoreRootMotionY = false;
        reusableData.currentMidInAirMultiplier = 0.6f;
    }
    public override void OnUpdate()
    {
        base.OnUpdate();
        if (player.verticalSpeed < 0)
        {
            animancer.Play(jumpFallAndLandData.platFormerDownLoop);
        }
        reusableLogic.InAirMoveCheck(GetTargetDir());
        InAirMove();
        UpdateRotation(false, 0, true, 2.2f);

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