using System;
using UnityEngine;

// A visible physics projectile for the aiming test, without damage or inventory.
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class CCTVTestProjectile : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 10;
    [SerializeField, Min(0.1f)] private float lifetime = 5;

    private Action<string> onFinished;
    private float remaining;

    public void Launch(Vector3 direction, Action<string> finished)
    {
        onFinished = finished;
        remaining = lifetime;
        GetComponent<Rigidbody>().linearVelocity = direction * speed;
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0) Finish(null);
    }

    private void OnCollisionEnter(Collision collision) => Finish(collision.collider.gameObject.name);

    private void Finish(string objectName)
    {
        if (onFinished == null) return;
        var finished = onFinished;
        onFinished = null;
        finished(objectName);
        Destroy(gameObject);
    }
}
