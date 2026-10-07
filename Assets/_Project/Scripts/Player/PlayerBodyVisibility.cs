using UnityEngine;
using Mirror;

// 【阶段2.1】玩家模型可见性（联机适配）
// 阶段1 的隐身机制：模型部件在层 7(selfbody) + 本地相机 Culling Mask 不含层 7 —— 对"自己"依然有效。
// 但联机时别人的模型也带着层 7 → 会被我的相机剔除、看不见对方。
// 所以本脚本只做一件事：非本地玩家 → 模型整树挪回 Default 层(0)，让各端互相可见。
// 本地玩家 / 纯单机：什么都不动（阶段1 的表现——包括阴影行为——保持不变）。
public class PlayerBodyVisibility : NetworkBehaviour
{
    public override void OnStartClient()
    {
        if (NetUtil.IsLocalControl(this)) return;   // 自己的玩家：保持层 7（本机相机看不见自己的身体）

        // 远程玩家（别人）：整棵模型树（含部位碰撞体）改回 Default 层，本机相机才能看到
        foreach (var t in GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = 0;
    }
}