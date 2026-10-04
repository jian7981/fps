using System;
using System.Threading;
/**************************************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 定时器工具类V1.0基础版(完整版在HuHuFrameWork中)
**************************************************************************/
public class TimerService : MonoSingleton<TimerService>
{
    public TickTimer tickTimer { get; private set; }
    protected override void Awake()
    {
        base.Awake();
        tickTimer = new TickTimer();    
    }
    private void Update()
    {
        //驱动计时器
        tickTimer.UpdateTime();
    }
    private void OnDestroy()
    {
        //清除所有计时任务
        tickTimer.ResetTimer();
    }
    /// <summary>
    /// 添加计时任务
    /// </summary>
    /// <param name="time">定时事件，单位毫秒</param>
    /// <param name="taskCB">定时CallBack任务</param>
    /// <param name="cancelCB">注销定时CallBack</param>
    /// <param name="count">循环次数，小于等于0，代表无限次循环</param>
    /// <returns></returns>
    public int AddTimer(int time, Action taskCB, Action cancelCB = null, int count = 1)
    {
      return tickTimer.AddTimer(time,taskCB,cancelCB,count);
    }
    /// <summary>
    /// 移除计时任务，通过Tid参数注销
    /// </summary>
    /// <param name="tid"></param>
    public void RemoveTimer(int tid)
    {
        tickTimer.DeleteTimer(tid);
    }
}