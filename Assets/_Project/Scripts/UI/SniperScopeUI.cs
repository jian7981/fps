using UnityEngine;
using UnityEngine.UI;          // Image / Sprite

// ============ 狙击镜筒 UI【新增，1.7d】============
// 效果：狙击枪开镜时，屏幕切换成"瞄准镜"：四周黑边 + 中央透明圆孔 + 十字准线
// 贴图是"程序生成"的 —— 不需要任何美术素材；圆孔大小、十字粗细都在 Inspector 里调
// 挂载：挂玩家 Canvas 上（和 HUDManager 同一个物体）；把全屏镜筒 Image 拖到 scopeImage
public class SniperScopeUI : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] private Image scopeImage;      // 镜筒图（一个全屏 Image；贴图由本脚本生成并替换）

    [Header("镜筒参数")]
    [SerializeField] private int textureSize = 512;           // 生成贴图的分辨率（够用即可，越大越清晰）
    [Range(0.2f, 0.48f)]
    [SerializeField] private float holeRadiusRatio = 0.42f;   // 圆孔半径占屏幕比例（越大视野越宽）
    [SerializeField] private float crosshairThickness = 2f;   // 十字线宽度（贴图像素）

    private WeaponController weapons;   // 武器控制器（判断"是不是狙击 + 是否开镜"）

    private void Awake()
    {
        weapons = GetComponentInParent<WeaponController>();

        if (scopeImage == null)
        {
            Debug.LogError("SniperScopeUI：scopeImage 未赋值（把全屏镜筒 Image 拖进来）");
            return;
        }

        scopeImage.sprite = GenerateScopeSprite();   // 生成镜筒贴图（中央透明圆 + 黑边 + 十字）
        FitScopeToScreen();                          // 保持正方形并盖满屏幕（防止宽屏下圆孔被拉成椭圆）
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

    // 开镜变化：只有"狙击枪开镜"才显示镜筒（步枪走自己的开镜表现）
    private void HandleAimChanged(bool aiming)
    {
        if (scopeImage == null) return;
        bool isSniper = weapons != null && weapons.CurrentWeapon != null
                        && weapons.CurrentWeapon.weaponType == WeaponType.Sniper;
        scopeImage.gameObject.SetActive(aiming && isSniper);
    }

    // 让镜筒图保持"正方形 + 盖满屏幕"：正方形才能保证圆孔不被宽屏拉成椭圆
    private void FitScopeToScreen()
    {
        RectTransform rt = scopeImage.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);      // 居中锚点
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;

        float side = Mathf.Max(Screen.width, Screen.height) * 1.15f;   // 边长取屏幕长边再放大一点，保证四角盖住
        rt.sizeDelta = new Vector2(side, side);
    }

    // 程序生成镜筒贴图：中央透明圆孔 + 圆孔外全黑 + 圆孔内画细十字线
    // 用 Color32 数组一次性写入（比逐像素 SetPixel 快很多）
    private Sprite GenerateScopeSprite()
    {
        int size = Mathf.Max(64, textureSize);                   // 分辨率保护，防止手填太小
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];

        float center = size * 0.5f;                              // 贴图中心
        float holeRadius = size * holeRadiusRatio;               // 圆孔半径（像素）
        float halfLine = Mathf.Max(0.5f, crosshairThickness * 0.5f);   // 十字线半宽

        Color32 black = new Color32(0, 0, 0, 255);               // 不透明黑
        Color32 clear = new Color32(0, 0, 0, 0);                 // 全透明

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;                           // 相对中心的横向距离
                float dy = y - center;                           // 纵向距离
                float dist = Mathf.Sqrt(dx * dx + dy * dy);      // 到中心的距离

                if (dist > holeRadius)
                    pixels[y * size + x] = black;                // 圆孔外：全黑（挡住画面）
                else if (Mathf.Abs(dx) <= halfLine || Mathf.Abs(dy) <= halfLine)
                    pixels[y * size + x] = black;                // 圆孔内：画十字线
                else
                    pixels[y * size + x] = clear;                // 其余：透明（能看见场景）
            }
        }

        tex.SetPixels32(pixels);   // 一次性写入全部像素
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }
}