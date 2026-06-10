using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class MoveCamera : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float verticalSpeed = 6f;
    [SerializeField] private float lookSensitivity = 0.5f;
    [SerializeField] private float minPitch = -89f;
    [SerializeField] private float maxPitch = 89f;

    private CharacterController controller;

    private Vector2 inputMovement;
    private Vector2 lookDirection;

    private bool isMovingUp;
    private bool isMovingDown;

    private float yaw;
    private float pitch;

    private void Start()
    {
        controller = GetComponent<CharacterController>();

        Vector3 startRotation = transform.eulerAngles;
        yaw = startRotation.y;
        pitch = startRotation.x;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        RotateCamera();
        Move();
    }

    private void RotateCamera()
    {
        yaw += lookDirection.x * lookSensitivity;
        pitch -= lookDirection.y * lookSensitivity;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void Move()
    {
        Vector3 moveDirection =
            transform.right * inputMovement.x +
            transform.forward * inputMovement.y;

        if (isMovingUp)
        {
            moveDirection += Vector3.up;
        }

        if (isMovingDown)
        {
            moveDirection += Vector3.down;
        }

        controller.Move(moveDirection * moveSpeed * Time.deltaTime);
    }

    public void Move(InputAction.CallbackContext context)
    {
        inputMovement = context.ReadValue<Vector2>();
    }

    public void Look(InputAction.CallbackContext context)
    {
        lookDirection = context.ReadValue<Vector2>();
    }

    public void Up(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            isMovingUp = true;
        }

        if (context.canceled)
        {
            isMovingUp = false;
        }
    }

    public void Down(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            isMovingDown = true;
        }

        if (context.canceled)
        {
            isMovingDown = false;
        }
    }
}