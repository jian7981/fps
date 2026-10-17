using System.Collections.Generic;   // List<>
using UnityEngine;

// 【阶段2.6】瞬时表现对象池
// 用途：弹孔、命中烟雾这一类"每次开火/命中都会生成、过一小段时间自己消失"的对象。
// 为什么池化（诚实动机，不是性能）：
//   ① 生命周期记账容易出错 —— 原来每发 Instantiate + Destroy(go, lifetime)，同时用列表做"上限 N"的记账；
//      到点被销毁后，列表里那条引用只是变成 null、仍然占着一个名额 → "上限 N"实际不按存活数生效，
//      还会误删还活着的对象（原代码里 `if (oldest != null)` 那种防御判断就是在补这个漏洞）。
//   ② 反复创建/销毁的开销集中在战斗高峰期，池化后实例只创建一次、之后全是复用。
// 语义：上限 = 池容量；池满时"覆盖最早到期的那个"（= 场上最旧的一个），保持原来"超出上限覆盖最旧"的行为。
// 用法（在挂载脚本里驱动）：
//   pool = new SimplePool(prefab, capacity, lifetime);   // 建池（Awake）
//   pool.Prewarm();                                     // 可选：预热，避免第一次用时才创建
//   GameObject go = pool.Get(pos, rot);                 // 取一个（自动摆位/激活/续期）
//   pool.ReleaseExpired();                              // 每帧调用：回收到期对象
// 不是 MonoBehaviour：纯数据管理，谁用它谁负责在 Update 里驱动回收（各端各自算，不走网络）。
public class SimplePool
{
    private struct Entry
    {
        public GameObject go;      // 池中的实例
        public float expireTime;   // 在用的到期时刻；<= 0 表示空闲（可复用）
    }

    private readonly GameObject prefab;    // 模板（为空时 Get 直接返回 null，等同于"没配就不生成"）
    private readonly int capacity;         // 容量 = 场上同时存在的上限
    private readonly float lifetime;       // 单个实例存活秒数
    private readonly List<Entry> entries = new List<Entry>();

    public SimplePool(GameObject prefab, int capacity, float lifetime)
    {
        this.prefab = prefab;
        this.capacity = Mathf.Max(1, capacity);
        this.lifetime = Mathf.Max(0.01f, lifetime);
    }

    public int AliveCount   // 当前在场上的数量（调试用：想实测就把它打到 Console/Inspector）
    {
        get
        {
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].expireTime > 0f) n++;
            return n;
        }
    }

    public int PooledCount => entries.Count;   // 一共造了多少个实例（稳态下应等于 capacity）

    // 预热：一次性把实例建好并置为隐藏，避免第一次开火时才创建（用不到就不必调它）
    public void Prewarm()
    {
        if (prefab == null) return;
        while (entries.Count < capacity)
        {
            GameObject go = Object.Instantiate(prefab);
            go.SetActive(false);
            entries.Add(new Entry { go = go, expireTime = 0f });
        }
    }

    // 取一个实例：优先复用空闲的；不够就新建（不超过容量）；容量满了就覆盖"最早到期"的那个
    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        int index = FindFreeIndex();
        if (index < 0)
        {
            if (entries.Count < capacity)
            {
                entries.Add(default);          // 新槽：go == null，下面会创建实例
                index = entries.Count - 1;
            }
            else
            {
                index = FindOldestIndex();     // 池满 → 覆盖最旧的（和原来"超出上限覆盖最旧"一致）
            }
        }

        Entry entry = entries[index];
        if (entry.go == null)                  // 首次创建，或实例被场景卸载销毁过 → 重建
            entry.go = Object.Instantiate(prefab);

        entry.go.transform.SetPositionAndRotation(position, rotation);
        if (!entry.go.activeSelf) entry.go.SetActive(true);
        entry.expireTime = Time.time + lifetime;
        entries[index] = entry;
        return entry.go;
    }

    // 每帧调用：把到期的实例回收（隐藏 + 标记空闲）。池化后不再用 Destroy(go, lifetime) 定时销毁
    public void ReleaseExpired()
    {
        if (entries.Count == 0) return;
        float now = Time.time;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.expireTime <= 0f) continue;                 // 本来就空闲
            if (entry.go == null)                                  // 被外部销毁（如换场景）→ 视为空闲
            {
                entry.expireTime = 0f;
                entries[i] = entry;
                continue;
            }
            if (now >= entry.expireTime)
            {
                entry.go.SetActive(false);
                entry.expireTime = 0f;
                entries[i] = entry;
            }
        }
    }

    // 找空闲槽：到期回收过的，或实例已被销毁的（go == null）
    private int FindFreeIndex()
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].expireTime <= 0f || entries[i].go == null) return i;
        return -1;
    }

    // 找"最早到期"的槽 = 场上最旧的那个（池满时覆盖它）
    private int FindOldestIndex()
    {
        int oldest = 0;
        for (int i = 1; i < entries.Count; i++)
            if (entries[i].expireTime < entries[oldest].expireTime) oldest = i;
        return oldest;
    }
}
