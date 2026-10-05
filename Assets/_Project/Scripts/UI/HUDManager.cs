using System.Collections;      // 协程
using UnityEngine;
using UnityEngine.UI;          // Image
using TMPro;                   // TextMeshProUGUI

// ============ 玩家 HUD（表现层）============
// 职责：订阅玩家身上"已经广播出来的事件"，把结果画到屏幕上
// 铁律1 的直接体现：HUD 完全不碰战斗逻辑，只"听"事件 —— 联网时这份代码一行都不用改
// 挂载：挂在玩家预制体里的 Canvas 上
public class HUDManager : MonoBehaviour
{
    [Header("血条 / 护甲条（Image，Type = Filled）")]
    [SerializeField] private Image healthFill;
    [SerializeField] private Image armorFill;

    [Header("弹药")]
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private string reloadingLabel = "换弹中…";

    [Header("准星")]
    [SerializeField] private Image crosshair;
    [SerializeField] private Color crosshairNormalColor = Color.white;
    [SerializeField] private Color crosshairHitColor = Color.red;
    [SerializeField] private float hitFlashTime = 0.15f;    // 命中闪红持续秒数

    [Header("击杀提示")]
    [SerializeField] private TMP_Text killFeedText;
    [SerializeField] private float killFeedShowTime = 3f;   // 显示 3 秒后淡出

    [Header("记分板（按住显示）")]
    [SerializeField] private GameObject scoreboardPanel;
    [SerializeField] private TMP_Text scoreboardText;
    [SerializeField] private KeyCode scoreboardKey = KeyCode.Tab;

    // ---- 本地战绩（Tab 记分板 + 【步骤1.5】结算面板共用的数据源）----
    private int myKills;
    private int myDeaths;

    // 【步骤1.5】只读出口：结算面板要显示"我的击杀/死亡"
    public int MyKills => myKills;
    public int MyDeaths => myDeaths;

    private Health health;              // 数据源1：血量
    private WeaponController weapons;   // 数据源2：武器

    private Coroutine hitFlashCoroutine;
    private Coroutine killFeedCoroutine;

    private void Awake()
    {
        // Canvas 挂在玩家预制体里 → 往上找就能拿到玩家的两个数据源
        health = GetComponentInParent<Health>();
        weapons = GetComponentInParent<WeaponController>();
    }

    private void OnEnable()
    {
        // 订阅事件（一定要在 OnDisable 里配对取消订阅）
        if (health != null)
        {
            health.OnHealthChanged += RefreshHealth;   // 血量/护甲变化
            health.OnDied += HandleSelfDied;           // 自己死亡
        }

        if (weapons != null)
        {
            weapons.OnAmmoChanged += RefreshAmmo;            // 弹药变化
            weapons.OnWeaponChanged += HandleWeaponChanged;  // 切枪
            weapons.OnReloadStart += HandleReloadStart;      // 开始换弹
            weapons.OnReloadEnd += RefreshAmmo;              // 换弹结束
            weapons.OnHitTarget += HandleHitTarget;          // 命中目标
            weapons.OnKilled += HandleKilled;                // 【新增】击杀（真正的击杀，不含打尸体）
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnHealthChanged -= RefreshHealth;
            health.OnDied -= HandleSelfDied;
        }

        if (weapons != null)
        {
            weapons.OnAmmoChanged -= RefreshAmmo;
            weapons.OnWeaponChanged -= HandleWeaponChanged;
            weapons.OnReloadStart -= HandleReloadStart;
            weapons.OnReloadEnd -= RefreshAmmo;
            weapons.OnHitTarget -= HandleHitTarget;
            weapons.OnKilled -= HandleKilled; // 【新增】
        }
    }

    private void Start()
    {
        // 初始化界面（这时候事件还没来过，先手动读一次当前值）
        RefreshHealth();
        RefreshAmmo();
        RefreshScoreboard();
        SetCrosshairColor(crosshairNormalColor);
        if (scoreboardPanel != null) scoreboardPanel.SetActive(false);
        if (killFeedText != null) SetAlpha(killFeedText, 0f);
    }

    private void Update()
    {
        // 按住 Tab 显示记分板，松开隐藏（只在状态变化时调用 SetActive）
        bool wantShow = Input.GetKey(scoreboardKey);
        if (scoreboardPanel != null && scoreboardPanel.activeSelf != wantShow)
            scoreboardPanel.SetActive(wantShow);
    }

    // ---------- 血条 / 护甲条 ----------
    private void RefreshHealth()
    {
        if (health == null) return;

        // fillAmount 取值 0~1，所以用"当前值 ÷ 最大值"
        if (healthFill != null)
            healthFill.fillAmount = health.MaxHp > 0 ? (float)health.CurrentHp / health.MaxHp : 0f;

        if (armorFill != null)
            armorFill.fillAmount = health.MaxArmor > 0 ? (float)health.CurrentArmor / health.MaxArmor : 0f;
    }

    // ---------- 弹药 ----------
    private void RefreshAmmo()
    {
        if (ammoText == null || weapons == null || weapons.CurrentWeapon == null) return;
        ammoText.text = $"{weapons.CurrentMag} / {weapons.CurrentReserve}";   // 例：30 / 90
    }

    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        RefreshAmmo();   // 切枪后刷新弹药显示（这行以后可以顺便换枪名/图标）
    }

    private void HandleReloadStart(float reloadTime)
    {
        if (ammoText != null) ammoText.text = reloadingLabel;   // 换弹期间显示"换弹中…"
    }

    // ---------- 准星 + 击杀判定 ----------
    private void HandleHitTarget(RaycastHit hit, Health target)
    {
        // 命中反馈：准星闪红（击杀判定已移到 HandleKilled）
        if (hitFlashCoroutine != null) StopCoroutine(hitFlashCoroutine);
        hitFlashCoroutine = StartCoroutine(FlashCrosshairRoutine());
    }

    // 只有真正把对方打死时才会进来（打尸体不会）
    private void HandleKilled(Health victim)
    {
        myKills++;
        ShowKillFeed($"你 击杀了 {victim.name}");
        RefreshScoreboard();
    }

    // ---------- 自己死亡 ----------
    private void HandleSelfDied(GameObject killer)
    {
        myDeaths++;
        string killerName = (killer != null) ? killer.name : "环境";   // 【改】空击杀者容错
        ShowKillFeed($"{killerName} 击杀了 你");
        RefreshScoreboard();
    }
    private IEnumerator FlashCrosshairRoutine()
    {
        SetCrosshairColor(crosshairHitColor);
        yield return new WaitForSeconds(hitFlashTime);   // 等一小会儿
        SetCrosshairColor(crosshairNormalColor);
    }

    private void SetCrosshairColor(Color c)
    {
        if (crosshair != null) crosshair.color = c;
    }

    


    // ---------- 击杀提示：显示后淡出 ----------
    private void ShowKillFeed(string message)
    {
        if (killFeedText == null) return;

        killFeedText.text = message;
        if (killFeedCoroutine != null) StopCoroutine(killFeedCoroutine);
        killFeedCoroutine = StartCoroutine(KillFeedRoutine());
    }

    private IEnumerator KillFeedRoutine()
    {
        SetAlpha(killFeedText, 1f);                              // 显示
        yield return new WaitForSeconds(killFeedShowTime);       // 显示 3 秒
        SetAlpha(killFeedText, 0f);                              // 隐藏（把文字透明度设为 0）
    }

    private void SetAlpha(TMP_Text text, float alpha)
    {
        Color c = text.color;
        c.a = alpha;             // 只改透明度，不动颜色
        text.color = c;
    }

    // ---------- 记分板 ----------
    private void RefreshScoreboard()
    {
        if (scoreboardText == null) return;
        scoreboardText.text = $"本地战绩\n击杀：{myKills}\n死亡：{myDeaths}";
    }
}