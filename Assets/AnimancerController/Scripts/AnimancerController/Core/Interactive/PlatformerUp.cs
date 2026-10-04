using System.Collections;
using UnityEngine;
/*************************************************
作者: HuHu
邮箱: 3112891874@qq.com
功能: 平台跳跃交互
*************************************************/

public class PlatformerUp :MonoBehaviour
{
    LayerMask playerMask;
    [SerializeField] private float forceHight=15;
    private void Awake()
    {
        playerMask = LayerMask.GetMask("Player");
    }
    private void OnTriggerEnter(Collider other)
    {
        if ((1<< other.gameObject.layer& playerMask)!=0)
        {
            if (other.TryGetComponent<Player>(out var player))
            {
                player.ReusableData.jumpExternalForce = forceHight;
                player.StateMachine.ChangeState(player.StateMachine.platformerUpState);
            }
        }
    }
}
