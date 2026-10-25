using UnityEngine;

// 【阶段2.5】第三人称武器（挂骨骼版）
// 需求对应：
//   ① 统一持枪动作：第三人称枪的姿态【不受开镜影响】——别的玩家看不出你是否在开镜（和平精英机制）
//   ② 绑定评估结论 = 双枪方案：
//      第一人称 = 相机上的 view model（WeaponViewModel 管开镜姿态/后坐/贴墙压低，手感体系不动）
//      第三人称 = 本脚本：把"当前武器预制体的副本"挂到右手骨骼下，跟随动画自然摆动
//      贴墙收枪：第一人称由 view model 压低（WeaponViewModel 读 WeaponController.WallAmount）；
//      第三人称由本脚本的⑤按同样的思路压低手上的枪 —— 两边表现都自然
//   ③ 层对齐：生成时复制手部骨骼当前的层
//      本机自己的枪=自见剔除层（本地看不见，第一人称看到的是 view model）
//      远程玩家=正常层（人人都能看到他手里的枪）—— 与 PlayerBodyVisibility 的机制完全一致
//   ④ 同步：切枪事件在各端都会触发（本机由事件、远程由 SyncVar hook）→ 各端各自换手上的枪，天然一致
//   ⑤ 贴墙避让：拿着枪贴近墙时，把枪【程序化压低】（只动枪的变换，不动骨骼、不做动画）——
//      旋转：枪口朝下；位移：往裆部方向收（往身体中心 + 往下 + 往身后）→ 防止第三人称的枪一直插进墙里。
//      和第一人称的贴墙收枪同一套检测思路（前/左前/右前三条射线）。
//      纯表现、每端自己算：不联网同步（和开镜不同步一个道理），谁看谁算，各端观感一致
// 挂载：Player.prefab 和 Bot.prefab 的根物体上（拖好右手骨骼 + 按武器顺序拖枪预制体）
public class ThirdPersonWeapon : MonoBehaviour
{
    [Header("挂点")]
    [Tooltip("右手骨骼。**留空 = 自动按 Humanoid 语义取**（HumanBodyBones.RightHand）→ 以后换角色模型不用再手拖骨骼")]
    [SerializeField] private Transform handBone;

    [Header("枪预制体（顺序必须和 WeaponController 的武器列表一致）")]
    [SerializeField] private GameObject[] gunPrefabs;      // 0=AK74，1=M107 …

    [Header("握持姿态微调（数组顺序必须和枪预制体一致；先在 Play 模式调好再抄回来）")]
    [SerializeField] private Vector3[] holdLocalPositions;   // 每把枪的本地位置（0=AK74，1=M107…）
    [SerializeField] private Vector3[] holdLocalEulers;      // 每把枪的本地旋转（欧拉角）
    [SerializeField] private Vector3[] holdLocalScales;      // 每把枪的本地缩放（用来校正两把枪的大小差）

    [Header("兜底武器（没有 WeaponController 的对象用，比如 Bot）")]
    [Tooltip("Bot 不走武器系统（开火是 BotController 自己的一套逻辑），收不到切枪事件；" +
             "这里指定它默认握哪把枪（数组下标，0 = 第一把）。玩家不受影响，永远由切枪事件驱动")]
    [SerializeField] private int fallbackIndex = 0;

    [Header("贴墙避让（程序化压低：只动枪的变换，不动骨骼、不做动画）")]
    [Tooltip("开：拿着枪靠近墙时，把枪口往下压（观感≈低位持枪），避免枪一直插进墙里")]
    [SerializeField] private bool wallAdjustEnabled = true;
    [Tooltip("开始压低的手→墙距离（米）：站在这个距离内就开始把枪往下压")]
    [SerializeField] private float wallCheckDistance = 1.1f;
    [Tooltip("完全压低的手→墙距离（米）：贴到这个距离时压到最大角度")]
    [SerializeField] private float fullLowerDistance = 0.4f;
    [Tooltip("最大压低角度（度）：枪口朝下摆这么多（75 ≈ 枪基本垂在身侧）")]
    [SerializeField] private float wallLowerAngle = 75f;
    [Tooltip("压低的额外位移（角色本地空间：X 右+/左−，Y 上+/下−，Z 前+/后−；单位≈米，乘贴墙程度）。" +
             "默认 = 往下 + 往身后 + 往身体中心收 ≈ 把枪收到裆部附近。不想位移就把三个数改 0")]
    [SerializeField] private Vector3 wallLowerOffset = new Vector3(-0.1f, -0.25f, -0.12f);
    [Tooltip("压低/回正的过渡速度（越大越干脆）")]
    [SerializeField] private float wallLowerSpeed = 8f;

    [Header("调试")]
    [Tooltip("调握持姿态用：勾上后本脚本不再每帧重设枪的姿态 → 可以在 Play 模式里直接拖枪物体自由摆位（拖的不会被顶回去）。" +
             "摆好后把枪的 localPosition / localRotation(欧拉角) / localScale 抄进上面三个数组，再取消勾选即可立刻验证")]
    [SerializeField] private bool freezePoseForTuning = false;

    private WeaponController weapons;
    private Health health;
    private GameObject currentGun;      // 当前挂在手上的枪副本
    private int currentIndex = -1;      // 当前显示的武器下标（-1 = 还没生成）
    private float wallAmount;           // 贴墙压低程度 0~1（每帧平滑逼近，LateUpdate 用它摆枪）

    private void Awake()
    {
        weapons = GetComponent<WeaponController>();
        health = GetComponent<Health>();
        ResolveHandBone();   // 【阶段4-C】右手骨骼留空 → 自动按 Humanoid 语义解析
    }

    // 【阶段4-C】右手骨骼：留空就自动从 Animator 取 HumanBodyBones.RightHand
    //   为什么这样更稳：骨骼引用从"手拖某个模型的骨骼实例"变成"按 Humanoid 语义查" ——
    //   换角色模型（只要是 Humanoid）不用再回来重拖一次（与 ModelFacingCorrector 的取法一致）
    //   public：以后做"运行时换角色/换装"时，换完模型调一次它就能重新挂到新骨骼上
    public void ResolveHandBone()
    {
        if (handBone != null) return;   // 手拖了就用你拖的（允许覆盖）

        Animator anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.avatar != null && anim.isHuman)
            handBone = anim.GetBoneTransform(HumanBodyBones.RightHand);

        if (handBone == null)
            Debug.LogWarning("[第三人称武器] 找不到右手骨骼：模型里没有 Animator，或它不是 Humanoid " +
                             "→ 请在 Inspector 里手拖 handBone", this);
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

    private void Start()
    {
        // 【修复】Bot 身上没有 WeaponController（AI 开火是 BotController 自己的逻辑），
        // 收不到切枪事件 → 这里兜底生成一把默认枪，保证单机时 AI 手上也能看到枪。
        // 玩家有 WeaponController → 由下面的事件驱动，不走这里。
        if (weapons == null) ShowGun(fallbackIndex);
    }

    // 切枪 / 初始武器（各端都会收到：本机玩家由事件驱动；远程玩家由 SyncVar hook 驱动）
    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        ShowGun(index);
    }

    // 生成/替换手上的枪（切枪事件和 Bot 兜底共用）
    private void ShowGun(int index)
    {
        if (handBone == null || gunPrefabs == null) return;
        if (index < 0 || index >= gunPrefabs.Length || gunPrefabs[index] == null) return;
        if (index == currentIndex && currentGun != null) return;   // 没变化，不重建

        // 换掉手上的枪：销毁旧的，生成新枪挂在骨骼下
        if (currentGun != null) Destroy(currentGun);
        currentGun = Instantiate(gunPrefabs[index], handBone);
        currentGun.transform.localPosition = GetPoseValue(holdLocalPositions, index, Vector3.zero);
        currentGun.transform.localRotation = Quaternion.Euler(GetPoseValue(holdLocalEulers, index, Vector3.zero));
        currentGun.transform.localScale = GetPoseValue(holdLocalScales, index, Vector3.one);

        // 枪预制体可能自带碰撞体（模型里可能有占位碰撞）：全部清掉，避免影响子弹判定和角色移动
        Collider[] colliders = currentGun.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++) Destroy(colliders[i]);

        ApplyLayer(currentGun);   // 层对齐（本人=剔除层；远程=正常层）
        currentIndex = index;
    }

    // 大厅/展示用：不经过 WeaponController，直接切换手上显示的武器。
    // index 对应 gunPrefabs / holdLocal* 数组下标；以后做"选择带入武器 / 选择大厅展示武器"时，
    // 大厅侧只需要找到角色的 ThirdPersonWeapon 并调用这个方法。
    // 注意：战斗里不要直接调用它绕过 WeaponController，否则会和真实武器状态不同步。
    public void SetDisplayWeapon(int index)
    {
        ResolveHandBone();   // 换角色后 handBone 可能为空，先按 Humanoid 重解析
        ShowGun(index);
    }

    // 取第 index 把枪的姿态配置；数组没配 / 不够长时用默认值（位置/旋转 = 0，缩放 = 1）
    private Vector3 GetPoseValue(Vector3[] array, int index, Vector3 defaultValue)
    {
        if (array == null || index < 0 || index >= array.Length) return defaultValue;
        return array[index];
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

    // 【贴墙避让】在动画之后执行（LateUpdate）：
    // 先把手上的枪摆回配置好的握持姿态，再按贴墙程度叠加"旋转（枪口朝下）+ 位移（往裆部方向收）"
    private void LateUpdate()
    {
        if (currentGun == null || handBone == null) return;

        // 【调试】调握持姿态期间整段跳过：不再重设枪的姿态（否则你在 Play 里拖枪会被下一帧顶回去）。
        // 代价是贴墙压低也一起跳过 —— 正好方便看清"干净"的握持姿态；调完记得取消勾选。
        if (freezePoseForTuning) return;

        UpdateWallAmount();   // 每帧检测：手附近有没有墙（平滑逼近 0~1）

        // 枪是 GameObject，操作姿态要走 .transform
        Transform gun = currentGun.transform;

        // 1. 摆回握持姿态（手骨骼有动画在动，枪是它的子物体，跟着走；这里只保证枪自身的本地姿态正确）
        gun.localPosition = GetPoseValue(holdLocalPositions, currentIndex, Vector3.zero);
        gun.localRotation = Quaternion.Euler(GetPoseValue(holdLocalEulers, currentIndex, Vector3.zero));

        // 2. 贴墙压低 = 旋转 + 位移（都乘贴墙程度，越近越到位）
        if (wallAmount > 0.001f)
        {
            // 旋转：绕玩家右轴在世界空间旋转 —— 正角度 = 枪口朝下（不碰手/手臂的动画）
            Quaternion drop = Quaternion.AngleAxis(wallLowerAngle * wallAmount, transform.right);
            gun.rotation = drop * gun.rotation;

            // 位移：偏移写在"角色本地空间"（X 右+/左−，Y 上+/下−，Z 前+/后−），
            // 换算成手骨骼空间的位移再叠加 —— 观感 = 枪从手边往裆部方向收过去
            Vector3 worldOffset = transform.TransformDirection(wallLowerOffset) * wallAmount;
            gun.localPosition += handBone.InverseTransformVector(worldOffset);
        }
    }

    // 【贴墙避让】贴墙程度检测：从手部骨骼朝 正前/右前45°/左前45° 三条短射线
    // 【阶段4-C 改动】三向射线 + "命中角色不算墙"的判定抽到 Shared/WallProbe，与第一人称共用同一套实现
    //   （原来两边各写了一份一模一样的；现在的区别只剩"起点"：第一人称从相机、第三人称从手）
    private void UpdateWallAmount()
    {
        float target = 0f;

        // 死了不检测（枪已收起）；关掉开关就永远保持 0（不压低）
        if (wallAdjustEnabled && !(health != null && health.IsDead) && handBone != null)
            target = WallProbe.Probe(handBone.position, transform.forward, transform.right,
                                     wallCheckDistance, fullLowerDistance);

        wallAmount = Mathf.MoveTowards(wallAmount, target, wallLowerSpeed * Time.deltaTime);
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