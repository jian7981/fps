using UnityEngine;

[System.Serializable]
public class PlayerLockMovementData
{
    [field: SerializeField] public PlayerIdleData PlayerIdleData { get; private set; }
    [field: SerializeField] public PlayerMoveLoopData PlayerMoveLoopData { get; private set; }
    [field: SerializeField] public PlayerMoveEndData PlayerMoveEndData { get; private set; }
}