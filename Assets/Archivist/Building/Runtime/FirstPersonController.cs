using UnityEngine;
using UnityEngine.InputSystem;

namespace Archivist.Building
{
    /// <summary>
    /// POC-04 walk-and-look controller. Implements S2.1–S2.6 of
    /// <c>docs/space/requirements.md</c> and nothing else: no carrying, no
    /// interaction, no head bob, no footsteps. It exists so the room can be
    /// stood in and measured.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Movement (S2.2)")]
        [Tooltip("Metres per second. Carried speed will be a fraction of this, never a second constant.")]
        [SerializeField] float walkSpeed = 1.8f;

        [Header("Look (S2.3)")]
        [Tooltip("Degrees of rotation per pixel of pointer delta.")]
        [SerializeField] float lookSensitivity = 0.12f;
        [SerializeField] float pitchLimit = 85f;

        [Header("Jump (S2.5)")]
        [Tooltip("Honoured as a collision probe for the POC. The shipping default is expected to be off.")]
        [SerializeField] bool enableJump = true;
        [SerializeField] float jumpHeight = 0.6f;
        [SerializeField] float gravity = -9.81f;

        [Header("Wiring")]
        [SerializeField] InputActionAsset inputActions;
        [SerializeField] Transform eye;

        CharacterController body;
        InputActionMap playerMap;
        InputAction moveAction;
        InputAction lookAction;
        InputAction jumpAction;

        float pitch;
        float verticalSpeed;

        void Awake()
        {
            body = GetComponent<CharacterController>();

            playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
            moveAction = playerMap.FindAction("Move", throwIfNotFound: true);
            lookAction = playerMap.FindAction("Look", throwIfNotFound: true);
            jumpAction = playerMap.FindAction("Jump", throwIfNotFound: true);
        }

        void OnEnable()
        {
            playerMap.Enable();
            SetCursorCaptured(true);
        }

        void OnDisable()
        {
            playerMap.Disable();
            SetCursorCaptured(false);
        }

        void Update()
        {
            ApplyLook();
            ApplyMove();
        }

        void ApplyLook()
        {
            Vector2 delta = lookAction.ReadValue<Vector2>() * lookSensitivity;

            transform.Rotate(0f, delta.x, 0f);

            pitch = Mathf.Clamp(pitch - delta.y, -pitchLimit, pitchLimit);
            eye.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void ApplyMove()
        {
            Vector2 input = moveAction.ReadValue<Vector2>();
            Vector3 planar = (transform.right * input.x + transform.forward * input.y);
            if (planar.sqrMagnitude > 1f) planar.Normalize();

            // A small downward bias keeps the controller in contact with the floor,
            // so isGrounded stays true across seams instead of chattering (S2.6).
            if (body.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;

            if (enableJump && body.isGrounded && jumpAction.WasPressedThisFrame())
                verticalSpeed = Mathf.Sqrt(-2f * gravity * jumpHeight);

            verticalSpeed += gravity * Time.deltaTime;

            Vector3 velocity = planar * walkSpeed + Vector3.up * verticalSpeed;
            body.Move(velocity * Time.deltaTime);
        }

        static void SetCursorCaptured(bool captured)
        {
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }
    }
}
