
using Animancer;
using UnityEngine;
/*************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 玩家控制核心组件
*************************************************/
[RequireComponent(typeof(AnimancerComponent))]
public class Player : CharacterBase
{
    public PlayerSO playerSO;
    //移动业务
    public  AnimancerComponent animancer { get; private set; }
    public PlayerStateMachine StateMachine { get; private set; }
    public PlayerReusableData ReusableData { get; private set; }
    public PlayerReusableLogic ReusableLogic { get; private set; }
    public Transform camTransform { get; private set; }

    //服务模块
    public InputService InputService { get; private set; }
    public TimerService TimerService { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        InputService = InputService.Instance;
        TimerService = TimerService.Instance;

        camTransform = Camera.main.transform;
        animancer = GetComponent<AnimancerComponent>();
        if (animancer == null)
        {
            Debug.LogError("未指定Animancer组件，无法播放动画！！");
            return;
        }
        //创建参数
        ReusableData = new PlayerReusableData(animancer, playerSO);
        //创建逻辑
        ReusableLogic = new PlayerReusableLogic(this);
        //创建状态机
        StateMachine = new PlayerStateMachine(this);
        //设置默认开始状态
        StateMachine.ChangeState(StateMachine.idleState);
    }
    protected override void Update()
    {
        base.Update();
        StateMachine?.OnUpdate();
    }
    protected override void OnAnimatorMove()
    {
        base.OnAnimatorMove();
        StateMachine?.OnAnimationUpdate();
    }
    public void AnimationEnd()
    {
        StateMachine?.OnAnimationEnd();
    }

}
