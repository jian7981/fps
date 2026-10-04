using System;
using System.Collections.Generic;

public class EventHandlerBase<T>
{
    //支持一个事件名但有多个事件的注册、且事件的传参依据各类事件函数来选择或者不使用
    private readonly Dictionary<T, List<Action<object, object>>> eventDic = new Dictionary<T, List<Action<object, object>>>();
   
    private readonly Dictionary<object,List<T>> targetEventDic = new Dictionary<object, List<T>>(); 

    public void AddEventHandler(T t, Action<object, object> action)
    {
        if (!eventDic.ContainsKey(t))
        {
            //eventDic.Add(t, new List<Action<object, object>>());使用索引器：
            eventDic[t]=new List<Action<object, object>>();
        }
        //避免从重复注册相同的事件
        List<Action<object,object>> eventList = eventDic[t];
        Action<object, object> checkAction = eventList.Find(i => i == action);
        if (checkAction != null)
        {
            //说明List存在
            return;
        }
        //不存在就添加上这个事件
        eventList.Add(action);
        //更新从属者信息
        object target = action.Target; 
        if (!targetEventDic.ContainsKey(target))
        {
            //targetEventDic.Add(target, new List<T>());
            targetEventDic[target] = new List<T>();
        }
        //把事件名添加到这个从属者列表中
        targetEventDic[target].Add(t);
    }

    public void RemoveEventByID(T t)
    {
        if (eventDic.ContainsKey(t))
        {
            //先不着急移除
            //更新target
            List<Action<object,object>> actions = eventDic[t];
            foreach (var action in actions)
            {
                object target = action.Target;
                if (target != null && targetEventDic.ContainsKey(target))
                {
                    //这里可能存在一个位置注册多个同名的事件（list会存在重复元素）
                    List<T> idList = targetEventDic[target];
                    idList.RemoveAll(id => id.Equals(t));//可以用拉姆达表达式
                    if (idList.Count == 0)
                    {
                        targetEventDic.Remove(target);
                    }
                }
            }
            eventDic.Remove(t);
        }
    }
    public void RemoveEventByTarget(object target)
    {
        if (targetEventDic.ContainsKey(target))
        {
            List<T> idList = targetEventDic[target];
            foreach (var id in idList)
            {
                if (eventDic.ContainsKey(id))
                {
                    List<Action<object, object>> actions = eventDic[id];
                    actions.RemoveAll(action => action.Target == target);
                    if (actions.Count == 0)
                    {
                        eventDic.Remove(id);
                    }
                }
            }
            targetEventDic.Remove(target);
        }
    }
    public List<Action<object, object>> GetEvent(T t)
    {
        if (eventDic.ContainsKey(t))
        {
            return eventDic[t];
        }
        return null;
    }
    
}
