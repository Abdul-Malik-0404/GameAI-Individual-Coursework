using UnityEngine;

// Anything a player's bullet can hurt (the NPC bots) implements this.
// sourcePosition = where the shot was fired from, so the victim can react to it
// (e.g. the GridBot goes to investigate that spot if it didn't see the shooter).
public interface IDamageable
{
    void TakeDamage(int damage, Vector3 sourcePosition);
}

[System.Serializable]
public class DamageProfile
{
    [Tooltip("Chance (0-1) that a hit only grazes.")]
    [Range(0f, 1f)] public float grazeChance = 0.25f;
    [Tooltip("Damage multiplier for a graze (0.5 = half damage).")]
    public float grazeMultiplier = 0.5f;
    [Tooltip("Chance (0-1) of a critical hit.")]
    [Range(0f, 1f)] public float critChance = 0.15f;
    [Tooltip("Damage multiplier for a critical hit (2.5 = two and a half times the damage).")]
    public float critMultiplier = 2.5f;

    public DamageProfile() { }

    public DamageProfile(float grazeChance, float grazeMultiplier, float critChance, float critMultiplier)
    {
        this.grazeChance = grazeChance;
        this.grazeMultiplier = grazeMultiplier;
        this.critChance = critChance;
        this.critMultiplier = critMultiplier;
    }

    // Rolls the dice for one hit. Returns the final damage; hitType says what kind of hit it was.
    public int Roll(int baseDamage, string shooter, out string hitType)
    {
        float roll = Random.value; // Uniform random number from 0 to 1
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

    // Average damage multiplier over many hits (useful for the write-up)
    public float ExpectedMultiplier
    {
        get
        {
            float normalChance = Mathf.Max(0f, 1f - critChance - grazeChance);
            return critChance * critMultiplier + grazeChance * grazeMultiplier + normalChance;
        }
    }
}

public class Bullet : MonoBehaviour
{
    [Tooltip("Base damage. NPCs overwrite this on the bullets they fire.")]
    public int damage = 10;

    [Tooltip("Leave ON for the player's bullets. NPCs switch it OFF on the bullets they fire.")]
    public bool firedByPlayer = true;

    // Set by the NPC that fires this bullet. Left empty on the player's bullets (they don't roll).
    [System.NonSerialized] public DamageProfile damageProfile;
    [System.NonSerialized] public string shooterName = "NPC";

    private Vector3 origin; // Where the bullet was fired from

    void Awake()
    {
        // Awake runs inside Instantiate, so this is the muzzle (fire point) position
        origin = transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.isTrigger) return; // Ignore trigger zones, artefacts and other bullets

        PlayerController player = other.GetComponentInParent<PlayerController>();
        IDamageable npc = other.GetComponentInParent<IDamageable>();

        if (firedByPlayer)
        {
            if (player != null) return;                 // Don't hit the shooter
            if (npc != null) npc.TakeDamage(damage, origin); // Player damage is fixed
        }
        else
        {
            if (npc != null) return;                    // NPC bullets pass through NPCs (no friendly fire)
            if (player != null)
            {
                string hitType = "HIT";
                int finalDamage = damage;
                if (damageProfile != null)
                    finalDamage = damageProfile.Roll(damage, shooterName, out hitType);
                player.TakeDamage(finalDamage, hitType);
            }
        }

        // Disappear on hitting anything else (walls, the target, etc.)
        Destroy(gameObject);
    }
}
