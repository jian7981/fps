using UnityEngine.Rendering;

public class StateMachineBase
{
    public IState currentState;
    public IState lastState;

    /// <summary>
    /// 状态切换的API
    /// </summary>
    /// <param name="targetState"></param>
    public virtual void ChangeState(IState targetState)
    {
        currentState?.OnExit();
        lastState = currentState;
        currentState = targetState;
        currentState?.OnEnter();
    }
    /// <summary>
    /// 动画状态退出的接口
    /// </summary>
    public void OnAnimationEnd()
    {
        currentState.OnAnimationEnd();
    }
    /// <summary>
    /// Update状态API
    /// </summary>
    public void OnUpdate()
    {
        currentState?.OnUpdate();
    }
    /// <summary>
    /// 按动画帧来更新
    /// </summary>
    public void OnAnimationUpdate()
    {
        currentState?.OnAnimationUpdate();
    }

}
