using UnityEngine;

// Anything a player's bullet can hurt (the NPC bots) implements this.
// sourcePosition = where the shot was fired from, so the victim can react to it
// (e.g. the GridBot goes to investigate that spot if it didn't see the shooter).
public interface IDamageable
{
    void TakeDamage(int damage, Vector3 sourcePosition);
}

// Put this on the bullet prefab.
// The prefab needs: a Rigidbody with Use Gravity OFF, and a Collider with Is Trigger ON.
// The player and the NPCs can share the same prefab.
public class Bullet : MonoBehaviour
{
    public int damage = 10;

    [Tooltip("Leave ON for the player's bullets. NPCs switch it OFF on the bullets they fire.")]
    public bool firedByPlayer = true;

    private Vector3 origin; // Where the bullet was fired from

    void Awake()
    {
        // Awake runs inside Instantiate, so this is the muzzle (fire point) position
        origin = transform.position;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.isTrigger) return; // Ignore trigger zones and other bullets

        PlayerController player = other.GetComponentInParent<PlayerController>();
        IDamageable npc = other.GetComponentInParent<IDamageable>();

        if (firedByPlayer)
        {
            if (player != null) return;                 // Don't hit the shooter
            if (npc != null) npc.TakeDamage(damage, origin);
        }
        else
        {
            if (npc != null) return;                    // NPC bullets pass through NPCs (no friendly fire)
            if (player != null) player.TakeDamage(damage);
        }

        // Disappear on hitting anything else (walls, the target, etc.)
        Destroy(gameObject);
    }
}
