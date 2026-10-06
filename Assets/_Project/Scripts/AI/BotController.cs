using UnityEngine;

// ============ Bot 大脑：三状态状态机（步骤1.4）============
// Patrol（巡逻）：在出生点附近随机游走；看得见敌人 → 切 Chase
// Chase（追击）：朝敌人跑；追进攻击距离 → 切 Attack；跟丢太久 → 回 Patrol
// Attack（攻击）：站住、面向敌人开枪；敌人跑远/躲起来 → 回 Chase
// 铁律3：Bot 开枪也走统一伤害入口 Health.TakeDamage，不搞自己的扣血逻辑
// 挂载：挂在 Bot 预制体根物体上（和 Health / CharacterController / BotTargeting 同一物体）
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class BotController : MonoBehaviour
{
    // 三个状态；用 enum 让代码一眼看懂"现在在干嘛"
    private enum BotState { Patrol, Chase, Attack }

    [Header("移动")]
    [SerializeField] private float patrolSpeed = 2f;       // 巡逻速度（慢慢逛）
    [SerializeField] private float chaseSpeed = 5f;        // 追击速度
    [SerializeField] private float rotateSpeed = 360f;     // 转身速度（度/秒）
    [SerializeField] private float gravity = -18f;         // 重力（和 PlayerMotor 保持一致）
    [SerializeField] private float patrolRadius = 15f;     // 巡逻点最远离自己多少米
    [SerializeField] private float arriveDistance = 1.5f;  // 离巡逻点多近算"到了"
    [SerializeField] private float patrolTimeout = 8f;     // 一个巡逻点最多走几秒（防被墙卡死）

    [Header("感知")]
    [SerializeField] private float attackRange = 18f;      // 进入这个距离就停下开枪
    [SerializeField] private float eyeHeight = 1.6f;       // 眼睛高度（视线/弹道的起点）
    [SerializeField] private float loseSightTime = 3f;     // 跟丢敌人后最多再追几秒

    [Header("战斗")]
    [SerializeField] private int damage = 10;              // 每枪伤害
    [SerializeField] private float fireRate = 2f;          // 每秒几枪
    [SerializeField] private float fireRange = 60f;        // 子弹射程
    [SerializeField] private float spreadAngle = 2.5f;     // 散布角度（越大越不准）
    [SerializeField] private bool drawDebugShots = true;   // 调试：把弹道画成黄线（Scene 视图）

    [Header("动画")]
    [SerializeField] private Animator animator;                // 留空 = 自动找子物体
    [SerializeField] private string speedParameter = "Speed";  // 和 Animator Controller 里一致
    [SerializeField] private float animDamp = 10f;             // 速度平滑

    // ---------- 运行时引用 ----------
    private CharacterController controller;   // 移动
    private Health health;                    // 自己的血量（判死亡/复活）
    private BotTargeting targeting;           // 找敌人的"眼睛"

    // ---------- 运行时数据 ----------
    private BotState state = BotState.Patrol; // 当前状态（出生先巡逻）
    private float velocityY;                  // 竖直速度（重力累积）
    private Vector3 patrolPoint;              // 当前巡逻点
    private float patrolTimer;                // 当前巡逻点已经走了多少秒
    private Vector3 lastKnownPosition;        // 最后一次看见敌人的位置
    private float lostSightTimer;             // 跟丢敌人已经多少秒
    private float nextFireTime;               // 下一次允许开枪的时间点

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<Health>();
        targeting = GetComponent<BotTargeting>();
        if (animator == null) animator = GetComponentInChildren<Animator>();   // 和 PlayerMotor 一样：自动往下找

        // 忘挂 BotTargeting 就明确报错，别让 NullReference 到处飞
        if (targeting == null)
        {
            Debug.LogError("BotController：根物体上缺少 BotTargeting 组件，Bot 已停工");
            enabled = false;
            return;
        }
        PickRandomPatrolPoint();   // 先随便挑个巡逻点
    }

    private void OnEnable() { if (health != null) health.OnRespawned += OnRespawned; }   // 复活时重置状态机
    private void OnDisable() { if (health != null) health.OnRespawned -= OnRespawned; }   // 配对取消订阅

    private void Update()
    {
        if (health.IsDead)          // 死了：站住不动，动画回 Idle
        {
            SetAnimatorSpeed(0f);
            return;
        }

        Vector3 posBefore = transform.position;   // 记录移动前的位置（算动画速度用）
        targeting.Scan();                          // 定期找人（内部有节流，不会每帧搜全场）

        // 状态机主循环：每个状态一个函数
        switch (state)
        {
            case BotState.Patrol: UpdatePatrol(); break;
            case BotState.Chase: UpdateChase(); break;
            case BotState.Attack: UpdateAttack(); break;
        }

        ApplyGravity();

        // 和 PlayerMotor 相同的原理：用本帧实际位移算水平速度，喂给 Animator 的 Speed 参数
        Vector3 delta = transform.position - posBefore;
        float planarSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / Time.deltaTime;
        SetAnimatorSpeed(planarSpeed);
    }

    // ---------------- Patrol 巡逻 ----------------
    private void UpdatePatrol()
    {
        Health target = targeting.CurrentTarget;

        // 看得见敌人 → 开追（记下最后位置，切 Chase）
        if (target != null && CanSee(target))
        {
            state = BotState.Chase;
            lastKnownPosition = target.transform.position;
            lostSightTimer = 0f;
            return;
        }

        MoveTowards(patrolPoint, patrolSpeed);
        patrolTimer += Time.deltaTime;

        // 走到了、或者走太久（可能被墙卡住）→ 换一个新巡逻点
        Vector3 flatOffset = patrolPoint - transform.position;
        flatOffset.y = 0f;
        if (flatOffset.magnitude <= arriveDistance || patrolTimer >= patrolTimeout)
        {
            PickRandomPatrolPoint();
        }
    }

    // ---------------- Chase 追击 ----------------
    private void UpdateChase()
    {
        // 目标没了/死了/跑出视野范围 → 回巡逻
        if (!targeting.IsTargetValid())
        {
            BackToPatrol();
            return;
        }

        Health target = targeting.CurrentTarget;
        float distance = Vector3.Distance(transform.position, target.transform.position);
        bool canSee = CanSee(target);

        if (canSee)
        {
            lastKnownPosition = target.transform.position;   // 看得见：刷新"最后已知位置"
            lostSightTimer = 0f;
        }
        else
        {
            lostSightTimer += Time.deltaTime;                 // 看不见：开始计时
            if (lostSightTimer >= loseSightTime)              // 跟丢太久 → 放弃
            {
                BackToPatrol();
                return;
            }
        }

        // 看得见 + 进入攻击距离 → 开打
        if (canSee && distance <= attackRange)
        {
            state = BotState.Attack;
            return;
        }

        // 追：看得见就追实时位置，看不见就追"最后看到的位置"
        MoveTowards(canSee ? target.transform.position : lastKnownPosition, chaseSpeed);
    }

    // ---------------- Attack 攻击 ----------------
    private void UpdateAttack()
    {
        if (!targeting.IsTargetValid())
        {
            BackToPatrol();
            return;
        }

        Health target = targeting.CurrentTarget;
        float distance = Vector3.Distance(transform.position, target.transform.position);

        // 敌人跑远 / 躲到墙后 → 回到追击
        if (distance > attackRange || !CanSee(target))
        {
            state = BotState.Chase;
            return;
        }

        FaceTowards(target.transform.position);   // 站住，转向敌人

        // 身体转得差不多了才开枪，避免边转身边乱打
        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;
        if (Vector3.Angle(transform.forward, dir) < 15f)
        {
            FireAt(target);
        }
    }

    // 回巡逻状态（好几个地方都要用，收成一个函数）
    private void BackToPatrol()
    {
        state = BotState.Patrol;
        targeting.ClearTarget();
        PickRandomPatrolPoint();
    }

    // ---------------- 移动相关 ----------------
    // 朝一个点走：先转身，再水平前进（竖直方向交给重力）
    private void MoveTowards(Vector3 destination, float speed)
    {
        Vector3 dir = destination - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;   // 已经站在点上了

        FaceTowards(destination);
        controller.Move(dir.normalized * speed * Time.deltaTime);
    }

    // 平滑转身（只转水平方向）
    private void FaceTowards(Vector3 destination)
    {
        Vector3 dir = destination - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
    }

    // 重力：和 PlayerMotor 一个写法
    private void ApplyGravity()
    {
        if (controller.isGrounded && velocityY < 0f) velocityY = -2f;   // 落地钳住，避免越贴越深
        velocityY += gravity * Time.deltaTime;
        controller.Move(new Vector3(0f, velocityY, 0f) * Time.deltaTime);
    }

    // 随机挑一个巡逻点（单机 Demo 的简化做法，没上 NavMesh）
    private void PickRandomPatrolPoint()
    {
        Vector2 circle = Random.insideUnitCircle * patrolRadius;   // 随机一个圆内偏移
        patrolPoint = transform.position + new Vector3(circle.x, 0f, circle.y);
        patrolTimer = 0f;
    }

    // ---------------- 感知 / 开枪 ----------------
    // 视线检测：眼睛 → 敌人胸口，中间没有被墙挡住才算"看得见"
    private bool CanSee(Health target)
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 chest = target.transform.position + Vector3.up * 1.0f;

        if (Physics.Linecast(eye, chest, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            // 第一个挡路的东西如果是敌人自己（或自己身上的碰撞体）→ 视线通畅
            Health hitHealth = hit.collider.GetComponentInParent<Health>();
            return hitHealth == target || hitHealth == health;
        }
        return true;   // 中间什么都没挡
    }

    // 开枪：★ 伤害走统一入口 Health.TakeDamage（铁律3）
    private void FireAt(Health target)
    {
        if (Time.time < nextFireTime) return;              // 射速冷却还没到
        nextFireTime = Time.time + 1f / fireRate;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 aimPoint = target.transform.position + Vector3.up * 1.0f;   // 瞄准胸口

        // 随机散布：把瞄准方向随机偏一点点，避免每枪都必中
        Quaternion spread = Quaternion.Euler(
            Random.Range(-spreadAngle, spreadAngle),
            Random.Range(-spreadAngle, spreadAngle),
            0f);
        Vector3 direction = spread * (aimPoint - eye).normalized;

        if (drawDebugShots) Debug.DrawRay(eye, direction * fireRange, Color.yellow, 0.15f);   // Scene 视图看弹道

        // 【改，1.8a】RaycastAll + 过滤：跳过自己身上的部位碰撞体（否则会被自己的手臂挡住），
        // 并支持部位伤害倍率（头 2 / 四肢 0.75 / 躯干 1）
        // Collide：部位碰撞体勾了 Is Trigger（不挡角色移动），射线默认忽略触发器，这里显式允许
        RaycastHit[] hits = Physics.RaycastAll(eye, direction, fireRange, ~0, QueryTriggerInteraction.Collide);
        if (hits.Length == 0) return;
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));   // 从近到远排序

        for (int i = 0; i < hits.Length; i++)
        {
            Health hitHealth = hits[i].collider.GetComponentInParent<Health>();
            if (hitHealth == health) continue;   // 命中自己身上的部位 → 跳过，继续往后找
            if (hits[i].collider.isTrigger && hits[i].collider.GetComponent<HitBox>() == null) continue;   // 无关触发器忽略

            if (hitHealth != null && hitHealth != health)   // 打到的是别人
            {
                // 【新增，1.8a】部位伤害：命中带 HitBox 标签的部位按倍率算
                HitBox hitBox = hits[i].collider.GetComponent<HitBox>();
                float multiplier = (hitBox != null) ? hitBox.DamageMultiplier : 1f;
                int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * multiplier));

                hitHealth.TakeDamage(finalDamage, gameObject);   // 统一入口；同队免伤在 Health 里兜底
            }
            break;   // 第一个有效命中就结束（子弹不穿透）
        }
    }

    // ---------------- 动画 / 复活 ----------------
    // 复活时把一切归零，重新开始巡逻
    private void OnRespawned()
    {
        state = BotState.Patrol;
        velocityY = 0f;
        lostSightTimer = 0f;
        nextFireTime = 0f;
        targeting.ClearTarget();
        PickRandomPatrolPoint();
        SetAnimatorSpeed(0f);
    }

    // 和 PlayerMotor 同一套：平滑地把速度写进 Animator 参数
    private void SetAnimatorSpeed(float targetSpeed)
    {
        if (animator == null) return;
        float current = animator.GetFloat(speedParameter);
        animator.SetFloat(speedParameter, Mathf.Lerp(current, targetSpeed, animDamp * Time.deltaTime));
    }
}