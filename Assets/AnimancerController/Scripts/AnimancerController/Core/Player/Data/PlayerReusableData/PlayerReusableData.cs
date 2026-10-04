
using Animancer;
using System;
using System.Collections.Generic;
using UnityEngine;
public enum ObstructHeight
{
    low =0,lowMedium =1, medium =2, mediumHight =3,Hight =4,
}
public enum ClimbType
{
    Vault,Climb
}
public enum MatchType
{
    Root,
    RootY,
}
public struct ClimbTargetMatchInfo
{
   public Vector3 TargetPos;//爬上去的目标位置
   public Vector3 InitPos;//开始进行目标位置匹配的初始位置
   public bool setTargetMatchInitPos;//是否完成最后的匹配操作

    public ClimbTargetMatchInfo(Vector3 TargetPos)
    {
        this.TargetPos = TargetPos;

        InitPos = Vector3.zero;
        setTargetMatchInitPos = false;
    }
}
/**************************************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 可变数据复用类，缓存可读可写数据
**************************************************************************/

public class PlayerReusableData
{
    public float currentRotationTime;
    //animancer控制混合树Mixer用到的参数
    public SmoothedFloatParameter standValueParameter { get; set; }
    public SmoothedFloatParameter rotationValueParameter { get; set; }
    public SmoothedFloatParameter speedValueParameter { get; set; }
    public SmoothedFloatParameter lockValueParameter { get; set; }
    public SmoothedFloatParameter lock_X_ValueParameter { get; set; }
    public SmoothedFloatParameter lock_Y_ValueParameter { get; set; }
    //锁敌
    public BindableProperty<Transform> lockTarget { get; set; } = new BindableProperty<Transform>();

    public int drawTargetId = -1;
    public int drawCurrentId = -1;
    public Vector3 targetDir;
    public BindableProperty<float> targetAngle = new BindableProperty<float>();
    public BindableProperty<string> currentState = new BindableProperty<string>();

    //IdleState
    public ManualMixerState standIdleMixerState;
    public ManualMixerState crouchIdleMixerState;
    public List<AnimancerState> standIdleList = new List<AnimancerState>();
    public List<AnimancerState> crouchIdleList = new List<AnimancerState>();
    public int currentStandIdleIndex;
    public int currentCrouchIdleIndex;
    public bool isLockIdle = false;
    //攀爬
    public ObstructHeight ObstructHeight;
    public ClimbType ClimbType;
    public ClipTransition targetClimbClip;
    //跳跃
    public float horizontalSpeed;
    //跳跃惯性
    public Vector3 currentInertialVelocity;
    public int cashIndex = 0;
    public readonly static int cashSize = 3;
    public Vector3[] cashVelocity = new Vector3[cashSize];

    //HangWall
    public float originalCCRadius;
    public Vector3 vaultPos;
    public RaycastHit hit;
    //打断点检测事件
    public Action inputInterruptionCB { get; set; }
    //检测墙的距离
    public float checkWallDistance = 0.6f;
    //是否原地跳跃
    public bool isInPlaceJump;
    //外力跳跃
    public float jumpExternalForce = 15;

    //
    public float currentMidInAirMultiplier = 0.6f;
    public PlayerReusableData(AnimancerComponent animancerComponent, PlayerSO playerSO)
    {
        standValueParameter  = new SmoothedFloatParameter(animancerComponent, playerSO.playerParameterData.standValueParameter,0.15f);
        standValueParameter.Parameter.Value = 1;

        rotationValueParameter = new SmoothedFloatParameter(animancerComponent,playerSO.playerParameterData.rotationValueParameter,0.2f);
        speedValueParameter = new SmoothedFloatParameter(animancerComponent, playerSO.playerParameterData.speedValueParameter, 1f);
        lockValueParameter = new SmoothedFloatParameter(animancerComponent,playerSO.playerParameterData.LockValueParameter,0.1f);
        lock_X_ValueParameter = new SmoothedFloatParameter(animancerComponent, playerSO.playerParameterData.Lock_X_ValueParameter, 0.3f);
        lock_Y_ValueParameter = new SmoothedFloatParameter(animancerComponent, playerSO.playerParameterData.Lock_Y_ValueParameter, 0.3f);
    }

}