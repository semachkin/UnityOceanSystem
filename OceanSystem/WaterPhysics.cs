using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaterPhysics : MonoBehaviour
{
    public static List<WaterPhysics> objects = new List<WaterPhysics>();

    Rigidbody rb;

    public static float Gravity = 9.81f;

    [Range(0, 1)]
    public float density;
    [Range(0, 10)]
    public float damping;
    [Range(0, 1)]
    public float displacement;

    [HideInInspector]
    public float currentHeight;
    [HideInInspector]
    public Vector3 currentNormal;

    void Awake() {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        objects.Add(this);
    }
    void OnDestroy()
    {
        objects.Remove(this);
    }

    void FixedUpdate() {
        float depth = currentHeight - transform.position.y;

        if (depth > 0) {
            depth = Mathf.Clamp01(depth);

            float volume = rb.mass / density;

            currentNormal = Vector3.Lerp(currentNormal, Vector3.up, 1 - displacement);

            Vector3 force = currentNormal * depth * volume * Gravity;
            
            float vn = Vector3.Dot(rb.velocity, currentNormal);
            Vector3 dapmingForce = -currentNormal * vn * damping;

            Vector3 dragForce = -rb.velocity * rb.angularDrag;

            force = Vector3.ClampMagnitude(force, volume * Gravity);

            rb.AddForceAtPosition(force + dapmingForce + dragForce, transform.position);
        }
    }
}
