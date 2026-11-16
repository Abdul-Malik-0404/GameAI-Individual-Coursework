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

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
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
        // Apply movement to the Rigidbody for solid wall collisions
        rb.linearVelocity = movement * moveSpeed;

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
        }
    }

    // Method to handle taking damage from NPCs later
    public void TakeDamage(int damage)
    {
        healthPoints -= damage;
        Debug.Log("Player HP: " + healthPoints);
        if (healthPoints <= 0)
        {
            Debug.Log("Player Defeated!");
            // Add death logic later (respawn or game over)
        }
    }
}
