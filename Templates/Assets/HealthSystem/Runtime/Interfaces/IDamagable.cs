using UnityEngine;

namespace PG.HealthSystem
{
    public interface IDamagable
    {
        public event System.Action<float, GameObject> damaged;
        public void OnDamage(float damage, bool unblockable = false, GameObject damageObject = null);
    }
}
