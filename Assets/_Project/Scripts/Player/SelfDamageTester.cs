using UnityEngine;

// 【临时验证用，测完删掉】按 K 对自己造成伤害，用来验证 HUD 的血条/护甲条
public class SelfDamageTester : MonoBehaviour
{
    [SerializeField] private int damage = 25;

    private Health health;

    private void Awake() { health = GetComponent<Health>(); }

    private void Update()
    {
        // attacker 传 null = 环境伤害，会自动绕过"同队免伤"
        if (Input.GetKeyDown(KeyCode.K)) health.TakeDamage(damage, null);
    }
}