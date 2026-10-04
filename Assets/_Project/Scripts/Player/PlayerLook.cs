using UnityEngine;

public class PlayerLook : MonoBehaviour
{
    [Header("视角参数")]
    public Camera cam;
    public float mouseSensitivity = 200f;
    public float maxLookAngle = 85f;   // 【新增】上下视角上限（原来写死 50，抬头低头幅度太小）

    private float xRotation = 0f;
    private Health health;             // 【新增】

    void Awake()   // 【新增】先拿 Health，供 OnEnable 订阅使用
    {
        health = GetComponent<Health>();
    }

    void OnEnable()   // 【新增】复活时把视角角度复位
    {
        if (health != null) health.OnRespawned += ResetLook;
    }

    void OnDisable()
    {
        if (health != null) health.OnRespawned -= ResetLook;   // 【新增】配对取消订阅

        // 解锁鼠标（原样保留）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 【新增】复活后视角回到水平：不写的话会保持死亡瞬间低着/抬着头
    void ResetLook()
    {
        xRotation = 0f;
        if (cam != null) cam.transform.localRotation = Quaternion.identity;
    }

    void Start()
    {
        // 锁鼠标（原样保留）
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (cam != null)
            cam.nearClipPlane = 0.01f; // 防止贴墙时看到墙内部（透视）
    }

    void Update()
    {
        if (health != null && health.IsDead) return;   // 【新增】死亡时不能转视角
        if (cam == null) return;                       // 【新增】没拖相机时安全退出（原来是直接空引用崩溃）

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);   // 【改】改用可调参数

        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f); // 只旋转摄像机的X轴
        transform.Rotate(Vector3.up * mouseX);                             // 旋转玩家的Y轴
    }
}