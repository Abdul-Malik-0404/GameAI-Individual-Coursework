using UnityEngine;
using UnityEngine.InputSystem;

// The player: movement, shooting, health and death.
// When the player dies, the GameManager restarts the whole level, which also resets the player.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float turnSpeed = 10f;
    private Rigidbody rb;
    private Vector3 movement;

    [Header("Combat Settings")]
    [Tooltip("Starting health. This also counts as full health.")]
    public int healthPoints = 100;
    public GameObject bulletPrefab;
    public Transform firePoint; // Where the bullet spawns
    public float bulletSpeed = 15f;

    // Both NPC types HEAR gunshots by listening to this. It sends the position the shot was fired from.
    public static event System.Action<Vector3> OnShotFired;

    // Sent every time the player loses health: (damage taken, hit type e.g. "CRIT", "GRAZE", "HIT").
    // The HUD listens to this to show the damage numbers.
    public event System.Action<int, string> Damaged;

    // NPCs check this so they ignore the player during the countdown before the level restarts
    public bool IsDead { get; private set; }

    // Full health (the starting value of healthPoints). Used by Heal and by the HUD's health bar.
    public int MaxHealth { get; private set; }

    private float knockbackUntil;

    void Awake()
    {
        MaxHealth = healthPoints;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (IsDead) return; // No moving or shooting while dead

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        // Gather 8-directional input (WASD or Arrow Keys)
        float moveX = 0f;
        float moveZ = 0f;

        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  moveX -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) moveX += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  moveZ -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    moveZ += 1f;
        }

        // Normalize prevents moving faster diagonally
        movement = new Vector3(moveX, 0f, moveZ).normalized;

        // Handle Projectile Attack (Spacebar or Left Click)
        bool spacePressed = kb != null && kb.spaceKey.wasPressedThisFrame;
        bool clickPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

        if (spacePressed || clickPressed)
        {
            Shoot();
        }
    }

    void FixedUpdate()
    {
        if (IsDead) return;

        // While knocked back (rammed by the Tanker), let physics carry the player instead of the controls
        if (Time.time < knockbackUntil) return;

        // Apply movement to the Rigidbody for solid wall collisions.
        // Keep the current vertical speed so gravity still works.
        rb.linearVelocity = new Vector3(movement.x * moveSpeed, rb.linearVelocity.y, movement.z * moveSpeed);

        // Rotate the player's "Front Face" to point where they are moving
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
        }
    }

    void Shoot()
    {
        if (bulletPrefab != null && firePoint != null)
        {
            // Spawn the bullet and give it forward velocity
            GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
            Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
            if (bulletRb != null)
            {
                bulletRb.linearVelocity = firePoint.forward * bulletSpeed;
            }

            // Destroy bullet after 3 seconds to keep memory clean
            Destroy(bullet, 3f);

            // Make "noise": NPCs that can hear gunshots are told where this shot came from
            if (OnShotFired != null) OnShotFired(firePoint.position);
        }
    }

    public void TakeDamage(int damage, string hitType)
    {
        if (IsDead) return;

        healthPoints -= damage;
        Debug.Log("Player took " + damage + " (" + hitType + "). HP: " + healthPoints);

        if (Damaged != null) Damaged(damage, hitType);

        if (healthPoints <= 0)
        {
            healthPoints = 0;
            Die();
        }
    }

    // Called by artefact pick-ups. Can't go above full health.
    public void Heal(int amount)
    {
        if (IsDead) return;
        healthPoints = Mathf.Min(MaxHealth, healthPoints + amount);
        Debug.Log("Player healed +" + amount + ". HP: " + healthPoints);
    }

    // Called by the Tanker's ram: pushes the player and briefly takes away control
    public void ApplyKnockback(Vector3 velocity, float duration)
    {
        if (IsDead) return;
        rb.linearVelocity = velocity;
        knockbackUntil = Time.time + duration;
    }

    void Die()
    {
        IsDead = true;
        movement = Vector3.zero;

        // Freeze in place (velocity first: a kinematic body can't have its velocity set)
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;

        // Hide the body and switch off its colliders, so bullets pass through during the countdown
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        Debug.Log("Player Defeated!");

        // The GameManager restarts the whole level (player, NPCs and artefacts all reset)
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerDied();
        else
            Debug.LogWarning("PlayerController: no GameManager in the scene, so the level won't restart.");
    }
}
