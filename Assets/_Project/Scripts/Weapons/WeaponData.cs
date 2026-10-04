using UnityEngine;

// 武器配置（ScriptableObject）：一把枪 = 一个 .asset 资产文件
// 好处：以后加新枪不用改一行代码，只新建资产填数值
// [CreateAssetMenu] 会在 Project 窗口的右键菜单里加一项：Create → FPS → Weapon Data
[CreateAssetMenu(fileName = "NewWeapon", menuName = "FPS/Weapon Data", order = 0)]
public class WeaponData : ScriptableObject
{
    [Header("基础信息")]
    public string weaponName = "新武器";      // 显示名（以后 HUD 上显示）

    [Header("伤害与射程")]
    public int damage = 25;                  // 每发伤害
    public float range = 100f;               // 射程（米）

    [Header("射速")]
    public float fireRate = 8f;              // 每秒最多打几发（8 = 每秒 8 发）

    [Header("弹药")]
    public int magSize = 30;                 // 弹匣容量
    public int reserveAmmo = 90;             // 备弹总量
    public float reloadTime = 2f;            // 换弹耗时（秒）
    public bool autoReloadWhenEmpty = true;  // 打空后自动换弹
}