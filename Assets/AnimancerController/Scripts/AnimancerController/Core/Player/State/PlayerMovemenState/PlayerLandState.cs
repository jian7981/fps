/*************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 着陆状态
*************************************************/
using Animancer;


public class PlayerLandState : PlayerMovementState
{
    public PlayerLandState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
    }
    public override void OnEnter()
    {
        base.OnEnter();
        //
        if (player.isOnGround.Value)
        {
            AnimancerState state = null;
            int index = 0;
            if (player.verticalSpeed < -15)
            {
                index = 1;
            }
            if (!reusableData.isInPlaceJump)
            {
                if (playerSO.playerMovementData.PlayerJumpFallAndLandData.forwardJumpLand.Length == 1)
                {
                    index = 0;
                }
                state = animancer.Play(playerSO.playerMovementData.PlayerJumpFallAndLandData.forwardJumpLand[index]);
            }
            else
            {
                if (playerSO.playerMovementData.PlayerJumpFallAndLandData.placeJumpLand.Length == 1)
                {
                    index = 0;
                }
                state = animancer.Play(playerSO.playerMovementData.PlayerJumpFallAndLandData.placeJumpLand[index]);
            }
            state.Events(player).SetCallback(playerSO.playerParameterData.moveInterruptEvent, OnInputInterruption);
            state.Events(player).OnEnd = OnStateDefaultEnd;
        }
        else
        {
            OnStateDefaultEnd();
        }
    }
}