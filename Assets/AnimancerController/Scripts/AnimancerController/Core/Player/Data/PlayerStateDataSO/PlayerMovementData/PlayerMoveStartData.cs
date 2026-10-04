using Animancer;
using UnityEngine;

[System.Serializable]
public class PlayerMoveStartData
{
    [field: SerializeField] public TransitionAsset moveStart { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_F { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_L45 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_L90 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_L135 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_L180 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_R45 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_R90 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_R135 { get; private set; }
    [field: SerializeField] public TransitionAsset moveStart_R180 { get; private set; }

}