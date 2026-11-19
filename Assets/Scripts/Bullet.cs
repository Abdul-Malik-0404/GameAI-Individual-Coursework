using UnityEngine;

// Anything a player's bullet can hurt (the NPC bots) implements this.
// sourcePosition = where the shot was fired from, so the victim can react to it
// (e.g. the GridBot goes to investigate that spot if it didn't see the shooter).
public interface IDamageable
{
    void TakeDamage(int damage, Vector3 sourcePosition);
}

// OPTION A - PROBABILISTIC DAMAGE
// Every time an NPC hits the player, the damage is decided by a dice roll instead of a fixed number:
//
//   roll = random number from 0 to 1
//   roll < critChance                    -> CRITICAL : base damage x critMultiplier
//   roll < critChance + grazeChance      -> GRAZE    : base damage x grazeMultiplier
//   otherwise                            -> HIT      : base damage x 1
//
// Average damage per hit = base x (crit% x critMult + graze% x grazeMult + normal% x 1)
//
// The model is ASYMMETRIC in two ways:
//   1. Between the two sides: NPC hits on the player are rolled; the player's hits on NPCs are fixed.
//      The player's damage stays predictable so skill decides the fight, while enemy damage
//      varies so fights don't feel scripted.
//   2. In shape: most hits do normal or reduced damage, with a rare big critical - a long tail on the
//      high side (a right-skewed distribution), so a crit is a rare, readable "spike" of danger.
// Each NPC has its own profile (set in its Inspector) to match its character.
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

// Put this on the bullet prefab.
// The prefab needs: a Rigidbody with Use Gravity OFF, and a Collider with Is Trigger ON.
// The player and the NPCs share the same prefab.
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
            if (npc != null) npc.TakeDamage(damage, origin); // Player damage is fixed (asymmetric model)
        }
        else
        {
            if (npc != null) return;                    // NPC bullets pass through NPCs (no friendly fire)
            if (player != null)
            {
                // Option A: roll the dice for this hit (fixed damage only if no profile was given)
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
