using UnityEngine;

[System.Serializable]
public class PlayerMovementData
{
    [field: SerializeField] public PlayerIdleData PlayerIdleData { get;private set; }
    [field: SerializeField] public PlayerMoveStartData PlayerMoveStartData { get; private set; }
    [field: SerializeField] public PlayerMoveLoopData PlayerMoveLoopData { get; private set; }
    [field: SerializeField] public PlayerMoveEndData PlayerMoveEndData { get; private set; }
    [field :SerializeField] public PlayerClimbData  PlayerClimbData { get; private set; }
    [field:SerializeField] public PlayerHangWallData PlayerHangWallData { get; set; }
    [field: SerializeField] public PlayerJumpFallAndLandData PlayerJumpFallAndLandData { get;private set; }

}