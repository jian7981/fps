
using Animancer;
using System;
using System.Collections.Generic;
using UnityEngine;

public class PlayerClimbState : PlayerMovementState
{
    PlayerClimbData climbData;
    AnimancerState animancerState;

    ClimbTargetMatchInfo targetMatchInfo_Start;
    ClimbTargetMatchInfo targetMatchInfo_Y;

    List<AnimancerEvent> animancerEventList = new List<AnimancerEvent>();
    PlayerClimbAnimationSettings animationSettings;
    int drawID;
    Action cancelClimbTask;
    public PlayerClimbState(PlayerStateMachine stateMachine) : base(stateMachine)
    {
        climbData = playerSO.playerMovementData.PlayerClimbData;
    }

    public override void OnEnter()
    {
        var clip = GetClimbAnimation();
        if (clip == null)
        {
            player.StateMachine.ChangeState(player.StateMachine.jumpState);
            return;
        }
        player.disEnableGravity = true;
        player.controller.enabled = false;
        player.applyFullRootMotion = true;
        animancerState = animancer.Play(clip);
        animancerState.ApplyFootIK = true;
        //获取攀爬或者翻越信息
        animationSettings = GetClimbTimeSetting();

        targetMatchInfo_Y = new ClimbTargetMatchInfo(reusableData.vaultPos + Vector3.up * animationSettings.targetHeightOffSet);
        targetMatchInfo_Start = new ClimbTargetMatchInfo(new Vector3(reusableData.hit.point.x, player.transform.position.y, reusableData.hit.point.z) + reusableData.hit.normal * (0.35f + animationSettings.startMatchDistanceOffset));
        base.OnEnter();
    }
    protected override void AddEventListening()
    {
        base.AddEventListening();
        animancerState.Events(player).OnEnd = OnStateDefaultEnd;
        animancerState.Events(player).SetCallback(playerSO.playerParameterData.moveInterruptEvent, OnInputInterruption);
        animancerState.Events(player).Add(animancerEventList,new AnimancerEvent(animationSettings.targetMatchTime.y + animationSettings.enableCCTimeOffset,ResetCC));
        SetCancelClimb();
    }
    protected override void RemoveEventListening()
    {
        base.RemoveEventListening();
        reusableData.inputInterruptionCB = null;
        cancelClimbTask = null;
        animancerState?.Events(player).RemoveAll(animancerEventList);
    }
    public override void OnExit()
    {
        base.OnExit();
        animancerState = null;
    }
    public override void OnUpdate()
    {
        base.OnUpdate();
        cancelClimbTask?.Invoke();
    }
   
    public override void OnAnimationUpdate()
    {
        base.OnAnimationUpdate();
        //运动匹配
        reusableLogic.ClimbTargetMatch(animancerState,ref targetMatchInfo_Start,animationSettings.startMatchTime.x, animationSettings.startMatchTime.y);
        reusableLogic.ClimbTargetMatch_Y(animancerState,ref targetMatchInfo_Y, animationSettings.targetMatchTime.x, animationSettings.targetMatchTime.y);
    }
    private void SetCancelClimb()
    {
        if (reusableData.ClimbType != ClimbType.Climb)
        {
            return;
        }
        if (reusableData.ObstructHeight == ObstructHeight.mediumHight)
        {
            animancerState.Events(player).Add(animancerEventList, new AnimancerEvent(0.15f,OnCancelClimb));
        }
    }
    private void OnCancelClimb()
    {
        if (animancerState.Speed == -1)//说明正在倒反不需要再取消了
        {
            return;
        }
        Debug.Log("开启取消攀爬动作检测！");
        cancelClimbTask = () =>
        {
            if (animancerState.NormalizedTime >= animationSettings.targetMatchTime.y + animationSettings.enableCCTimeOffset)
            {
                cancelClimbTask = null;
            }
            float angle = GetTargetAngle();
            if (inputServer.Move!=Vector2.zero && Mathf.Abs(angle) > 100)
            {
                Debug.Log("开始取消攀爬");
                float currentTime = animancerState.NormalizedTime;
                animancerState.Speed = -1;
                animancerState.Events(player).Add(animancerEventList, new AnimancerEvent(currentTime - 0.12f, FinishCancelClimb));
                cancelClimbTask = null;
            }
        };
    }

    private void FinishCancelClimb()
    {
        Debug.Log("完成取消攀爬");
        ResetCC();
        playerStateMachine.ChangeState(playerStateMachine.idleState);
    }

   
    protected override void OnInputInterruption()
    {
        base.OnInputInterruption();
        Debug.Log("开启移动检测！");
    }
    private void ResetCC()
    {
        Debug.Log("恢复CC");
        player.disEnableGravity = false;
        player.controller.enabled = true; 
        player.applyFullRootMotion = false;
    }
   
    public ClipTransition GetClimbAnimation()
    {
        int index = (int)reusableData.ObstructHeight;
        if (reusableData.ClimbType == ClimbType.Climb)
        {
            if (index >= climbData.climbs.Length)
            {
                return null;
            }
            if (index < 0)
            {
                return climbData.climbs[0];
            }
            ClipTransition target = climbData.climbs[index];
            return target;
        }
        else if (reusableData.ClimbType == ClimbType.Vault)
        {
            index--;//最低情况的障碍物不翻
            if (index < 0)
            {
                return climbData.vaults[0];
            }
            if (index >= climbData.vaults.Length)
            {
                return null;
            }
            ClipTransition target = climbData.vaults[index];
            return target;
        }
        return null;
    }
    public PlayerClimbAnimationSettings GetClimbTimeSetting()
    {
        int index = (int)reusableData.ObstructHeight;
        if (reusableData.ClimbType == ClimbType.Climb)
        {
            if (index >= climbData.climbSettings.Length)
            {
                return climbData.climbSettings[0];
            }
            PlayerClimbAnimationSettings targetSetting = climbData.climbSettings[index];
            if (targetSetting ==null)
            {
                return climbData.climbSettings[0];
            }
            return targetSetting;
        }
        else if(reusableData.ClimbType == ClimbType.Vault)
        {
            if (index >= climbData.vaults.Length)
            {
                return climbData.vaultSettings[0];
            }
            PlayerClimbAnimationSettings targetSetting = climbData.vaultSettings[index];
            if (targetSetting == null)
            {
                return climbData.vaultSettings[0];
            }
            return targetSetting;
        }
        return null;
    }
}