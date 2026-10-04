using Animancer;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public static class ExpandClass
{
    /// <summary>
    /// 检查当前动画的标签是否为传参中的标签
    /// </summary>
    /// <param name="animator"></param>
    /// <param name="tag"></param>
    /// <param name="layer"></param>
    /// <returns></returns>
    public static bool AnimationAtTag(this Animator animator, string tag, int layer = 0)
    {
        return animator.GetCurrentAnimatorStateInfo(layer).IsTag(tag);
    }
    /// <summary>
    /// 添加animancerEvent，并更新animancerEventList
    /// </summary>
    /// <param name="events"></param>
    /// <param name="animancerEventList"></param>
    /// <param name="animancerEvent"></param>
    public static void Add(this AnimancerEvent.Sequence events, List<AnimancerEvent> animancerEventList, AnimancerEvent animancerEvent)
    {
        events.Add(animancerEvent);
        animancerEventList.Add(animancerEvent);
    }
    /// <summary>
    /// 移除animancerEvent，并更新animancerEventList
    /// </summary>
    /// <param name="events"></param>
    /// <param name="animancerEventList"></param>
    /// <param name="animancerEvent"></param>
    public static void Remove(this AnimancerEvent.Sequence events, List<AnimancerEvent> animancerEventList, AnimancerEvent animancerEvent)
    {
        events.Remove(animancerEvent);
        animancerEventList.Remove(animancerEvent);  
    }
    /// <summary>
    /// 销毁所有在animancerEventList的事件
    /// </summary>
    /// <param name="events"></param>
    /// <param name="animancerEventList"></param>
    public static void RemoveAll(this AnimancerEvent.Sequence events, List<AnimancerEvent> animancerEventList)
    {
        foreach (var animancerEvent in animancerEventList)
        {
            events.Remove(animancerEvent);
        }
        animancerEventList.Clear();
    }
}