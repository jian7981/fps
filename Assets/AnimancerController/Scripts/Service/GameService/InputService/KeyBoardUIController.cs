using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class KeyboardUIController : MonoBehaviour
{
    public EventSystem eventSystem;
    public Selectable firstSelected;

    void Start()
    {
        // 设置初始选中的 UI 元素
        if (firstSelected != null)
        {
            firstSelected.Select();
        }
    }
    void Update()
    {
        // 检查是否有选中的 UI 元素
        if (eventSystem.currentSelectedGameObject == null)
        {
            eventSystem.SetSelectedGameObject(firstSelected.gameObject);
        }

        // 处理键盘输入
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            Selectable next = eventSystem.currentSelectedGameObject.GetComponent<Selectable>().FindSelectableOnDown();
            if (next != null)
            {
                next.Select();
            }
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            // 处理确认操作
            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
        }
    }
}
