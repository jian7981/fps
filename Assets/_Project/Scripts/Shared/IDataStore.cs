namespace Shared
{
    // ============ 存档接口（所有"存/读数据"的统一出口）============
    // 铁律：业务代码不许直接写 PlayerPrefs.GetXXX，一律走这个接口
    // 现在：PlayerPrefsDataStore（本地存档）
    // 阶段3：MySqlDataStore（服务器存档）—— 调用方一行不用改
    public interface IDataStore
    {
        // ---- 整数 ----
        void SetInt(string key, int value);                 // 存一个整数
        int GetInt(string key, int defaultValue);           // 读一个整数（没存过就返回默认值）

        // ---- 小数（灵敏度和音量都是小数，所以接口里要有）----
        void SetFloat(string key, float value);             // 存一个小数
        float GetFloat(string key, float defaultValue);     // 读一个小数

        // ---- 字符串 ----
        void SetString(string key, string value);           // 存一个字符串
        string GetString(string key, string defaultValue);  // 读一个字符串

        // ---- 落盘 ----
        void Save();                                        // 立刻写进磁盘（不调的话要等程序正常退出才保存）
    }
}