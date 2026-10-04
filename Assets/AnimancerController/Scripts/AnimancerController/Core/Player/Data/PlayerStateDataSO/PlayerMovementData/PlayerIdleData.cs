using Animancer;
using System;
using UnityEngine;

[Serializable]
public class PlayerIdleData
{
    [field: SerializeField] public TransitionAsset idle { get; private set; }

    [field:SerializeField] public AnimationClip[] strandIdle_Lock { get; private set; }
    [field: SerializeField] public AnimationClip[] crouchIdle_Lock { get; private set; }
    [field: SerializeField] public AnimationClip[] strandIdle { get; private set; }
    [field: SerializeField] public AnimationClip[] crouchIdle { get; private set; }
}