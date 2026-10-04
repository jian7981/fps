using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
/**************************************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 有限状态机，缓存状态，驱动更新状态类
**************************************************************************/
public class PlayerStateMachine : StateMachineBase
{
   //缓存状态
    public Player player;
    public PlayerIdleState idleState;
    public PlayerMoveStartState moveStartState;
    public PlayerMoveLoopState moveLoopState;
    public PlayerMoveEndState moveEndState;
    public PlayerJumpState jumpState;
    public PlayerClimbState climbState;
    public PlayerLedgeClimbState ledgeClimbState;
    public PlayerMoveToWallState moveWallState;
    public PlayerFallLoopState fallLoopState;
    public PlayerPlatformerUpState platformerUpState;
    public PlayerLandState landState;
    public PlayerStateMachine(Player player)
    {
        this.player = player;
        idleState = new PlayerIdleState(this);
        moveStartState = new PlayerMoveStartState(this);
        moveLoopState = new PlayerMoveLoopState(this);
        moveEndState = new PlayerMoveEndState(this);
        jumpState= new PlayerJumpState(this);
        climbState = new PlayerClimbState(this);
        ledgeClimbState = new PlayerLedgeClimbState(this);
        moveWallState = new  PlayerMoveToWallState(this);
        fallLoopState = new PlayerFallLoopState(this);
        platformerUpState = new PlayerPlatformerUpState(this);
        landState= new PlayerLandState(this);
    }
    public override void ChangeState(IState targetState)
    {
        base.ChangeState(targetState);
        player.ReusableData.currentState.Value = targetState.GetType().Name;
    }
}
