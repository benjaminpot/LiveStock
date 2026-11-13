using UnityEngine;
using UnityEngine.InputSystem;  

public class PlayerControl : MonoBehaviour
{
   public Transform cam;
    private Vector2 moveInput; //2D方向移動，private:只有這支程式可以用
    private Vector2 lookInput; //偵測滑鼠移多少
    private Rigidbody rb;
    private bool grounded;
    public LayerMask groundMask;
    private float speed = 5f;
    private FruitPickup nearbyFruit;

    public void Move(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }
    public void Look(InputAction.CallbackContext context)
    {
        lookInput = context.ReadValue<Vector2>();
    }
    public void Jump(InputAction.CallbackContext context)
    {
        if (context.performed && grounded) //按下的瞬間
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x,5f,rb.linearVelocity.z);
        }
    }
    public void Sprint(InputAction.CallbackContext context)
    {
        if (context.ReadValueAsButton())
        {
            speed = 10f;
        }
        else
        {
            speed = 5f;
        }
    }
    public void Crouch(InputAction.CallbackContext context)
    {
        if (context.ReadValueAsButton())
        {
            speed = 2f;
            transform.localScale = new Vector3(1f,0.6f,1f);
        }
        else
        {
            speed = 5f;
            transform.localScale = new Vector3(1f,1f,1f);

        }
    }
    public void EnterFruitRange(FruitPickup fruit)
    {
        
            nearbyFruit = fruit;
    }
    public void ExitFruitRange(FruitPickup fruit)
    {
        if (nearbyFruit == fruit)
        {
            nearbyFruit = null;
        }
    }
    public void Interact(InputAction.CallbackContext context)
    {
        if (!context.performed) return;
        if(nearbyFruit == null) return;
        nearbyFruit.Collect();
        nearbyFruit = null;
    }
    private void Awake() //拿到組件
    {
        rb = GetComponent<Rigidbody>();
    }
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked; //讓滑鼠不見
    }

    // Update is called once per frame
    void Update()
    {              
        grounded = Physics.Raycast(transform.position,Vector3.down,1f*transform.localScale.y+0.1f,groundMask,QueryTriggerInteraction.Ignore);     
        if(rb.linearVelocity.y>0.1f) grounded = false;
                       //玩家看的方向

        transform.Rotate(Vector3.up * lookInput.x * 0.15f);
        cam.Rotate(Vector3.left*lookInput.y*0.1f);
    }
    private void FixedUpdate()
    {
        Vector3 move = transform.forward*moveInput.y + transform.right*moveInput.x;
        rb.linearVelocity = new Vector3(move.x * speed, rb.linearVelocity.y, move.z * speed);
        
    }
}
