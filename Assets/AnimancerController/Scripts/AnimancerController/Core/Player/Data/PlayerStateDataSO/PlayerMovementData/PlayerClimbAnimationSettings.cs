using UnityEngine;
[System.Serializable]
public class PlayerClimbAnimationSettings
{
    public Vector2 startMatchTime;
    public Vector2 targetMatchTime;
    public float targetHeightOffSet;//目标高度的偏移量
    public float startMatchDistanceOffset;//开始爬&翻越的距离的偏移量
    public float enableCCTimeOffset;//开启CC组件相对于TargetMatchTime的偏移量，进入攀爬状态时会自动禁用
}