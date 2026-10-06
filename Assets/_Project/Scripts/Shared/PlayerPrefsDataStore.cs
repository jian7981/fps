using UnityEngine;   // PlayerPrefs

namespace Shared
{
    // ============ 存档接口的本地实现：用 Unity 自带的 PlayerPrefs ============
    // 阶段3换服务器存档时，这个文件原样留着，只改 DataManager 里 new 的那一行
    public class PlayerPrefsDataStore : IDataStore
    {
        public void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);          // 写进内存
        }

        public int GetInt(string key, int defaultValue)
        {
            return PlayerPrefs.GetInt(key, defaultValue);   // 没存过返回默认值
        }

        public void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
        }

        public float GetFloat(string key, float defaultValue)
        {
            return PlayerPrefs.GetFloat(key, defaultValue);
        }

        public void SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
        }

        public string GetString(string key, string defaultValue)
        {
            return PlayerPrefs.GetString(key, defaultValue);
        }

        public void Save()
        {
            PlayerPrefs.Save();                      // 把内存里的值刷进磁盘
        }
    }
}