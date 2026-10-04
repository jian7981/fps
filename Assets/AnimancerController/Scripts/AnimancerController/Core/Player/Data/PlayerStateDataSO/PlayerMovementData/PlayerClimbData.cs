using Animancer;
using System;
using UnityEngine;

[Serializable]
public class PlayerClimbData
{    
    //根据高度的不同填入合适的动画

    //翻越动画
    [field: SerializeField] public ClipTransition[] vaults;
    //攀爬动画
    [field: SerializeField] public ClipTransition[] climbs;
    //与上面的动画一一对应
    public PlayerClimbAnimationSettings[] vaultSettings;
    public PlayerClimbAnimationSettings[] climbSettings;
    //攀墙拐角
    public ClipTransition outwardCorner_Left;
    public ClipTransition outwardCorner_Right;
    public ClipTransition inwardCorner_Left;
    public ClipTransition inwardCorner_right;
}