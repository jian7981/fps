using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMoveToWallState : PlayerMovementState
{
    public PlayerMoveToWallState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
    }

    public override void OnEnter()
    {
        base.OnEnter();
        animancer.Play(playerSO.playerMovementData.PlayerMoveEndData.moveToWall);
    }

    protected override void AddEventListening()
    {
        base.AddEventListening();
        inputServer.inputMap.Player.Move.started += OnMove;
    }

    private void OnMove(InputAction.CallbackContext context)
    {
        Vector3 dir = GetTargetDir();
        if (Physics.Raycast(player.transform.position + Vector3.up, dir, 1, player.whatIsGround))
        {
            return;
        }
        else
        {
            player.StateMachine.ChangeState(player.StateMachine.moveStartState);
        }
    }
}