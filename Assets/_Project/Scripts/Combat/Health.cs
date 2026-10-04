using System;                          // 引入 System 命名空间：为了用 Action（委托）和 IEnumerator 所在的类型体系
using System.Collections;              // 引入集合命名空间：为了用 IEnumerator（协程的返回类型）
using UnityEngine;                     // 引入 Unity 引擎命名空间：MonoBehaviour、GameObject、Mathf 都在这里

// 统一的受伤 / 死亡 / 复活入口
// 铁律：任何扣血都必须走 TakeDamage，外部不许直接改血量
// 联网后（阶段2）：伤害只在服务器执行，CurrentHp 改成 [SyncVar]，
// 客户端照样通过 OnHealthChanged / OnDamaged 刷新 UI —— 事件接口不变，UI 零改动
public class Health : MonoBehaviour   // 继承 MonoBehaviour：才能挂到游戏物体上、才能用协程和生命周期函数
{
    [Header("生命值")]                  // Header：只是让 Inspector 面板上显示一段分组标题，纯美化
    [SerializeField]                   // SerializeField：让 private 字段也能显示在 Inspector 里，但代码外部依然不能访问
    private int maxHp = 100;           // 最大血量（私有字段 + 序列化 = 面板可调、代码受保护）

    [Header("护甲（先扣护甲，吸收一部分伤害）")]
    [SerializeField] private int maxArmor = 50;              // 最大护甲值
    [Range(0f, 1f)]                                          // Range：把这个 float 在 Inspector 里变成 0~1 的滑条，防止手填 3.5 这种非法值
    [SerializeField] private float armorAbsorbRatio = 0.6f;  // 护甲吸收比例：0.6 表示 60% 的伤害打在护甲上

    [Header("队伍（0=A队，1=B队）")]
    [SerializeField] private int teamId = 0;                 // 所属队伍编号：以后 Bot、联机分队伍全靠它
    [SerializeField] private bool allowFriendlyFire = false; // 是否允许友军伤害，默认关闭（同队免伤）

    [Header("死亡后自动复活")]
    [SerializeField] private bool autoRespawn = true;        // 死亡后是否自动复活
    [SerializeField] private float respawnDelay = 3f;        // 复活等待秒数
    [Tooltip("留空=原地复活；填了=复活时传送过去（以后由 GameManager 按队伍出生点填充）")]
    // Tooltip：鼠标悬停在 Inspector 这项上时显示这句提示文字
    [SerializeField] private Transform respawnPoint;         // 复活点；没赋值就是 null，代码里会判空

    // ---------- 只读状态（外部只能读，不能改）----------
    // => 是"表达式体属性"，等价于只写 get 的属性，读 MaxHp 就等于读 maxHp
    public int MaxHp => maxHp;
    // { get; private set; } 是"自动属性"：外部能读，只有本类内部能写
    public int CurrentHp { get; private set; }               // 当前血量（外部只读，改它必须走 TakeDamage）
    public int MaxArmor => maxArmor;                         // 最大护甲（只读）
    public int CurrentArmor { get; private set; }            // 当前护甲（外部只读）
    public bool IsDead { get; private set; }                 // 是否已死亡（外部只读，给移动/射击脚本判断用）
    public int TeamId => teamId;                             // 队伍编号的只读出口

    // ---------- 事件（逻辑层只管发事件，表现层订阅）----------
    // event：事件关键字；Action<...>：System 命名空间下的泛型委托（= 一个没有返回值的函数签名）
    // 外部用 += 订阅，用 -= 取消订阅；本类内部用 ?.Invoke() 触发
    public event Action<int, GameObject> OnDamaged;   // 受伤时广播：(本次伤害总量, 攻击者)
    public event Action OnHealthChanged;              // 血量或护甲变了：HUD 收到后自己来读最新数值
    public event Action<GameObject> OnDied;           // 死亡时广播：(击杀者)
    public event Action OnRespawned;                  // 复活完成时广播（无参数）

    private Coroutine respawnCoroutine;               // 保存复活协程的句柄，用来防止重复启动协程

    private void Awake()                              // Awake：物体创建时执行一次（比 Start 更早）
    {
        CurrentHp = maxHp;                           // 出场时血量满
        CurrentArmor = maxArmor;                      // 出场时护甲满
        IsDead = false;                              // 出场时是活着的
    }

    // ★ 唯一伤害入口：所有掉血都必须调用这个方法
    public void TakeDamage(int damage, GameObject attacker)   // damage=伤害值，attacker=攻击者（可能是玩家/Bot/空）
    {
        if (IsDead || damage <= 0) return;       // 已经死了、或伤害不是正数 → 直接忽略，防止死后被鞭尸、负数回血

        // 同队免伤（attacker 为空的伤害，比如跌落/环境伤害，不算队友）
        // != gameObject 是排除"自己打自己"：自伤永远允许（测试脚本就用到这一点）
        if (!allowFriendlyFire && attacker != null && attacker != gameObject)
        {
            // GetComponentInParent：从攻击者身上（或它的父物体）找 Health，
            // 这样将来子弹、武器这类子物体当 attacker 也能正确找到所属角色
            Health attackerHealth = attacker.GetComponentInParent<Health>();
            // 找到对方的 Health 且队伍编号跟我一样 → 是队友 → 不掉血，直接返回
            if (attackerHealth != null && attackerHealth.teamId == teamId) return;
        }

        // 护甲先吃一部分伤害，吃不完的落到血量上
        int armorDamage = 0;                     // 记录这次护甲实际承受了多少伤害（用于日志/表现）
        if (CurrentArmor > 0)                    // 有护甲才走护甲结算
        {
            // Mathf.Min(护甲值, 伤害×0.6 向上取整)：护甲不够就只扣完护甲，不会扣成负数
            armorDamage = Mathf.Min(CurrentArmor, Mathf.CeilToInt(damage * armorAbsorbRatio));
            CurrentArmor -= armorDamage;         // 扣除护甲
        }
        int hpDamage = damage - armorDamage;     // 总伤害 - 护甲挡掉的 = 真正打到血量的部分
        // Mathf.Max(0, …)：血量最低是 0，不会出现负数
        CurrentHp = Mathf.Max(0, CurrentHp - hpDamage);

        // ?.Invoke：如果没人订阅（为 null）就不执行，避免空引用报错
        OnDamaged?.Invoke(damage, attacker);     // 广播"受伤"给表现层（飘字、红屏、音效）
        OnHealthChanged?.Invoke();               // 广播"数值变了"，HUD 会自己来读 CurrentHp / CurrentArmor

        if (CurrentHp <= 0) Die(attacker);       // 血量归零 → 进入死亡流程（把攻击者当击杀者传下去）
    }

    private void Die(GameObject killer)          // 死亡（私有：外部不能直接调用，只能通过打伤害触发）
    {
        if (IsDead) return;                      // 已经死了就不再重复触发（防重复广播）
        IsDead = true;                           // 先置死亡标记，后续伤害会被 TakeDamage 拦掉
        OnDied?.Invoke(killer);                  // 广播死亡：记分板、击杀提示都在这里接
        Debug.Log("死亡");
        // autoRespawn 为 true 且当前没有正在跑的复活协程 → 启动复活倒计时
        if (autoRespawn && respawnCoroutine == null)
            respawnCoroutine = StartCoroutine(RespawnAfterDelay());   // StartCoroutine 启动协程，并拿到句柄
    }

    private IEnumerator RespawnAfterDelay()      // 协程：返回 IEnumerator，可以"暂停若干秒"再继续
    {
        yield return new WaitForSeconds(respawnDelay);   // yield return：等 respawnDelay 秒（此处 3 秒）后继续往下执行
        respawnCoroutine = null;                 // 倒计时结束，清空句柄，允许下次死亡重新启动协程
        Revive();                                // 执行复活
    }

    // 复活：血量护甲回满；配了 respawnPoint 就传送过去
    // 以后 GameManager 会这样用：health.transform.position = 出生点.position; health.Revive();
    // 复活：血量护甲回满；配了 respawnPoint 就传送过去
    public void Revive()
    {
        // ---------- 传送回出生点 ----------
        if (respawnPoint != null)
        {
            // 【关键】有 CharacterController 时，直接改 transform.position 可能被控制器覆盖，
            // 标准做法：先禁用控制器 → 传送 → 再启用
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            Vector3 before = transform.position;          // 【诊断】传送前的位置
            transform.position = respawnPoint.position;
            Vector3 after = transform.position;           // 【诊断】传送后的位置

            if (cc != null) cc.enabled = true;

            Debug.Log($"[复活] 出生点={respawnPoint.name}，传送前={before}，传送后={after}");
        }
        else
        {
            // 【诊断】如果打出这条，说明 GameManager 没设置成功
            Debug.LogWarning("[复活] respawnPoint 是空的（GameManager 没设置成功？）→ 只能原地复活");
        }

        CurrentHp = maxHp;
        CurrentArmor = maxArmor;
        IsDead = false;

        OnHealthChanged?.Invoke();
        OnRespawned?.Invoke();
    }

    // 【新增】由外部系统（GameManager）设置出生点：比如按队伍随机抽一个
    public void SetRespawnPoint(Transform point)
    {
        respawnPoint = point;
    }

    // 【新增，步骤1.4】生成 Bot 时由 GameManager 调用：把队伍编号改成配置里的值
    // 它会影响两件事：1) 同队免伤判定  2) 队服颜色（TeamColorApplier 在 Start 里读 TeamId）
    public void SetTeamId(int newTeamId)
    {
        teamId = newTeamId;
    }

}