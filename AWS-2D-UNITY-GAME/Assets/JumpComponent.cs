using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class JumpComponent : MonoBehaviour
{
    public float jumpForce = 5f;
    public bool isGrounded = true;

    public event Action OnJump;

    private Rigidbody2D rb;

    void Awake() => rb = GetComponent<Rigidbody2D>();

    void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame &&
            isGrounded)
        {
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            isGrounded = false;

            OnJump?.Invoke(); // salto real
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
    }
}
