using UnityEngine;

// Collectible world pickup attached and configured by ArtefactSpawner.
public class Artefact : MonoBehaviour
{
    public string typeName = "Artefact";
    public int healAmount = 0;
    public float spinSpeed = 90f;
    public float bobHeight = 0.15f;

    [HideInInspector] public ArtefactSpawner spawner;

    private Vector3 startPosition;
    private float bobOffset;

    void Start()
    {
        startPosition = transform.position;
        bobOffset = Random.Range(0f, 6.28f);
    }

    void Update()
    {
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = startPosition + Vector3.up * Mathf.Sin(Time.time * 2f + bobOffset) * bobHeight;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || player.IsDead) return;

        if (healAmount > 0) player.Heal(healAmount);
        if (spawner != null) spawner.OnArtefactCollected(this);

        Destroy(gameObject);
    }
}
