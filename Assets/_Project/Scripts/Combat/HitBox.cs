using UnityEngine;

// ============ 部位伤害标签【新增，1.8a】============
// 挂在"部位碰撞体"的物体上（头 / 手臂 / 腿…），告诉射击系统：这里是什么部位、伤害倍率多少
//
// 用法（每个部位三步）：
//   1. 在对应骨骼下新建空物体（比如 HitBox_Head）
//   2. 加一个 CapsuleCollider，调大小包住部位，并勾上 Is Trigger（关键：不勾会挡住角色移动）
//   3. 加本脚本，填 Damage Multiplier（头 2 / 四肢 0.75）
// 躯干不用加：角色身上原有的胶囊判定就是躯干（没标签 = 倍率 1）
public class HitBox : MonoBehaviour
{
    [Tooltip("伤害倍率：头 2 / 躯干 1 / 四肢 0.5")]
    [SerializeField] private float damageMultiplier = 1f;

    // 只读出口：交给射击系统读（WeaponController / BotController 命中时用）
    public float DamageMultiplier => damageMultiplier;
}