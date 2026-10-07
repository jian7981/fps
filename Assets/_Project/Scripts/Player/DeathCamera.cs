using System.Collections;
using UnityEngine;

// 死亡镜头：死亡后相机缓缓升到尸体上方俯视，复活时平滑回到第一人称
// 完全靠订阅事件驱动（铁律1），不碰任何战斗逻辑
// 挂载：玩家根物体（X Bot）上
public class DeathCamera : MonoBehaviour
{
    [Header("引用（留空则自动查找）")]
    [SerializeField] private Transform cameraPivot;          // 相机所在节点
    [SerializeField] private Health health;
    [SerializeField] private WeaponViewModel weaponViewModel;

    [Header("死亡镜头参数")]
    [SerializeField] private float riseHeight = 3.5f;        // 升到多高
    [SerializeField] private float backOffset = 1.5f;        // 同时往后拉多少
    [SerializeField] private float riseDuration = 2.5f;      // 上升过程时长
    [SerializeField] private float lookDownAngle = 55f;      // 低头俯视角

    private Vector3 firstPersonLocalPos;                     // 第一人称机位（要记住，复活回这里）
    private Vector3 firstPersonLocalEuler;
    private Coroutine riseCoroutine;

    private void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (weaponViewModel == null) weaponViewModel = GetComponentInChildren<WeaponViewModel>();

        if (cameraPivot == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraPivot = cam.transform;
        }

        // 【修复，2.2-A】机位记录改到 Awake：
        // Mirror 在"主机初次生成"时会把所有 SyncVar 的 hook 强制触发一遍（含 OnRespawned），
        // 这发生在 Start 之前——记录若留在 Start，相机会被提前的"复活事件"设成 (0,0,0)（卡在地里）。
        if (cameraPivot != null)
        {
            firstPersonLocalPos = cameraPivot.localPosition;
            firstPersonLocalEuler = cameraPivot.localEulerAngles;
        }
    

}

    

    private void OnEnable()
    {
        if (health != null)
        {
            health.OnDied += HandleDied;
            health.OnRespawned += HandleRespawned;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDied -= HandleDied;
            health.OnRespawned -= HandleRespawned;
        }
    }

    private void HandleDied(GameObject killer)
    {
        if (cameraPivot == null) return;

        // 死亡时机位：在第一人称基础上升高 + 后退
        Vector3 deathPos = firstPersonLocalPos + new Vector3(0f, riseHeight, -backOffset);
        Quaternion deathRot = Quaternion.Euler(lookDownAngle, 0f, 0f);

        if (weaponViewModel != null) weaponViewModel.SetModelsVisible(false);   // 死亡时收枪

        if (riseCoroutine != null) StopCoroutine(riseCoroutine);
        riseCoroutine = StartCoroutine(RiseRoutine(deathPos, deathRot));
    }

    private IEnumerator RiseRoutine(Vector3 targetPos, Quaternion targetRot)
    {
        Vector3 startPos = cameraPivot.localPosition;
        Quaternion startRot = cameraPivot.localRotation;
        float t = 0f;

        while (t < riseDuration)
        {
            t += Time.deltaTime;
            // SmoothStep：开头快、结尾慢，看起来更"缓缓升天"
            float k = Mathf.SmoothStep(0f, 1f, t / riseDuration);
            cameraPivot.localPosition = Vector3.Lerp(startPos, targetPos, k);
            cameraPivot.localRotation = Quaternion.Slerp(startRot, targetRot, k);
            yield return null;   // 等下一帧
        }

        riseCoroutine = null;
    }

    private void HandleRespawned()
    {
        if (riseCoroutine != null) { StopCoroutine(riseCoroutine); riseCoroutine = null; }
        if (cameraPivot == null) return;

        // 回到第一人称机位，并把枪恢复
        cameraPivot.localPosition = firstPersonLocalPos;
        cameraPivot.localRotation = Quaternion.Euler(firstPersonLocalEuler);
        if (weaponViewModel != null) weaponViewModel.SetModelsVisible(true);
    }
}