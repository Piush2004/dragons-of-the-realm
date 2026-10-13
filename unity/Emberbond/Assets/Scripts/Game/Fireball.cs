using UnityEngine;
using UnityEngine.Rendering;

namespace Emberbond
{
    /// <summary>A dragon's fireball: glows, lights its surroundings, explodes on contact and burns what it hits.</summary>
    public sealed class Fireball : MonoBehaviour
    {
        public Vector3 Velocity;
        public GameObject Owner;
        public float Damage = 25f;
        public float Radius = 7f;
        public float Life = 6f;
        private Light _light;

        public static Fireball Spawn(Vector3 pos, Vector3 velocity, GameObject owner, float damage)
        {
            GameObject go = new GameObject("Fireball");
            go.transform.position = pos;
            Fireball f = go.AddComponent<Fireball>();
            AudioDirector.Play(Sfx.Fire, pos, 1f);
            f.Velocity = velocity; f.Owner = owner; f.Damage = damage;
            return f;
        }

        private void Start()
        {
            Material m = Effects.Additive;

            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.55f, 0.2f);
            _light.range = 90f;
            _light.intensity = 3e6f;                       // HDRP uses lumens; this reads as a bright fire
            _light.shadows = LightShadows.None;

            ParticleSystem ps = gameObject.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = m;
            var main = ps.main;
            main.startLifetime = 0.5f; main.startSize = 3.2f; main.startSpeed = 0.5f;
            main.startColor = new Color(1f, 0.6f, 0.2f, 0.28f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 50f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.9f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f), new GradientColorKey(new Color(0.8f, 0.15f, 0.02f), 1f) },
                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
        }

        private void Update()
        {
            Life -= Time.deltaTime;
            if (Life <= 0f) { Destroy(gameObject); return; }
            Vector3 step = Velocity * Time.deltaTime;
            RaycastHit hit;
            if (Physics.SphereCast(transform.position, 1.1f, step.normalized, out hit, step.magnitude + 0.1f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Owner == null || !hit.collider.transform.IsChildOf(Owner.transform))
                {
                    Explode(hit.point);
                    return;
                }
            }
            transform.position += step;
            Terrain t = Terrain.activeTerrain;
            if (t != null && transform.position.y < t.SampleHeight(transform.position) + t.transform.position.y)
                Explode(transform.position);
        }

        private void Explode(Vector3 at)
        {
            Collider[] hits = Physics.OverlapSphere(at, Radius);
            for (int i = 0; i < hits.Length; i++)
            {
                Damageable d = hits[i].GetComponentInParent<Damageable>();
                if (d == null || (Owner != null && d.gameObject == Owner)) continue;
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(at, hits[i].transform.position) / Radius);
                d.Hurt(Damage * Mathf.Lerp(0.4f, 1f, falloff), Owner);
            }
            // Blast force on anything that is a rigid body, in a slightly wider ring than the damage.
            Collider[] around = Physics.OverlapSphere(at, Radius * 1.7f);
            for (int i = 0; i < around.Length; i++)
            {
                Rigidbody rb = around[i].attachedRigidbody;
                if (rb != null && !rb.isKinematic) rb.AddExplosionForce(1800f, at, Radius * 1.7f, 0.9f, ForceMode.Impulse);
                ThirdPersonPlayer pl = around[i].GetComponentInParent<ThirdPersonPlayer>();
                if (pl != null && (Owner == null || pl.gameObject != Owner))
                {
                    Vector3 away = pl.transform.position - at; away.y = 0f;
                    pl.Knock(away.normalized * 9f + Vector3.up * 3f);
                }
            }
            Effects.Burst(at, Radius);
            AudioDirector.Play(Sfx.Explosion, at, 1f);
            Destroy(gameObject);
        }
    }

    /// <summary>Small one-shot visual effects built from code.</summary>
    public static class Effects
    {
        private static Material _additive;
        public static Material Additive
        {
            get
            {
                if (_additive == null) _additive = Resources.Load<Material>("FireAdditive");
                if (_additive == null) _additive = new Material(Shader.Find("HDRP/Unlit"));
                return _additive;
            }
        }

        public static void Burst(Vector3 at, float radius)
        {
            GameObject go = new GameObject("Explosion");
            go.transform.position = at;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.25f); l.range = radius * 14f; l.intensity = 6e6f;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = Additive;
            var main = ps.main;
            main.duration = 0.4f; main.loop = false; main.startLifetime = 1.4f; main.startSize = radius * 0.7f;
            main.startSpeed = radius * 1.2f; main.startColor = new Color(1f, 0.5f, 0.1f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.5f;
            var col = ps.colorOverLifetime; col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(0.15f, 0.1f, 0.08f), 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            Object.Destroy(go, 3f);
            go.AddComponent<FadeLight>();
        }
    }

    public sealed class FadeLight : MonoBehaviour
    {
        private Light _l; private float _t;
        private void Start() { _l = GetComponent<Light>(); }
        private void Update()
        {
            _t += Time.deltaTime;
            if (_l != null) _l.intensity = Mathf.Lerp(6e6f, 0f, _t / 0.6f);
        }
    }
}
