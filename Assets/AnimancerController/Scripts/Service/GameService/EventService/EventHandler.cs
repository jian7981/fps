using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.VisualScripting;

public class EventHandler<T>
{
    private readonly EventHandlerBase<T> eventHandlerBase =new EventHandlerBase<T>();
    private readonly Queue<EventMessage> eventQue = new Queue<EventMessage>();
    public void OnEventInit()
    {
        eventQue.Clear();
    }
    public void OnEventUpdate()
    {
        if (eventQue.Count > 0)
        {
            var eventMessage = eventQue.Dequeue();
            TriggerEvent(eventMessage);
        }
    }
    public void AddEventListening(T id,Action<object,object> action)
    {
        eventHandlerBase.AddEventHandler(id, action);
    }
    public void RemoveEventListeningByID(T id)
    {
        eventHandlerBase.RemoveEventByID(id);
    }
    public void RemoveEventListeningByTarget(object target)
    {
        eventHandlerBase.RemoveEventByTarget(target);
    }
    public void SentMessage(T t,object param1,object param2)
    {
        List<Action<object, object>> actions = eventHandlerBase.GetEvent(t);
        if (actions == null) { return; }
        foreach (var action in actions)
        {
            EventMessage eventMessage = new EventMessage(action,param1,param2);
            TriggerEvent(eventMessage);
        }
    }
    public void SentMessageByQue(T t, object param1, object param2)
    {
        List<Action<object, object>> actions = eventHandlerBase.GetEvent(t);
        foreach (var action in actions)
        {
            EventMessage eventMessage = new EventMessage(action, param1, param2);
            eventQue.Enqueue(eventMessage);
        }
    }

    private void TriggerEvent(EventMessage eventMessage)
    {
        eventMessage.action(eventMessage.param1,eventMessage.param2);
    }
}
public class EventMessage
{
   public Action<object,object> action;
   public object param1;
   public object param2;
   public EventMessage(Action<object, object> action, object param1, object param2)
    {
        this.action = action;
        this.param1 = param1;
        this.param2 = param2;
    }
}