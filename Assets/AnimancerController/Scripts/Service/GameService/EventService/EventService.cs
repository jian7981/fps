using System;
public enum EventID//这里添加事件ID
{
    OnDoneStateUIInit
}
/**************************************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 事件系统
**************************************************************************/

public class EventService : MonoSingleton<EventService>
{
    private EventHandler<EventID> EventHandler = new EventHandler<EventID>();

    protected override void Awake()
    {
        base.Awake();
        EventHandler.OnEventInit();
    }

    private void Update()
    {
       EventHandler?.OnEventUpdate();
    }
    private void OnDestroy()
    {
        EventHandler = null;
    }
    /// <summary>
    /// 添加事件，参数超过2个的，封装个类传递参数
    /// </summary>
    /// <param name="eventID"></param>
    /// <param name="action"></param>
    public void AddEventListening(EventID eventID, Action<object, object> action)
    {
        EventHandler.AddEventListening(eventID, action);
    }
    /// <summary>
    /// 通过ID注销某一类事件：这会将所有地方注册的该类事件都给注销
    /// </summary>
    /// <param name="eventID"></param>
    public void RemoveEventListeningByID(EventID eventID)
    {
        EventHandler.RemoveEventListeningByID(eventID);
    }
    /// <summary>
    /// 清除该对象所有注册的事件
    /// </summary>
    /// <param name="target"></param>
    public void RemoveEventListeningByTarget(object target)
    {
        EventHandler.RemoveEventListeningByTarget(target);
    }
    /// <summary>
    /// 立即处理事件
    /// </summary>
    /// <param name="eventID"></param>
    /// <param name="param1"></param>
    /// <param name="param2"></param>
    public void SendMessage(EventID eventID, object param1, object param2)
    {
        EventHandler.SentMessage(eventID, param1, param2);
    }
    /// <summary>
    /// 分帧处理事件
    /// </summary>
    /// <param name="eventID"></param>
    /// <param name="param1"></param>
    /// <param name="param2"></param>
    public void SendMessageByQue(EventID eventID, object param1, object param2)
    {
        EventHandler.SentMessageByQue(eventID, param1, param2);
    }
}
