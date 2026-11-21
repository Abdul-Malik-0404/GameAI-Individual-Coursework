using UnityEngine;

// Implemented by damageable NPCs; sourcePosition reports where the shot originated.
public interface IDamageable
{
    void TakeDamage(int damage, Vector3 sourcePosition);
}

// Probabilistic damage roll (graze / normal hit / critical hit) for NPC attacks.
[System.Serializable]
public class DamageProfile
{
    [Range(0f, 1f)] public float grazeChance = 0.25f;
    public float grazeMultiplier = 0.5f;
    [Range(0f, 1f)] public float critChance = 0.15f;
    public float critMultiplier = 2.5f;

    public DamageProfile() { }

    public DamageProfile(float grazeChance, float grazeMultiplier, float critChance, float critMultiplier)
    {
        this.grazeChance = grazeChance;
        this.grazeMultiplier = grazeMultiplier;
        this.critChance = critChance;
        this.critMultiplier = critMultiplier;
    }

    public int Roll(int baseDamage, string shooter, out string hitType)
    {
        float roll = Random.value;
        float multiplier;

        if (roll < critChance)
        {
            multiplier = critMultiplier;
            hitType = "CRIT";
        }
        else if (roll < critChance + grazeChance)
        {
            multiplier = grazeMultiplier;
            hitType = "GRAZE";
        }
        else
        {
            multiplier = 1f;
            hitType = "HIT";
        }

        int damage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * multiplier));
        Debug.Log(shooter + " damage roll " + roll.ToString("0.00") + " -> " + hitType +
                  " (" + baseDamage + " x " + multiplier + " = " + damage + ")");
        return damage;
    }

    public float ExpectedMultiplier
    {
        get
        {
            float normalChance = Mathf.Max(0f, 1f - critChance - grazeChance);
            return critChance * critMultiplier + grazeChance * grazeMultiplier + normalChance;
        }
    }
}

// Trigger-based projectile shared by both the player (fixed damage) and NPCs (rolled damage).
public class Bullet : MonoBehaviour
{
    public int damage = 10;
    public bool firedByPlayer = true;

    [System.NonSerialized] public DamageProfile damageProfile;
    [System.NonSerialized] public string shooterName = "NPC";

    private Vector3 origin;

    void Awake()
    {
        origin = transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.isTrigger) return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        IDamageable npc = other.GetComponentInParent<IDamageable>();

        if (firedByPlayer)
        {
            if (player != null) return;
            if (npc != null) npc.TakeDamage(damage, origin);
        }
        else
        {
            if (npc != null) return;
            if (player != null)
            {
                string hitType = "HIT";
                int finalDamage = damageProfile != null
                    ? damageProfile.Roll(damage, shooterName, out hitType)
                    : damage;
                player.TakeDamage(finalDamage, hitType);
            }
        }

        Destroy(gameObject);
    }
}
