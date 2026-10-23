using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float jumpForce = 5f;
    public Transform groundCheck;
    public float groundDistance = 0.4f;
    public LayerMask groundMask;

    private Rigidbody rb;
    private Vector2 movementInput;
    private bool isGrounded;
    private PlayerInput playerInput;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = new PlayerInput();  
    }

  
    void Update()
    {
        CheckGrounded();
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    void OnJump()
    {
        if (isGrounded)
        {
            rb.AddForce(new Vector3(0, jumpForce, 0), ForceMode.Impulse);
        }
    }
    void CheckGrounded()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);
    }
  
    
   void OnMovement(InputValue value)
    {
    movementInput = value.Get<Vector2>();
    }

    void MovePlayer()
    {
        Vector3 direction = transform.right * movementInput.x + transform.forward * movementInput.y;
            direction.Normalize();
        rb.linearVelocity = new Vector3(direction.x * moveSpeed, rb.linearVelocity.y, direction.z * moveSpeed);
    } 
      
}
