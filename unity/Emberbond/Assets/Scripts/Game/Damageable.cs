using UnityEngine;

namespace Emberbond
{
    /// <summary>Anything that can be hurt: the player, enemy dragons, soldiers.</summary>
    public sealed class Damageable : MonoBehaviour
    {
        public float MaxHealth = 100f;
        public float Health = 100f;
        public string DisplayName = "";
        public bool IsBoss;
        public System.Action<Damageable, float, GameObject> OnDamaged;
        public System.Action<Damageable> OnDied;

        public bool Dead { get { return Health <= 0f; } }
        public float Fraction { get { return Mathf.Clamp01(Health / Mathf.Max(1f, MaxHealth)); } }

        public void Hurt(float amount, GameObject source)
        {
            if (Dead || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            if (OnDamaged != null) OnDamaged(this, amount, source);
            if (Health <= 0f && OnDied != null) OnDied(this);
        }

        public void Heal(float amount) { Health = Mathf.Min(MaxHealth, Health + amount); }
    }
}
