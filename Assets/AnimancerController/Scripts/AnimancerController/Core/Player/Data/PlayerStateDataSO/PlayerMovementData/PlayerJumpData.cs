using Animancer;
using System;
using UnityEngine;

[Serializable]
public class PlayerJumpFallAndLandData
{
    [field: SerializeField] public ClipTransition placeJumpStart { get; private set; }
    [field: SerializeField] public ClipTransition forwardJumpStart { get; private set; }
    //下落    
    [field: SerializeField] public ClipTransition fallStart { get; private set; }
    [field: SerializeField] public ClipTransition fall{ get; private set; }

    [field: SerializeField] public ClipTransition[] placeJumpLand { get; private set; }//按照下落高度分为不同的着陆动画
    [field: SerializeField] public ClipTransition[] forwardJumpLand { get; private set; }//按照下落高度分为不同的着陆动画

    [field: SerializeField] public ClipTransition platformerUpStart;

    [field: SerializeField] public ClipTransition platformerUpLoop;

    [field: SerializeField] public ClipTransition platFormerDownLoop;
}