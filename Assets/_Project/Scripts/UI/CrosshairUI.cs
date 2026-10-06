using System.Collections;      // 协程
using UnityEngine;
using UnityEngine.UI;          // Image

// ============ 动态准星（把"散布"画出来）【新增，1.7d】============
// 腰射准星：4 条短线组成十字，散布越大线离中心越远（开枪张开、停火收拢），中心可配一个小点
// 开镜准星：一个独立的物体（比如一个小点/细十字），开镜时显示、收镜时隐藏
// 命中敌人：整体闪红；开镜/收镜：两套准星互换
// 挂载：挂玩家 Canvas 上（和 HUDManager 同一个物体）
public class CrosshairUI : MonoBehaviour
{
    [Header("引用")]
    public GameObject crosshairRoot;      // 腰射准星容器（4 条线的父物体，开镜时整体隐藏它）
    public RectTransform lineUp;          // 上边那条线
    public RectTransform lineDown;        // 下边
    public RectTransform lineLeft;        // 左边
    public RectTransform lineRight;       // 右边
    public Image centerDot;               // 腰射准星的中心点（可选；拖上后一起闪红）
    public GameObject aimCrosshair;       // 【新增】开镜准星（开镜时显示它；做一个点或细十字的物体拖进来）

    [Header("间隙参数（像素）")]
    public float gapBase = 12f;           // 基础间隙：散布 = 0 时，线离中心多远
    public float gapPerDegree = 10f;      // 每 1 度散布 → 间隙增加多少像素

    [Header("命中反馈")]
    public Color normalColor = Color.white;   // 平时颜色
    public Color hitColor = Color.red;        // 命中敌人时的颜色
    public float hitFlashTime = 0.15f;        // 闪红持续秒数

    private WeaponController weapons;     // 数据源（往上找玩家身上的武器控制器）
    private Image[] lines;                // 4 条线的 Image（换颜色用）
    private Coroutine flashCoroutine;     // 闪红协程句柄

    private void Awake()
    {
        weapons = GetComponentInParent<WeaponController>();   // 和 HUDManager 一样的拿法
        lines = new Image[]
        {
            lineUp.GetComponent<Image>(), lineDown.GetComponent<Image>(),
            lineLeft.GetComponent<Image>(), lineRight.GetComponent<Image>()
        };
    }

    private void OnEnable()
    {
        if (weapons == null) return;
        weapons.OnHitTarget += HandleHitTarget;     // 命中敌人 → 闪红
        weapons.OnAimChanged += HandleAimChanged;   // 开镜变化 → 两套准星互换
    }

    private void OnDisable()
    {
        if (weapons == null) return;
        weapons.OnHitTarget -= HandleHitTarget;     // 配对取消订阅
        weapons.OnAimChanged -= HandleAimChanged;
    }

    private void Start()
    {
        // 【修复】强制把 4 条线的锚点/轴心设为中心 —— 锚点被误改会让准星线跑偏/飞出屏幕
        SetCenterAnchor(lineUp);
        SetCenterAnchor(lineDown);
        SetCenterAnchor(lineLeft);
        SetCenterAnchor(lineRight);

        // 【防御】aimCrosshair 槽如果被误拖成"准星线本身"，会在开镜时把它隐藏掉 → 表现为某条线消失
        if (aimCrosshair != null && (aimCrosshair == lineUp.gameObject || aimCrosshair == lineDown.gameObject
                                     || aimCrosshair == lineLeft.gameObject || aimCrosshair == lineRight.gameObject))
        {
            Debug.LogWarning("CrosshairUI：Aim Crosshair 槽拖成了准星线本身（会导致那条线消失），已自动忽略；请改成独立的开镜准星物体");
            aimCrosshair = null;
        }

        SetColor(normalColor);       // 初始白色
        ApplyGap(gapBase);           // 初始摆好位置
        if (aimCrosshair != null) aimCrosshair.SetActive(false);   // 开镜准星初始隐藏
    }

    // 【修复】把一条准星线的锚点和轴心强制设为"中心"（位置由脚本接管，锚点必须是中心）
    private void SetCenterAnchor(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private void Update()
    {
        if (weapons == null) return;
        // 散布 → 间隙：开枪后 CurrentSpread 变大 → 准星张开；停火后自动收拢
        ApplyGap(gapBase + weapons.CurrentSpread * gapPerDegree);
    }

    // 按间隙摆放 4 条线（只改 anchoredPosition，不触发布局重建，零开销）
    private void ApplyGap(float gap)
    {
        lineUp.anchoredPosition = new Vector2(0f, gap);
        lineDown.anchoredPosition = new Vector2(0f, -gap);
        lineLeft.anchoredPosition = new Vector2(-gap, 0f);
        lineRight.anchoredPosition = new Vector2(gap, 0f);
    }

    // 开镜/收镜：腰射准星 与 开镜准星 互换显示
    private void HandleAimChanged(bool aiming)
    {
        if (crosshairRoot != null) crosshairRoot.SetActive(!aiming);   // 开镜 → 藏腰射准星
        if (aimCrosshair != null) aimCrosshair.SetActive(aiming);      // 开镜 → 显示开镜准星
    }

    // 命中敌人：闪一下红
    private void HandleHitTarget(RaycastHit hit, Health target)
    {
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        SetColor(hitColor);
        yield return new WaitForSeconds(hitFlashTime);
        SetColor(normalColor);
    }

    private void SetColor(Color c)
    {
        if (lines != null)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                lines[i].color = c;
            }
        }
        if (centerDot != null) centerDot.color = c;   // 中心点一起变色
    }
}