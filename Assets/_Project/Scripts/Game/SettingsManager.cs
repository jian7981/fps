using UnityEngine;   // AudioListener / Mathf / RuntimeInitializeOnLoadMethod
using Shared;        // DataManager

// ============ 全局设置（鼠标灵敏度 / 主音量）============
// 静态类：设置是全局唯一的数据，不用挂场景物体
// 存档统一走 DataManager（IDataStore），阶段3换服务器存档后这里也不用改
public static class SettingsManager
{
    // ---- 存档键名（统一放这里，避免各处手拼字符串拼错）----
    private const string KeyMouseSensitivity = "settings_mouse_sensitivity";
    private const string KeyMasterVolume = "settings_master_volume";

    // ---- 默认值（第一次进游戏、还没存过档时用）----
    public const float DefaultMouseSensitivity = 200f;   // 和 PlayerLook 原来的默认值保持一致
    public const float DefaultVolume = 0.8f;

    // ---- 鼠标灵敏度的可调范围（设置界面的滑条也用它俩）----
    public const float MinMouseSensitivity = 50f;
    public const float MaxMouseSensitivity = 500f;

    // ---- 鼠标灵敏度：读存档；写的时候夹到范围内并存好 ----
    public static float MouseSensitivity
    {
        get { return DataManager.Store.GetFloat(KeyMouseSensitivity, DefaultMouseSensitivity); }
        set
        {
            float clamped = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);   // 防止超出范围
            DataManager.Store.SetFloat(KeyMouseSensitivity, clamped);                       // 存起来（只在内存，见 SaveAll）
        }
    }

    // ---- 主音量：读存档；写的时候夹到 0~1 并立刻应用 ----
    public static float MasterVolume
    {
        get { return DataManager.Store.GetFloat(KeyMasterVolume, DefaultVolume); }
        set
        {
            float clamped = Mathf.Clamp01(value);        // 音量范围就是 0~1
            DataManager.Store.SetFloat(KeyMasterVolume, clamped);
            AudioListener.volume = clamped;              // 拖滑条那一刻就能听出变化
        }
    }

    // 写盘：拖动滑条时会每帧变化，每帧写盘太浪费，统一等"离开设置界面"时调一次
    public static void SaveAll()
    {
        DataManager.Store.Save();
    }

    // 游戏启动时自动跑一次（不用等玩家打开设置界面）：先把存档里的音量应用上
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedVolumeOnBoot()
    {
        AudioListener.volume = MasterVolume;             // 音量是全局静态值，这里设一次全程有效
        Debug.Log("[设置] 启动读取存档：音量 " + MasterVolume + "｜鼠标灵敏度 " + MouseSensitivity);
    }
}