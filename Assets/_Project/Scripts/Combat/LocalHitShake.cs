using UnityEngine;
using Mirror;   // NetworkBehaviour（判"这是不是本机玩家自己"）

// 【阶段4-B】本机挨打时的镜头反馈：把 Health 的受击信号接到 CameraShaker
// 挂载：玩家根物体（和 Health 同物体）—— 只需要挂 Player.prefab，Bot 不用挂
//
// 为什么用 OnHealthChanged 而不是 OnDamaged：
//   OnDamaged 只在"服务器侧"触发（Health.TakeDamage 开头就 return），它的用途是计分；
//   而 OnHealthChanged 是 SyncVar 的 hook → 纯单机 / host / 客机三条路径都会触发，
//   所以本机受击反馈**不需要任何网络改动**。
// 为什么强度看"血量 + 护甲 的总量差"：
//   护甲会先吃掉一部分伤害，只看血量会漏掉"打甲不掉血"的那几发；
//   而（护甲吃掉的 + 落到血上的）总量差恰好等于这一下承受的伤害。
[RequireComponent(typeof(Health))]
public class LocalHitShake : MonoBehaviour
{
    [Header("引用（留空自动从子物体里找）")]
    [SerializeField] private CameraShaker shaker;

    [Header("强度映射")]
    [Tooltip("挨到这么多伤害 = 满强度（默认 40 点，狙击一枪就顶格）")]
    [SerializeField] private float damageForFullShake = 40f;
    [Tooltip("再轻的命中也要让玩家感觉到，所以有个下限")]
    [Range(0f, 1f)] [SerializeField] private float minStrength = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float maxStrength = 1f;

    [Header("抖动时长")]
    [SerializeField] private float shakeDuration = 0.22f;

    private Health health;
    private NetworkBehaviour netBehaviour;
    private int lastPool;         // 上一帧的"血量 + 护甲"总量
    private bool initialized;

    private void Awake()
    {
        health = GetComponent<Health>();
        netBehaviour = GetComponent<NetworkBehaviour>();   // 判"是不是本机玩家自己"用
        if (shaker == null) shaker = GetComponentInChildren<CameraShaker>(true);
    }

    private void Start()
    {
        // Start 在所有 Awake 之后跑 → 这时血量/护甲已经是初值（出生满血满甲）
        // 客机可能还没收到初始同步（此时读到的还是 0），那也没关系：
        // 初始值同步回来时总量是"变大的"，差值算出负数 → 不会误抖，基准自然被纠正
        if (health == null) return;
        lastPool = health.CurrentHp + health.CurrentArmor;
        initialized = true;
    }

    private void OnEnable()
    {
        if (health != null) health.OnHealthChanged += HandleHealthChanged;
    }

    private void OnDisable()
    {
        if (health != null) health.OnHealthChanged -= HandleHealthChanged;   // 配对取消订阅
    }

    private void HandleHealthChanged()
    {
        if (health == null) return;

        int pool = health.CurrentHp + health.CurrentArmor;

        // 万一事件比 Start 还早（极少）：只记基准，不抖
        if (!initialized) { initialized = true; lastPool = pool; return; }

        int delta = lastPool - pool;
        lastPool = pool;

        if (delta <= 0) return;                              // 加血 / 复活 → 不抖
        if (!NetUtil.IsLocalControl(netBehaviour)) return;   // 别人挨打不该影响我的镜头
        if (shaker == null) return;

        float strength = Mathf.Clamp(delta / damageForFullShake, minStrength, maxStrength);
        shaker.Shake(strength, shakeDuration);
    }
}
