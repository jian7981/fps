using Animancer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

[System.Serializable]
public class PlayerMoveEndData
{
    [field: SerializeField] public TransitionAsset moveEnd_L { get; private set; }
    [field: SerializeField] public TransitionAsset moveEnd_R { get; private set; }
    [field: SerializeField] public ClipTransition moveToWall { get; private set; }
}