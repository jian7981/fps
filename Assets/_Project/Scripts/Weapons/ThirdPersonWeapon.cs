using UnityEngine;

// 【阶段2.5】第三人称武器（挂骨骼版）
// 需求对应：
//   ① 统一持枪动作：第三人称枪的姿态【不受开镜影响】——别的玩家看不出你是否在开镜（和平精英机制）
//   ② 绑定评估结论 = 双枪方案：
//      第一人称 = 相机上的 view model（WeaponViewModel 管开镜姿态/后坐/贴墙压低，手感体系不动）
//      第三人称 = 本脚本：把"当前武器预制体的副本"挂到右手骨骼下，跟随动画自然摆动
//      贴墙收枪（压低）只作用于第一人称 —— 别的玩家看到的一直是自然持枪，不会出现"枪莫名其妙往下压"
//   ③ 层对齐：生成时复制手部骨骼当前的层
//      本机自己的枪=自见剔除层（本地看不见，第一人称看到的是 view model）
//      远程玩家=正常层（人人都能看到他手里的枪）—— 与 PlayerBodyVisibility 的机制完全一致
//   ④ 同步：切枪事件在各端都会触发（本机由事件、远程由 SyncVar hook）→ 各端各自换手上的枪，天然一致
// 挂载：Player.prefab 和 Bot.prefab 的根物体上（拖好右手骨骼 + 按武器顺序拖枪预制体）
public class ThirdPersonWeapon : MonoBehaviour
{
    [Header("挂点")]
    [Tooltip("右手骨骼（展开角色模型 → 骨骼树里的右手，拖进来）")]
    [SerializeField] private Transform handBone;

    [Header("枪预制体（顺序必须和 WeaponController 的武器列表一致）")]
    [SerializeField] private GameObject[] gunPrefabs;      // 0=AK74，1=M107 …

    [Header("握持姿态微调（在 Play 模式下拖着调，满意后抄回这些值）")]
    [SerializeField] private Vector3 holdLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 holdLocalEuler = Vector3.zero;
    [SerializeField] private Vector3 holdLocalScale = Vector3.one;

    private WeaponController weapons;
    private Health health;
    private GameObject currentGun;      // 当前挂在手上的枪副本
    private int currentIndex = -1;      // 当前显示的武器下标（-1 = 还没生成）

    private void Awake()
    {
        weapons = GetComponent<WeaponController>();
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (weapons != null) weapons.OnWeaponChanged += HandleWeaponChanged;
        if (health != null) { health.OnDied += HandleDied; health.OnRespawned += HandleRespawned; }
    }

    private void OnDisable()
    {
        if (weapons != null) weapons.OnWeaponChanged -= HandleWeaponChanged;   // 配对取消订阅
        if (health != null) { health.OnDied -= HandleDied; health.OnRespawned -= HandleRespawned; }
    }

    // 切枪 / 初始武器（各端都会收到：本机玩家由事件驱动；远程玩家由 SyncVar hook 驱动）
    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        if (handBone == null || gunPrefabs == null) return;
        if (index < 0 || index >= gunPrefabs.Length || gunPrefabs[index] == null) return;
        if (index == currentIndex && currentGun != null) return;   // 没变化，不重建

        // 换掉手上的枪：销毁旧的，生成新枪挂在骨骼下
        if (currentGun != null) Destroy(currentGun);
        currentGun = Instantiate(gunPrefabs[index], handBone);
        currentGun.transform.localPosition = holdLocalPosition;
        currentGun.transform.localRotation = Quaternion.Euler(holdLocalEuler);
        currentGun.transform.localScale = holdLocalScale;

        // 枪预制体可能自带碰撞体（模型里可能有占位碰撞）：全部清掉，避免影响子弹判定和角色移动
        Collider[] colliders = currentGun.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);

        ApplyLayer(currentGun);   // 层对齐（本人=剔除层；远程=正常层）
        currentIndex = index;
    }

    // 层对齐：递归复制手部骨骼当前的层
    // （时序保证：远程玩家对象在 OnStartClient 里已经把模型整树改成正常层，之后才生成手上的枪）
    private void ApplyLayer(GameObject gun)
    {
        int layer = (handBone != null) ? handBone.gameObject.layer : 0;
        gun.layer = layer;
        Transform[] all = gun.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = layer;
    }

    // 死亡：收起手上的枪（尸体不握枪，观感更正常）
    private void HandleDied(GameObject killer)
    {
        if (currentGun != null) currentGun.SetActive(false);
    }

    // 复活：重新握枪
    private void HandleRespawned()
    {
        if (currentGun != null) currentGun.SetActive(true);
    }
}