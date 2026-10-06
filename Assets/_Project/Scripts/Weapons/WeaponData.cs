using UnityEngine;

// 【新增，1.7d】武器类型：决定"开镜表现"走哪套模板
// 以后加新枪只选类型就能复用开镜动作：步枪=枪模挪到中心；狙击=直接切镜筒
public enum WeaponType
{
    Rifle = 0,    // 步枪：开镜时枪模平滑挪到屏幕中心 + 中倍率 FOV
    Sniper = 1,   // 狙击枪：开镜直接切换成瞄准镜（枪模隐藏 + 镜筒 UI + 大倍率）
}

// 武器配置（ScriptableObject）：一把枪 = 一个 .asset 资产文件
// 好处：以后加新枪不用改一行代码，只新建资产填数值
// [CreateAssetMenu] 会在 Project 窗口的右键菜单里加一项：Create → FPS → Weapon Data
[CreateAssetMenu(fileName = "NewWeapon", menuName = "FPS/Weapon Data", order = 0)]
public class WeaponData : ScriptableObject
{
    [Header("基础信息")]
    public string weaponName = "新武器";              // 显示名（HUD 上显示）
    public WeaponType weaponType = WeaponType.Rifle;  // 【新增，1.7d】武器类型（决定开镜表现模板）

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

    // ============== 【1.7d】以下三组：开镜 / 腰射散布 / 镜头后坐力 ==============

    [Header("开镜（ADS）")]
    public bool canAim = true;                 // 这把枪能不能开镜
    public float aimFOV = 45f;                 // 开镜后的相机视野角：越小倍率越大（狙击填 12 左右）
    public float aimSpeed = 12f;               // 开镜/收镜过渡速度（越大切得越快）
    public float aimSensitivityScale = 0.5f;   // 开镜时鼠标灵敏度的倍率（越小越稳，便于瞄准）

    [Header("腰射散布（单位：度，越大越飘）")]
    public float hipSpread = 1.2f;             // 腰射时的基础散布
    public float spreadPerShot = 0.5f;         // 每开一枪散布增加多少
    public float maxSpread = 5f;               // 散布上限
    public float spreadRecover = 4f;           // 每秒恢复多少度（视觉上=准星收紧速度）
    public float aimSpread = 0.05f;            // 开镜时的散布（接近 0 = 指哪打哪）

    [Header("镜头后坐力（开火时视角上跳）")]
    public float recoilPitch = 0.35f;          // 腰射-垂直：每发视角上跳多少度（主分量）
    public float recoilYawRandom = 0.15f;      // 腰射-水平：每发左右随机 ±多少度
    public float recoilRecover = 10f;          // 视角恢复速度（度/秒，腰射/开镜共用）
    public float aimRecoilPitch = 0.2f;        // 【新增】开镜-垂直：想有"开镜后坐力"就调大它
    public float aimRecoilYawRandom = 0.08f;   // 【新增】开镜-水平：开镜时左右随机 ±多少度
}