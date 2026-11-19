using UnityEngine;

// A placed artefact. The ArtefactSpawner adds this automatically and fills in its type.
// Pick-up is optional for the brief: walking into it collects it, and Health Pack / Extra Health heal.
public class Artefact : MonoBehaviour
{
    [Tooltip("Set by the spawner, e.g. 'Shield'.")]
    public string typeName = "Artefact";
    [Tooltip("Health given to the player on pick-up (0 = none).")]
    public int healAmount = 0;
    [Tooltip("Degrees per second it spins, so it stands out.")]
    public float spinSpeed = 90f;
    [Tooltip("How far it bobs up and down.")]
    public float bobHeight = 0.15f;

    [HideInInspector] public ArtefactSpawner spawner; // Set by the spawner

    private Vector3 startPosition;
    private float bobOffset;

    void Start()
    {
        startPosition = transform.position;
        bobOffset = Random.Range(0f, 6.28f); // So they don't all bob in sync
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = startPosition + Vector3.up * Mathf.Sin(Time.time * 2f + bobOffset) * bobHeight;
    }

    void OnTriggerEnter(Collider other)
    {
        // Only the player can collect it (bullets and NPCs are ignored)
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead) return;

        if (healAmount > 0) player.Heal(healAmount);
        if (spawner != null) spawner.OnArtefactCollected(this);

        Destroy(gameObject);
    }
}
