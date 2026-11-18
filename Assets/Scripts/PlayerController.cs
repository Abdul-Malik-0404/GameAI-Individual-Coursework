using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float turnSpeed = 10f;
    private Rigidbody rb;
    private Vector3 movement;

    [Header("Combat Settings")]
    public int healthPoints = 100;
    public GameObject bulletPrefab;
    public Transform firePoint; // Where the bullet spawns
    public float bulletSpeed = 15f;

    [Header("Respawn Settings")]
    [Tooltip("Seconds between dying and coming back at the start position.")]
    public float respawnDelay = 3f;
    [Tooltip("Seconds of invulnerability after respawning (the player blinks).")]
    public float spawnProtectionTime = 2f;

    // NPCs that can HEAR (the Tanker) listen to this. It sends the position the shot was fired from.
    public static event System.Action<Vector3> OnShotFired;

    // NPCs check this so they ignore the player while they are dead
    public bool IsDead { get; private set; }

    private int maxHealth;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float respawnTimer;
    private float invulnerableUntil;
    private float knockbackUntil;
    private bool isBlinking;
    private Renderer[] renderers;
    private Collider[] colliders;

    void Awake()
    {
        // Starting health counts as full health
        maxHealth = healthPoints;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        // Remember where the player started, to respawn there
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        // Everything that makes the player visible or solid, to hide it while dead
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
    }

    void Update()
    {
        // While dead: no moving or shooting, just count down to the respawn
        if (IsDead)
        {
            respawnTimer -= Time.deltaTime;
            if (respawnTimer <= 0f) Respawn();
            return;
        }

        UpdateSpawnProtectionBlink();

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        // 1. Gather 8-directional input (WASD or Arrow Keys)
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

        // 2. Handle Projectile Attack (Spacebar or Left Click)
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

        // While knocked back (e.g. rammed by the Tanker), let physics carry the player instead of the controls
        if (Time.time < knockbackUntil) return;

        // Apply movement to the Rigidbody for solid wall collisions.
        // Keep the current vertical speed so gravity still works (otherwise the player floats after being lifted).
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

    // Called by NPC bullets and by the Tanker's ram
    public void TakeDamage(int damage)
    {
        if (IsDead) return;
        if (Time.time < invulnerableUntil) return; // Spawn protection

        healthPoints -= damage;
        Debug.Log("Player HP: " + healthPoints);

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
        healthPoints = Mathf.Min(maxHealth, healthPoints + amount);
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
        respawnTimer = respawnDelay;
        movement = Vector3.zero;

        // Freeze in place (velocity first: a kinematic body can't have its velocity set)
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;

        // Hide the body and switch off its colliders, so bullets pass through and NPCs lose it
        SetBodyActive(false);

        Debug.Log("Player Defeated! Respawning in " + respawnDelay + " seconds.");
    }

    void Respawn()
    {
        // Move back to the start position
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        rb.position = spawnPosition;
        rb.rotation = spawnRotation;

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;

        healthPoints = maxHealth;
        IsDead = false;
        knockbackUntil = 0f;
        invulnerableUntil = Time.time + spawnProtectionTime;

        SetBodyActive(true);
        Debug.Log("Player respawned.");
    }

    void SetBodyActive(bool active)
    {
        foreach (Renderer r in renderers) r.enabled = active;
        foreach (Collider c in colliders) c.enabled = active;
    }

    // Blink while spawn protection is on, so it's clear the player can't be hurt yet
    void UpdateSpawnProtectionBlink()
    {
        if (Time.time < invulnerableUntil)
        {
            bool visible = Mathf.FloorToInt(Time.time * 10f) % 2 == 0;
            foreach (Renderer r in renderers) r.enabled = visible;
            isBlinking = true;
        }
        else if (isBlinking)
        {
            foreach (Renderer r in renderers) r.enabled = true;
            isBlinking = false;
        }
    }
}
