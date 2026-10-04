using Animancer;
using UnityEngine;

[System.Serializable]
public class PlayerHangWallData
{
    public Vector2 hightAndForwardOffSet;
    
    //扒住
    public ClipTransition hang_wall_idle_down;
    public ClipTransition hang_wall_idle_up;
    public ClipTransition hang_wall_idle_frond;
    //直接爬上去
    public ClipTransition hang_wall_climb;
    //IdleLoop
    public ClipTransition hang_wall_idle;
    //移动后摇
    public ClipTransition hang_wall_idle_right_inertia;
    public ClipTransition hang_wall_idle_left_inertia;
    //攀墙爬
    public TransitionAsset hand_wallMove_Mixer;
    //爬上去
    public ClipTransition hang_wall_climb_up;
    //尝试爬，不能爬上去

    //爬上去取消的后摇
    public ClipTransition hang_wall_idle_inertia_01;
    //看后面

    //向左跳
    public ClipTransition hang_wall_idle_jump_out_left;
    //向右跳
    public ClipTransition hang_wall_idle_jump_out_right;
    //向上跳
    public ClipTransition hang_wall_idle_jump_out_up;

}