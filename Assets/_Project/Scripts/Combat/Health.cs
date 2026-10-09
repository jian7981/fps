using System;                          // 为了用 Action（委托）
using System.Collections;              // 为了用 IEnumerator（协程）
using UnityEngine;                     // Unity 引擎命名空间
using Mirror;                          // 【阶段2.2】NetworkBehaviour / SyncVar

// 统一的受伤 / 死亡 / 复活入口
// 铁律：任何扣血都必须走 TakeDamage，外部不许直接改血量
// 【阶段2.2】服务器权威 + 状态同步：
//   1) 血量/护甲/死亡 改成 [SyncVar]，服务器一改，各端自动收到
//   2) TakeDamage 只在服务器执行（纯单机没有网络会话时自动直通，行为不变）
//   3) 原有 4 个事件照旧触发（hook 驱动）
//      → HUD、死亡动画、死亡镜头、计分等订阅方一行都不用改
public class Health : NetworkBehaviour   // 【阶段2.2】MonoBehaviour → NetworkBehaviour
{
    [Header("生命值")]
    [SerializeField] private int maxHp = 100;

    [Header("护甲（先扣护甲，吸收一部分伤害）")]
    [SerializeField] private int maxArmor = 50;
    [Range(0f, 1f)]
    [SerializeField] private float armorAbsorbRatio = 0.6f;

    [Header("队伍（0=A队，1=B队）")]
    // 【阶段2.4-B】改 SyncVar：进图时服务器按房间分配下发，客户端的上色（TeamColorApplier）才能对上
    [SyncVar] private int teamId = 0;
    [SerializeField] private bool allowFriendlyFire = false;

    [Header("死亡后自动复活")]
    [SerializeField] private bool autoRespawn = true;
    [SerializeField] private float respawnDelay = 3f;
    [Tooltip("留空=原地复活；填了=复活时传送过去（单机由 GameManager 填；联机的出生点复活在后续步骤做）")]
    [SerializeField] private Transform respawnPoint;

    // ---------- 同步状态：服务器改，各端自动收到 ----------
    [SyncVar(hook = nameof(HookHealthChanged))] private int currentHp;
    [SyncVar(hook = nameof(HookHealthChanged))] private int currentArmor;
    [SyncVar(hook = nameof(HookIsDead))] private bool isDead;

    // ---------- 只读状态（外部只能读，不能改）----------
    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public int MaxArmor => maxArmor;
    public int CurrentArmor => currentArmor;
    public bool IsDead => isDead;
    public int TeamId => teamId;

    // ---------- 事件（接口没变，订阅方零改动）----------
    public event Action<int, GameObject> OnDamaged;   // (伤害量, 攻击者) —— 联机下只在服务器触发（计分用）
    public event Action OnHealthChanged;              // 血量/护甲变了：HUD 收到后自己来读最新数值
    public event Action<GameObject> OnDied;           // 死亡广播：(击杀者)。客户端收到的 killer 为 null（动画/镜头/UI 都不用它）
    public event Action OnRespawned;                  // 复活完成时广播

    private Coroutine respawnCoroutine;

    private void Awake()
    {
        currentHp = maxHp;       // 出场满血
        currentArmor = maxArmor; // 出场满甲
        isDead = false;
    }

    // 【阶段2.3】联机：玩家由 NetworkManager 生成，GameManager 不再负责登记（它整体跳过了）
    // → 服务器在"每个角色生成时"把他登记进 MatchRules（这是团队计分 / 战绩表的根基）
    // 纯单机不会触发 OnStartServer，登记仍由 GameManager 负责，行为不变
    public override void OnStartServer()
    {
        MatchRules rules = FindObjectOfType<MatchRules>();
        if (rules == null) return;

        // 玩家对象带连接：显示"玩家 1 / 玩家 2"；以后联机 Bot 没有连接，退回"玩家"（2.4 再细化名字）
        string displayName = (connectionToClient != null)
            ? "玩家 " + (connectionToClient.connectionId + 1)
            : "玩家";
        rules.RegisterCombatant(this, displayName);
    }



    // ---------- SyncVar 变化时触发原有事件（各端表现层零改动）----------
    private void HookHealthChanged(int oldValue, int newValue) => OnHealthChanged?.Invoke();

    private void HookIsDead(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // 死亡：服务器自己会用"真实击杀者"在 Die() 里触发一次；这里只负责客户端
            if (NetUtil.IsServerSide()) return;
            OnDied?.Invoke(null);   // 客户端：死亡动画 / 死亡镜头 / 死亡 UI
        }
        else
        {
            OnRespawned?.Invoke();  // 复活：各端都要（动画复位 / 镜头回位 / 枪恢复）
        }
    }

    // ★ 唯一伤害入口：所有掉血都必须调用这个方法
    // 【阶段2.2】联机中只有服务器能真正扣血；客户端调用直接忽略（结果通过 SyncVar 同步回来）
    public void TakeDamage(int damage, GameObject attacker)
    {
        if (!NetUtil.IsServerSide()) return;   // 联机 + 不是服务器 → 拒绝（纯单机恒为 true，保持原行为）

        if (isDead || damage <= 0) return;     // 已经死了、或伤害不是正数 → 忽略

        // 同队免伤（attacker 为空的伤害，比如跌落/环境伤害，不算队友）
        if (!allowFriendlyFire && attacker != null && attacker != gameObject)
        {
            Health attackerHealth = attacker.GetComponentInParent<Health>();
            if (attackerHealth != null && attackerHealth.teamId == teamId) return;
        }

        // 护甲先吃一部分伤害，吃不完的落到血量上
        int armorDamage = 0;
        if (currentArmor > 0)
        {
            armorDamage = Mathf.Min(currentArmor, Mathf.CeilToInt(damage * armorAbsorbRatio));
            currentArmor -= armorDamage;         // 走 SyncVar：各端自动收到 + 触发 OnHealthChanged
        }
        int hpDamage = damage - armorDamage;
        currentHp = Mathf.Max(0, currentHp - hpDamage);   // 同上

        OnDamaged?.Invoke(damage, attacker);     // 服务器侧事件（计分用）

        // 【修复，2.2-A】Mirror 只在 host 模式赋值时才自动调 hook（单机/服务器不会）
        // → 权威侧显式补一次（host 模式会重复一次，订阅方都是幂等操作，无影响）
        OnHealthChanged?.Invoke();

        if (currentHp <= 0) Die(attacker);       // 血量归零 → 死亡流程
    }

    private void Die(GameObject killer)
    {
        if (isDead) return;
        isDead = true;                           // 走 SyncVar：各端 HookIsDead(true)（客户端触发 OnDied）

        // 服务器用真实击杀者再触发一次本地事件（击杀归属需要 killer）
        if (NetUtil.IsServerSide()) OnDied?.Invoke(killer);

        Debug.Log("死亡");

        // 复活倒计时只在服务器跑（客户端等 SyncVar 同步回来）
        if (autoRespawn && respawnCoroutine == null)
            respawnCoroutine = StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);   // 等 respawnDelay 秒（3 秒）
        respawnCoroutine = null;
        Revive();
    }

    // 复活：血量护甲回满；配了 respawnPoint 就传送过去
    // 【阶段2.2】联机时由服务器执行；各端通过 SyncVar 同步（OnRespawned 由 hook 触发）
    // 复活：血量护甲回满
    // 【阶段2.2-C】联机：位置是"客户端权威"，服务器选好出生点后发 RpcRespawn 让本人机器传送；
    //    纯单机：沿用原来的（GameManager 配的 respawnPoint）传送逻辑
    public void Revive()
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            // 【阶段2.4-C 扩展】联机复活：优先回到"自己座位对应的出生点"（进图时由 GameNetworkManager
            // 记录在 respawnPoint 上）；如果这个点已被别人占用（有人站在附近）→ 顺延到本队下一个空点；
            // 全被占 → 就回自己的点；最后再兜底原来的 NetworkStartPosition 随机逻辑
            Transform start = null;
            GameManager gm = FindObjectOfType<GameManager>();
            if (gm != null)
            {
                Transform[] list = (teamId == 0) ? gm.teamASpawnPoints : gm.teamBSpawnPoints;
                if (list != null && list.Length > 0)
                {
                    int startIndex = 0;
                    if (respawnPoint != null)
                    {
                        for (int i = 0; i < list.Length; i++)
                            if (list[i] == respawnPoint) { startIndex = i; break; }
                    }
                    for (int i = 0; i < list.Length; i++)
                    {
                        Transform candidate = list[(startIndex + i) % list.Length];
                        if (candidate != null && !IsSpawnOccupied(candidate.position)) { start = candidate; break; }
                    }
                    if (start == null) start = list[startIndex];   // 全被占：只好回自己的座位点
                }
            }
            if (start == null && NetworkManager.singleton != null)
                start = NetworkManager.singleton.GetStartPosition();

            Vector3 pos = (start != null) ? start.position : transform.position;
            Quaternion rot = (start != null) ? start.rotation : transform.rotation;
            RpcRespawn(pos, rot);   // 只发给这个角色"本人"那台机器执行
        }
        else
        {
            // ---------- 纯单机：逻辑与原来完全一致 ----------
            if (respawnPoint != null)
            {
                CharacterController cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                Vector3 before = transform.position;
                transform.position = respawnPoint.position;
                Vector3 after = transform.position;

                if (cc != null) cc.enabled = true;

                Debug.Log($"[复活] 出生点={respawnPoint.name}，传送前={before}，传送后={after}");
            }
            else
            {
                Debug.LogWarning("[复活] respawnPoint 是空的（GameManager 没设置成功？）→ 只能原地复活");
            }
        }

        currentHp = maxHp;      // 走 SyncVar：客户端侧通过 hook 收到
        currentArmor = maxArmor;
        isDead = false;

        // 【修复，2.2-A】Mirror 只在 host 模式赋值时才自动调 hook（单机不会）→ 权威侧显式补一次
        // （host 模式会重复一次，订阅方都是幂等操作，无影响）
        OnHealthChanged?.Invoke();
        OnRespawned?.Invoke();
    }


    // 【阶段2.2-C】复活传送 RPC：服务器调用 → 在"这个角色本人的机器"上执行
    // 为什么必须这样：位置是客户端权威（NetworkTransform ClientToServer），
    // 服务器直接改坐标会被本人机器随后上报的位置覆盖 —— 只有本人机器传送才真正生效
    [TargetRpc]
    private void RpcRespawn(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;   // 标准做法：先禁用控制器 → 传送 → 再启用

        transform.position = position;
        transform.rotation = rotation;

        if (cc != null) cc.enabled = true;

        Debug.Log($"[复活-联机] 已传送回出生点 {position}");
    }

    // 【阶段2.4-C 扩展】出生点是否"被占用"：有别的"活着的"玩家站在附近（2.5 米内）
    private bool IsSpawnOccupied(Vector3 position)
    {
        Health[] all = FindObjectsOfType<Health>();
        foreach (Health h in all)
        {
            if (h == this || h.isDead) continue;   // 自己 / 尸体不算
            if (Vector3.Distance(h.transform.position, position) < 2.5f) return true;
        }
        return false;
    }

    // 【新增】由外部系统（GameManager）设置出生点：比如按队伍随机抽一个
    public void SetRespawnPoint(Transform point)
    {
        respawnPoint = point;
    }

    // 【新增，步骤1.4】生成 Bot 时由 GameManager 调用：把队伍编号改成配置里的值
    public void SetTeamId(int newTeamId)
    {
        teamId = newTeamId;
    }
}