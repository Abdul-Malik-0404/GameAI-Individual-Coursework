using UnityEngine;
using UnityEngine.InputSystem;

// Handles player movement, projectile firing, health, knockback, and death notification.
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float turnSpeed = 10f;

    [Header("Combat Settings")]
    public int healthPoints = 100;
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float bulletSpeed = 15f;

    // Broadcasts shot origin to listening NPCs; Damaged notifies GameHUD of hit feedback.
    public static event System.Action<Vector3> OnShotFired;
    public event System.Action<int, string> Damaged;

    public bool IsDead { get; private set; }
    public int MaxHealth { get; private set; }

    private Rigidbody rb;
    private Vector3 movement;
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
        if (IsDead) return;

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        float moveX = 0f;
        float moveZ = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  moveX -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) moveX += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  moveZ -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    moveZ += 1f;
        }
        movement = new Vector3(moveX, 0f, moveZ).normalized;

        bool spacePressed = kb != null && kb.spaceKey.wasPressedThisFrame;
        bool clickPressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
        if (spacePressed || clickPressed) Shoot();
    }

    void FixedUpdate()
    {
        if (IsDead || Time.time < knockbackUntil) return;

        rb.linearVelocity = new Vector3(movement.x * moveSpeed, rb.linearVelocity.y, movement.z * moveSpeed);

        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
        }
    }

    void Shoot()
    {
        if (bulletPrefab == null || firePoint == null) return;

        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        Rigidbody bulletRb = bullet.GetComponent<Rigidbody>();
        if (bulletRb != null) bulletRb.linearVelocity = firePoint.forward * bulletSpeed;

        Destroy(bullet, 3f);
        if (OnShotFired != null) OnShotFired(firePoint.position);
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

    public void Heal(int amount)
    {
        if (IsDead) return;
        healthPoints = Mathf.Min(MaxHealth, healthPoints + amount);
        Debug.Log("Player healed +" + amount + ". HP: " + healthPoints);
    }

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
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;

        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        Debug.Log("Player Defeated!");
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayerDied();
        else
            Debug.LogWarning("PlayerController: no GameManager in the scene, so the level won't restart.");
    }
}
