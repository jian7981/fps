using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class BindableProperty<T> 
{
    public Action<T> ValueChanged;
    private T m_value;
    public T Value
    {
        set { if (!Equals(m_value,value))
            {
                m_value = value;
                ValueChanged?.Invoke(m_value);
            }
            }
       get { return m_value; }
    }

}
