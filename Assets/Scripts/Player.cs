using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{
    [SerializeField]
    private CharacterController charController;

    InputAction moveAction;
    InputAction jumpAction;
    private const float gravityMultiplier = 0.05f;
    private float gravity = -9.8f * gravityMultiplier;

    [SerializeField]
    private float baseMoveSpeed = 1;

    [SerializeField]
    private Animator animator;

    private Vector2 currentMovement;
    private Vector3 currentOtherVelocity;

    private void Start()
    {
        // 3. Find the references to the "Move" and "Jump" actions
        moveAction = InputSystem.actions.FindAction("Move");
        jumpAction = InputSystem.actions.FindAction("Jump");
    }

    private void Update()
    {
        if (charController.isGrounded)
        {
            currentOtherVelocity = gravity * Time.deltaTime * Vector3.up;
        }
        else
        {
            currentOtherVelocity += gravity * Time.deltaTime * Vector3.up;
        }

        currentMovement = baseMoveSpeed * Time.deltaTime * moveAction.ReadValue<Vector2>();

        if (jumpAction.WasPerformedThisFrame() && charController.isGrounded)
        {
            currentOtherVelocity += Vector3.up * .15f;
        }

        var velocity = new Vector3(currentMovement.x, 0, currentMovement.y) + currentOtherVelocity;
        charController.Move(velocity);
    }
}
