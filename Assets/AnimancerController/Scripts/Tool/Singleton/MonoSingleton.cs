
using UnityEngine;
//在物体销毁时访问次对象最好加一个Null的判断，防止程序退出次物体被销毁
public class MonoSingleton<T>:MonoBehaviour where T : MonoSingleton<T> 
{
    private static T instance;
    public static T Instance
    {
        get 
        {
            if (instance == null)
            {
                instance = GameObject.FindAnyObjectByType<T>();
                if (instance == null)
                {
                    var go = new GameObject(typeof(T).Name);
                    instance = go.AddComponent<T>();
                }
            }
            return instance; 
        }
    }
    protected virtual void Awake()
    {
        //默认放到DontDestroyScene中
        if (instance == null)
        {
            instance = (T)this;
            DontDestroyOnLoad(instance);
        }
        else
        {
            Destroy(instance);
        }
    }
}
