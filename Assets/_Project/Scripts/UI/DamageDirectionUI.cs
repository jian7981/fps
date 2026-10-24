using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;   // Image
using Mirror;           // NetworkBehaviour（判"这是不是本机玩家自己"）

// 【阶段4-B2】受击方向指示（HUD）
// 形态：以屏幕中心（准星）为圆心的一圈"环形弧段" —— 被打时在"子弹来向"对应的角度亮起一段弧，随即淡出
//      （Overwatch / Valorant / Apex 那种；CoD 那种"中心附近一个箭头"是同一套算法换贴图）
// 方向算法：命中那一刻取攻击者世界坐标 → 与**相机的水平朝向**做 SignedAngle →
//      正前方 = 弧在正上方、右侧 = 右侧、背后 = 正下方（一圈 360° 连续，没有"前/后"盲区）
// 挂载：玩家 Canvas（和 HUDManager 同一物体）
//
// 屏幕上不用你搭任何东西：居中容器与 N 段弧都是运行时创建的，弧线贴图也是脚本现画的（Texture2D 上画弧）
// ⚠ 形状参数（radius / thickness / arcDegrees）是在 Awake 里"按当时的值现画贴图"的 →
//   改完要重新进 Play 才生效（颜色 / 存活时间 / 段数可以随时改，立即生效）
public class DamageDirectionUI : MonoBehaviour
{
    [Header("引用（留空自动找）")]
    [Tooltip("方向参考用的相机：留空则从自己的层级往上找（玩家的相机就在 Canvas 上方）")]
    [SerializeField] private Camera viewCamera;
    [Tooltip("弧线贴图：留空 = 用脚本运行时生成的弧形贴图；想自己画一张就拖上来")]
    [SerializeField] private Sprite sprite;

    [Header("形状（像素）")]
    [Tooltip("弧到屏幕中心的距离")]
    [SerializeField] private float radius = 110f;
    [Tooltip("弧的粗细")]
    [SerializeField] private float thickness = 14f;
    [Tooltip("每段弧的张角（度）")]
    [SerializeField] private float arcDegrees = 55f;
    [Tooltip("贴图分辨率：越高越细腻（形状固定，实际尺寸由 radius 拉伸决定）")]
    [SerializeField] private int textureResolution = 256;

    [Header("表现")]
    [SerializeField] private Color color = new Color(1f, 0.25f, 0.2f, 1f);   // 橙红：明显但不刺眼
    [Tooltip("每段弧存活秒数（到点自动淡出消失）")]
    [SerializeField] private float lifetime = 1.5f;
    [Tooltip("淡入时长（很短：挨打要立刻看到）")]
    [SerializeField] private float fadeInTime = 0.06f;
    [Tooltip("同时最多几段（被两个方向夹击时能同时看到两段）")]
    [SerializeField] private int maxIndicators = 8;

    // 一段弧 = 一张图 + 它的出生时刻
    private class Indicator
    {
        public Image image;
        public float bornTime;
        public bool active;
    }

    private Health health;
    private NetworkBehaviour netBehaviour;          // 判"这是不是本机玩家自己"
    private RectTransform root;                     // 居中的容器（所有弧都是它的子物体）
    private readonly List<Indicator> indicators = new List<Indicator>();

    private void Awake()
    {
        health = GetComponentInParent<Health>();
        netBehaviour = GetComponentInParent<NetworkBehaviour>();

        if (viewCamera == null) viewCamera = GetComponentInParent<Camera>();
        if (viewCamera == null) viewCamera = Camera.main;

        BuildContainer();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.OnDamageFrom += HandleDamageFrom;
            health.OnDied += HandleDied;
            health.OnRespawned += ClearAll;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDamageFrom -= HandleDamageFrom;   // 配对取消订阅
            health.OnDied -= HandleDied;
            health.OnRespawned -= ClearAll;
        }
        ClearAll();
    }

    // ---------- 屏幕上的容器 + 弧段池（全部运行时创建）----------
    private void BuildContainer()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[受击方向] 没找到 Canvas —— 本组件要挂在玩家的 HUD Canvas 上", this);
            return;
        }

        if (sprite == null) sprite = BuildArcSprite();

        GameObject go = new GameObject("DamageDirectionRoot", typeof(RectTransform));
        root = go.GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        // 锚在屏幕正中（= 准星位置），sizeDelta 为 0：子物体用自己的 anchoredPosition 摆位
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = Vector2.zero;

        // 预先建好 maxIndicators 张弧（之后反复复用，不再 Instantiate）
        for (int i = 0; i < maxIndicators; i++)
        {
            GameObject arc = new GameObject("Arc" + i, typeof(RectTransform), typeof(Image));
            RectTransform rt = arc.GetComponent<RectTransform>();
            rt.SetParent(root, false);
            // 轴心放"底边中点"= 圆心：旋转这个元素时，它就沿着那圈圆环滑动
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(radius * 2f, radius);

            Image img = arc.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;   // 不挡 UI 点击
            img.color = color;

            arc.SetActive(false);
            Indicator ind = new Indicator();
            ind.image = img;
            ind.active = false;
            indicators.Add(ind);
        }
    }

    // ---------- 挨打 → 画一段弧 ----------
    private void HandleDamageFrom(Vector3 sourceWorldPos)
    {
        if (root == null || viewCamera == null) return;
        if (!NetUtil.IsLocalControl(netBehaviour)) return;   // 只有本机玩家挨打才画（别人挨打不该出现在我屏幕上）

        Vector3 dir = sourceWorldPos - viewCamera.transform.position;
        dir.y = 0f;                                          // 只取水平方向：不因敌人高低而上下漂
        Vector3 forward = viewCamera.transform.forward;
        forward.y = 0f;
        if (dir.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.01f) return;

        // 右为正；UI 的 z 旋转是逆时针为正 → 取负号，使"右侧来的人"那段弧出现在屏幕右侧
        float angle = Vector3.SignedAngle(forward, dir.normalized, Vector3.up);

        Indicator ind = TakeIndicator();
        if (ind == null) return;

        ind.image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle);
        Color c = color;
        c.a = 0f;                    // 从全透明开始（Update 里淡入）
        ind.image.color = c;
        ind.image.gameObject.SetActive(true);
        ind.bornTime = Time.unscaledTime;   // 用不受 timeScale 影响的时间：对局结束冻结时也能正常淡出
        ind.active = true;
    }

    // 取一个可用的弧：优先没在用的；都满了就顶掉最早的那段
    private Indicator TakeIndicator()
    {
        Indicator oldest = null;
        for (int i = 0; i < indicators.Count; i++)
        {
            if (!indicators[i].active) return indicators[i];
            if (oldest == null || indicators[i].bornTime < oldest.bornTime) oldest = indicators[i];
        }
        return oldest;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < indicators.Count; i++)
        {
            Indicator ind = indicators[i];
            if (!ind.active) continue;

            float t = now - ind.bornTime;
            if (t >= lifetime)
            {
                ind.active = false;
                ind.image.gameObject.SetActive(false);
                continue;
            }

            float alpha = (t < fadeInTime)
                ? Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeInTime))                     // 淡入
                : Mathf.Clamp01(1f - (t - fadeInTime) / Mathf.Max(0.01f, lifetime - fadeInTime));   // 淡出
            Color c = color;
            c.a = color.a * alpha;
            ind.image.color = c;
        }
    }

    private void HandleDied(GameObject killer) => ClearAll();

    private void ClearAll()
    {
        for (int i = 0; i < indicators.Count; i++)
        {
            indicators[i].active = false;
            if (indicators[i].image != null) indicators[i].image.gameObject.SetActive(false);
        }
    }

    // ---------- 运行时生成"环形弧段"贴图 ----------
    // 画法：把贴图当成"以底边中点为圆心"的上半圆 —— 距离在 [内半径, 外半径]、角度在 ±张角/2 之内的像素涂白，
    // 径向与角向都做柔化（避免锯齿和生硬的端头）。贴图会被 RectTransform 拉到 (2×radius) × radius → 形状比例天然对上
    private Sprite BuildArcSprite()
    {
        int res = Mathf.Max(64, textureResolution);
        int w = res;
        int h = Mathf.Max(32, res / 2);
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Vector2 center = new Vector2(w * 0.5f, 0f);          // 圆心 = 底边中点
        float outer = h;                                     // 外半径（归一化后 = 1）
        float innerRatio = Mathf.Clamp01((radius - thickness) / Mathf.Max(1f, radius));
        float half = arcDegrees * 0.5f;

        Color[] pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - center;
                float d = p.magnitude / outer;                       // 0（圆心）→ 1（外缘）
                float ang = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;   // 以"正上方"为 0，右为正

                float a = 0f;
                if (d >= innerRatio && d <= 1f && Mathf.Abs(ang) <= half)
                {
                    float radial = Mathf.Min(d - innerRatio, 1f - d) * outer * 0.5f;   // 两侧各柔化约 2 像素
                    float angular = (half - Mathf.Abs(ang)) * 0.6f;                    // 端头柔化
                    a = Mathf.Clamp01(Mathf.Min(Mathf.Min(radial, angular), 1f));
                }
                pixels[y * w + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0f), 100f);
    }
}
