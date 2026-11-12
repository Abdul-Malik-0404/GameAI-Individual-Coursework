using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player;
    public float distance = 6f;       // How far back the camera sits
    public float heightOffset = 1.5f; // Height of the pivot (aim for the player's head)
    public float mouseSensitivity = 3f;

    private float rotationY = 0f;
    private float rotationX = 15f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked; 
    }

    void LateUpdate()
    {
        if (player != null)
        {
            // 1. Get mouse input
            rotationY += Input.GetAxis("Mouse X") * mouseSensitivity;
            rotationX -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            
            // 2. Clamp to safely prevent looping overhead
            rotationX = Mathf.Clamp(rotationX, -20f, 80f); 

            // 3. Set the camera's rotation strictly to the mouse angle (No LookAt)
            Quaternion currentRotation = Quaternion.Euler(rotationX, rotationY, 0);
            transform.rotation = currentRotation;

            // 4. Position the camera by pushing it backward from the player
            Vector3 targetPivot = player.position + Vector3.up * heightOffset;
            transform.position = targetPivot - (transform.forward * distance);
        }
    }
}
