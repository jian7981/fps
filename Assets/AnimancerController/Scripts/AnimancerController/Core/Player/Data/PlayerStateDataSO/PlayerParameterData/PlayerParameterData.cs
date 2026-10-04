using Animancer;
using UnityEngine;

[System.Serializable]
public class PlayerParameterData
{
    [field: SerializeField] public StringAsset standValueParameter { get; set; }
    [field: SerializeField] public StringAsset rotationValueParameter { get; set; }
    [field: SerializeField] public StringAsset speedValueParameter{ get; set; }
    [field: SerializeField] public StringAsset LockValueParameter { get; set; }
    [field: SerializeField] public StringAsset Lock_X_ValueParameter { get; set; }
    [field: SerializeField] public StringAsset Lock_Y_ValueParameter { get; set; }
    [field: SerializeField] public StringAsset moveInterruptEvent { get; set; }
    [field: SerializeField] public StringAsset cancelClimbEvent { get; set; }

 }