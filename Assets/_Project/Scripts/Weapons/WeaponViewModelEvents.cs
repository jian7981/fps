using UnityEngine;

// 【阶段4-D1】第一人称 view model 的动画事件接收器
// 背景：借用的框架武器 fbx 里，动画剪辑自带了 AnimationEvent「ShakeCameras(float)」——
//   步枪：Reload / Reload Aimed / Take；狙击：Reload / Recharge / Take
//   它原本由框架**自己的** CameraShaker 脚本接收。我们没搬那个脚本，于是 Console 一直刷：
//   "AnimationEvent 'ShakeCameras' on animation '...' has no receiver!"（动画本身不受影响）
// 做法：把本脚本挂在【带 Animator 的那个 view model 根物体】上（AnimationEvent 只发给带 Animator 的物体），
//   把事件转交给【我们自己】的相机抖动 CameraShaker（阶段4-B 的多抖源叠加）：
//     ① 消掉报错  ② 换弹 / 取枪时镜头真的抖一下 —— 这本来就是那条事件的意图
// 挂载：VM_AR / VM_SN 的根物体（也就是拖进来的 fbx 根，与 Animator 同一个物体）
public class WeaponViewModelEvents : MonoBehaviour
{
    [Header("动画事件抖动")]
    [Tooltip("事件里的强度按这个倍率缩放：嫌换弹/取枪抖得烦就调小，0 = 完全不动（但事件仍被接收、不再报错）")]
    [SerializeField] private float strengthScale = 1f;

    // 我们的相机抖动挂在相机上；view model 是相机的后代 → 往上找
    private CameraShaker cameraShaker;

    private void Awake()
    {
        cameraShaker = GetComponentInParent<CameraShaker>();
    }

    // ★ 动画事件回调：方法名与参数类型必须和剪辑里的事件**完全一致**（float），否则等于没接上
    // 参数 = 框架原本给的强度（0.2 ~ 0.3）
    public void ShakeCameras(float strength)
    {
        if (cameraShaker == null) return;      // 没有抖动组件（远程玩家 / 非玩家相机）→ 静默忽略
        float s = strength * strengthScale;
        if (s <= 0f) return;
        cameraShaker.Shake(s, 0.22f, 22f);     // 时长 / 粗糙度给个手感默认值
    }
}
