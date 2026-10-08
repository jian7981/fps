using UnityEngine;
using UnityEngine.UI;          // Image / Sprite

// ============ 狙击镜筒 UI【新增，1.7d】【修复：铺满问题】============
// 效果：狙击枪开镜时，屏幕切换成"瞄准镜"：四周黑边 + 中央透明圆孔 + 十字准线
// 贴图是"程序生成"的 —— 不需要任何美术素材；圆孔大小、十字粗细都在 Inspector 里调
// 挂载：挂玩家 Canvas 上（和 HUDManager 同一个物体）；把全屏镜筒 Image 拖到 scopeImage
//
// 【修复】之前"全屏时左右两边铺不满"的原因：镜筒图大小只在 Awake 里按当时的窗口尺寸
// 算了一次，之后切全屏/改分辨率不会更新；而且按"像素"算的尺寸遇到 Canvas 缩放会偏小。
// 现在改成：① 锚点拉伸铺满整个 Canvas（任何分辨率都不漏边）；
//          ② 贴图按屏幕宽高比生成矩形（圆孔天然是正圆，不会被拉扁）；
//          ③ 每次开镜、以及开镜期间分辨率变化时都会刷新。
public class SniperScopeUI : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Image scopeImage;      // 镜筒图（一个盖满 Canvas 的 Image；贴图由本脚本生成并替换）

    [Header("镜筒参数")]
    [SerializeField] private int textureSize = 512;           // 生成贴图的高度（够用即可，越大越清晰）
    [Range(0.2f, 1.2f)]
    [SerializeField] private float holeRadiusRatio = 0.55f;    // 圆孔半径 ÷ 屏幕高度（调大=镜筒视野越大）
    [SerializeField] private float crosshairThickness = 2f;   // 十字线宽度（贴图像素）

    private WeaponController weapons;   // 武器控制器（判断"是不是狙击 + 是否开镜"）
    private float lastAspect = -1f;     // 上次生成贴图时的屏幕宽高比（变了才重建）

    private void Awake()
    {
        weapons = GetComponentInParent<WeaponController>();

        if (scopeImage == null)
        {
            Debug.LogError("SniperScopeUI：scopeImage 未赋值（把全屏镜筒 Image 拖进来）");
            return;
        }

        RefreshScope();                              // 生成贴图 + 拉伸铺满
        scopeImage.gameObject.SetActive(false);      // 初始隐藏（狙击开镜时才显示）
    }

    private void OnEnable()
    {
        if (weapons == null) return;
        weapons.OnAimChanged += HandleAimChanged;    // 订阅开镜变化
    }

    private void OnDisable()
    {
        if (weapons == null) return;
        weapons.OnAimChanged -= HandleAimChanged;    // 配对取消订阅
    }

    private void Update()
    {
        // 开镜期间切全屏/改窗口：宽高比变了就重建一次，保证永远铺满
        if (scopeImage == null || !scopeImage.gameObject.activeSelf) return;
        if (Mathf.Abs((float)Screen.width / Screen.height - lastAspect) > 0.01f) RefreshScope();
    }

    // 开镜变化：只有"狙击枪开镜"才显示镜筒（步枪走自己的开镜表现）
    private void HandleAimChanged(bool aiming)
    {
        if (scopeImage == null) return;
        bool isSniper = weapons != null && weapons.CurrentWeapon != null
                        && weapons.CurrentWeapon.weaponType == WeaponType.Sniper;

        bool show = aiming && isSniper;
        if (show) RefreshScope();                    // 每次开镜前按当前分辨率刷新
        scopeImage.gameObject.SetActive(show);
    }

    // 刷新：① 拉伸铺满 Canvas；② 宽高比变了才重建贴图（避免每次开镜都重新生成）
    private void RefreshScope()
    {
        RectTransform rt = scopeImage.rectTransform;
        rt.anchorMin = Vector2.zero;        // 拉伸：四边贴住父级（父级就是玩家 Canvas 根，全屏）
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        float aspect = (Screen.height > 0) ? (float)Screen.width / Screen.height : 16f / 9f;
        if (Mathf.Abs(aspect - lastAspect) < 0.01f && scopeImage.sprite != null) return;   // 宽高比没变：不重建
        lastAspect = aspect;

        // 换新贴图前销毁旧的（只有分辨率变化才会走到这里，次数极少）
        if (scopeImage.sprite != null) { Destroy(scopeImage.sprite.texture); Destroy(scopeImage.sprite); }
        scopeImage.sprite = GenerateScopeSprite(aspect);
    }

    // 程序生成镜筒贴图：中央透明圆孔 + 圆孔外全黑 + 圆孔内画细十字线
    // 贴图按屏幕宽高比生成（高 = textureSize，宽 = 高 × 宽高比），再被拉伸铺满 → 圆孔永远是正圆
    // 用 Color32 数组一次性写入（比逐像素 SetPixel 快很多）
    private Sprite GenerateScopeSprite(float aspect)
    {
        int h = Mathf.Max(64, textureSize);                          // 贴图高度
        int w = Mathf.Max(64, Mathf.RoundToInt(h * aspect));         // 贴图宽度（跟随屏幕宽高比）

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[w * h];

        float centerX = w * 0.5f;                                    // 贴图中心 X
        float centerY = h * 0.5f;                                    // 贴图中心 Y
        float holeRadius = h * holeRadiusRatio;                      // 圆孔半径（相对屏幕高度）
        float halfLine = Mathf.Max(0.5f, crosshairThickness * 0.5f); // 十字线半宽

        Color32 black = new Color32(0, 0, 0, 255);                   // 不透明黑
        Color32 clear = new Color32(0, 0, 0, 0);                     // 全透明

        for (int y = 0; y < h; y++)
        {
            float dy = y - centerY;                                  // 相对中心的纵向距离
            for (int x = 0; x < w; x++)
            {
                float dx = x - centerX;                              // 相对中心的横向距离
                float dist = Mathf.Sqrt(dx * dx + dy * dy);          // 到中心的距离

                if (dist > holeRadius)
                    pixels[y * w + x] = black;                       // 圆孔外：全黑（挡住画面）
                else if (Mathf.Abs(dx) <= halfLine || Mathf.Abs(dy) <= halfLine)
                    pixels[y * w + x] = black;                       // 圆孔内：画十字线
                else
                    pixels[y * w + x] = clear;                       // 其余：透明（能看见场景）
            }
        }

        tex.SetPixels32(pixels);   // 一次性写入全部像素
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
    }
}