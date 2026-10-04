using Animancer;
using UnityEngine;

[System.Serializable]
public class PlayerMoveLoopData
{
    [field: SerializeField] public TransitionAsset moveLoop { get; private set; }
}