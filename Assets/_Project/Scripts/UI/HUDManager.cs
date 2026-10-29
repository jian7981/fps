using System;                  // [Serializable]
using System.Collections;      // 协程
using System.Collections.Generic; // List<>
using UnityEngine;
using UnityEngine.UI;          // Image
using TMPro;                   // TextMeshProUGUI

// ============ 玩家 HUD（表现层）============
// 职责：订阅玩家身上"已经广播出来的事件"，把结果画到屏幕上
// 铁律1 的直接体现：HUD 完全不碰战斗逻辑，只"听"事件 —— 联网时这份代码一行都不用改
// 挂载：挂在玩家预制体里的 Canvas 上
public class HUDManager : MonoBehaviour
{
    // 【7.3】武器槽位（最多 3 个；数据来自 WeaponController 的武器列表）
    [Serializable]
    public class WeaponSlotUI
    {
        public GameObject root;       // 整个槽位（没枪时隐藏）
        public Image icon;            // icon_rifle / icon_sniper
        public TMP_Text keyText;      // 1 / 2 / 3
        public TMP_Text nameText;     // 武器名（可选）
        public GameObject activeFrame; // hud_weapon_slot_active
    }

    [Header("血条 / 护甲条（Image，Type = Filled）")]
    [SerializeField] private Image healthFill;
    [SerializeField] private Image armorFill;
    [SerializeField] private TMP_Text healthText;   // 【7.3】"100 / 100"
    [SerializeField] private TMP_Text armorText;    // 【7.3】"50 / 50"

    [Header("弹药")]
    [SerializeField] private TMP_Text ammoText;          // 旧版合并显示 "30 / 90"（可留空）
    [SerializeField] private TMP_Text ammoMagText;       // 【7.3】弹匣：30
    [SerializeField] private TMP_Text ammoReserveText;   // 【7.3】备弹：90
    [SerializeField] private TMP_Text weaponNameText;    // 【7.3】当前武器名
    [SerializeField] private Image reloadFill;           // 【7.3】换弹进度条（Filled）
    [SerializeField] private string reloadingLabel = "换弹中…";

    [Header("武器槽位（可选，最多拖 3 个）")]
    [SerializeField] private WeaponSlotUI[] weaponSlots;
    [SerializeField] private Sprite rifleIcon;    // icon_rifle
    [SerializeField] private Sprite sniperIcon;   // icon_sniper

    [Header("准星")]
    [SerializeField] private Image crosshair;
    [SerializeField] private Color crosshairNormalColor = Color.white;
    [SerializeField] private Color crosshairHitColor = Color.red;
    [SerializeField] private float hitFlashTime = 0.15f;    // 命中闪红持续秒数

    [Header("命中标记（可选）")]
    [SerializeField] private Image hitMarker;             // 屏幕中心 hitmarker
    [SerializeField] private Sprite hitMarkerHitSprite;   // hitmarker_hit
    [SerializeField] private Sprite hitMarkerKillSprite;  // hitmarker_kill
    [SerializeField] private float hitMarkerShowTime = 0.2f;

    [Header("击杀提示")]
    [SerializeField] private TMP_Text killFeedText;       // 旧版单行（可留空）
    [SerializeField] private float killFeedShowTime = 3f; // 显示 3 秒后淡出
    [SerializeField] private RectTransform killFeedContent;    // 【7.3】击杀列表容器（可选）
    [SerializeField] private GameObject killFeedRowPrefab;     // 【7.3】KillFeedRow.prefab（可选）
    [SerializeField] private float killFeedRowLifetime = 4f;   // 每行停留秒数

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
    private Coroutine reloadFillCoroutine;
    private Coroutine hitMarkerCoroutine;
    private readonly List<GameObject> killFeedRows = new List<GameObject>();

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
            weapons.OnReloadEnd += HandleReloadEnd;          // 换弹结束
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
            weapons.OnReloadEnd -= HandleReloadEnd;
            weapons.OnHitTarget -= HandleHitTarget;
            weapons.OnKilled -= HandleKilled; // 【新增】
        }

        if (hitMarkerCoroutine != null)
        {
            StopCoroutine(hitMarkerCoroutine);
            hitMarkerCoroutine = null;
        }
        if (hitMarker != null) hitMarker.gameObject.SetActive(false);
    }

    private void Start()
    {
        // 初始化界面（这时候事件还没来过，先手动读一次当前值）
        RefreshHealth();
        RefreshAmmo();
        RefreshWeaponSlots(weapons != null ? weapons.CurrentWeaponIndex : 0);
        RefreshScoreboard();
        SetCrosshairColor(crosshairNormalColor);
        if (reloadFill != null) reloadFill.fillAmount = 1f;
        if (hitMarker != null) hitMarker.gameObject.SetActive(false);
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

        // 【7.3】文字数值
        if (healthText != null) healthText.text = health.CurrentHp + " / " + health.MaxHp;
        if (armorText != null) armorText.text = health.CurrentArmor + " / " + health.MaxArmor;
    }

    // ---------- 弹药 ----------
    private void RefreshAmmo()
    {
        if (weapons == null || weapons.CurrentWeapon == null) return;

        if (ammoText != null)
            ammoText.text = $"{weapons.CurrentMag} / {weapons.CurrentReserve}";   // 旧版合并显示
        if (ammoMagText != null) ammoMagText.text = weapons.CurrentMag.ToString();
        if (ammoReserveText != null) ammoReserveText.text = weapons.CurrentReserve.ToString();
        if (weaponNameText != null) weaponNameText.text = weapons.CurrentWeapon.weaponName;
    }

    private void HandleWeaponChanged(int index, WeaponData weapon)
    {
        RefreshAmmo();   // 切枪后刷新弹药显示
        RefreshWeaponSlots(index);
    }

    // 【7.3】刷新武器槽位：有几把显示几个，当前那把点亮 activeFrame
    private void RefreshWeaponSlots(int currentIndex)
    {
        if (weaponSlots == null || weapons == null) return;

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            WeaponSlotUI slot = weaponSlots[i];
            if (slot == null) continue;

            WeaponData data = weapons.GetWeapon(i);
            bool hasWeapon = data != null;
            if (slot.root != null) slot.root.SetActive(hasWeapon);
            if (!hasWeapon) continue;

            if (slot.keyText != null) slot.keyText.text = (i + 1).ToString();
            if (slot.nameText != null) slot.nameText.text = data.weaponName;
            if (slot.icon != null && rifleIcon != null && sniperIcon != null)
                slot.icon.sprite = (data.weaponType == WeaponType.Sniper) ? sniperIcon : rifleIcon;
            if (slot.activeFrame != null) slot.activeFrame.SetActive(i == currentIndex);
        }
    }

    private void HandleReloadStart(float reloadTime)
    {
        if (ammoText != null) ammoText.text = reloadingLabel;   // 换弹期间显示"换弹中…"

        if (reloadFill != null)
        {
            if (reloadFillCoroutine != null) StopCoroutine(reloadFillCoroutine);
            reloadFill.fillAmount = 0f;
            reloadFillCoroutine = StartCoroutine(ReloadFillRoutine(reloadTime));
        }
    }

    private void HandleReloadEnd()
    {
        if (reloadFillCoroutine != null)
        {
            StopCoroutine(reloadFillCoroutine);
            reloadFillCoroutine = null;
        }
        if (reloadFill != null) reloadFill.fillAmount = 1f;
        RefreshAmmo();
    }

    private IEnumerator ReloadFillRoutine(float duration)
    {
        if (reloadFill == null || duration <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            reloadFill.fillAmount = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        reloadFill.fillAmount = 1f;
        reloadFillCoroutine = null;
    }

    // ---------- 准星 + 击杀判定 ----------
    private void HandleHitTarget(RaycastHit hit, Health target)
    {
        // 命中反馈：准星闪红（击杀判定已移到 HandleKilled）
        if (hitFlashCoroutine != null) StopCoroutine(hitFlashCoroutine);
        hitFlashCoroutine = StartCoroutine(FlashCrosshairRoutine());
        ShowHitMarker(false);
    }

    // 只有真正把对方打死时才会进来（打尸体不会）
    private void HandleKilled(Health victim)
    {
        myKills++;
        ShowKillFeed($"你 击杀了 {victim.name}");
        RefreshScoreboard();
        ShowHitMarker(true);
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

    private void ShowHitMarker(bool kill)
    {
        if (hitMarker == null) return;

        if (hitMarkerHitSprite != null && hitMarkerKillSprite != null)
            hitMarker.sprite = kill ? hitMarkerKillSprite : hitMarkerHitSprite;

        hitMarker.gameObject.SetActive(true);
        if (hitMarkerCoroutine != null) StopCoroutine(hitMarkerCoroutine);
        hitMarkerCoroutine = StartCoroutine(HideHitMarkerRoutine());
    }

    private IEnumerator HideHitMarkerRoutine()
    {
        yield return new WaitForSeconds(hitMarkerShowTime);
        if (hitMarker != null) hitMarker.gameObject.SetActive(false);
        hitMarkerCoroutine = null;
    }

    private void SetCrosshairColor(Color c)
    {
        if (crosshair != null) crosshair.color = c;
    }

    


    // ---------- 击杀提示：显示后淡出 ----------
    private void ShowKillFeed(string message)
    {
        // 【7.3】优先用"行预制体 + Content"的列表；没配才退回旧版单行文字
        if (killFeedContent != null && killFeedRowPrefab != null)
        {
            GameObject row = Instantiate(killFeedRowPrefab, killFeedContent, false);
            TMP_Text rowText = row.GetComponentInChildren<TMP_Text>(true);
            if (rowText != null) rowText.text = message;

            row.transform.SetAsFirstSibling();   // 新消息放最上面
            killFeedRows.Add(row);

            while (killFeedRows.Count > 5)
            {
                GameObject oldest = killFeedRows[0];
                killFeedRows.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }

            StartCoroutine(RemoveKillFeedRow(row));
            return;
        }

        if (killFeedText == null) return;

        killFeedText.text = message;
        if (killFeedCoroutine != null) StopCoroutine(killFeedCoroutine);
        killFeedCoroutine = StartCoroutine(KillFeedRoutine());
    }

    private IEnumerator RemoveKillFeedRow(GameObject row)
    {
        yield return new WaitForSeconds(killFeedRowLifetime);
        if (row == null) yield break;

        killFeedRows.Remove(row);
        Destroy(row);
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