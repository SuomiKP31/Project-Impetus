using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ControlSphereLogic : MonoBehaviour
{
    [Range(0.1f, 10.0f)]
    [SerializeField]
    private float Velocity;

    float m_horizontalInput;
    float m_verticalInput;
    Vector3 m_movementInput;
    Vector3 m_movement;
    // Update: Get Keyboard input
    void Start()
    {
        Velocity = 4.0f;
    }
    void Update()
    {
        m_horizontalInput = Input.GetAxis("Horizontal");
        m_verticalInput = Input.GetAxis("Vertical");
        //Debug.Log(m_verticalInput);
        m_movementInput = new Vector3(m_horizontalInput, 0, m_verticalInput);
        if (Input.GetButton("Jump")) m_movementInput.y = 1.0f;
        else if (Input.GetButton("Down")) m_movementInput.y = -1.0f;
        else m_movementInput.y = 0;
    }
    // Fixed Update: Update position
    void FixedUpdate()
    {
        m_movement = m_movementInput * Velocity * Time.deltaTime;
        gameObject.transform.position += m_movement;
    }
}
