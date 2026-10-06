namespace Shared
{
    // ============ 数据门面（全局唯一入口）============
    // 业务代码只认 DataManager.Store，拿到的永远是 IDataStore
    // 阶段3换服务器存档：只把下面这一个 new 换成 new MySqlDataStore()，调用方零改动
    public static class DataManager
    {
        // 全局唯一的一份存档实例（static 只有这一处，存的是设置不是对局状态，不违反铁律4）
        private static readonly IDataStore store = new PlayerPrefsDataStore();

        // 对外的只读出口
        public static IDataStore Store => store;
    }
}