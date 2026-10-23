using System;
using LiveStock.Multiplayer;
using UnityEngine;

// Server-side projectile used by Render Streaming CCTV aiming.
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class CCTVTestProjectile : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float speed = 10;
    [SerializeField, Min(0.1f)] private float lifetime = 5;

    [SerializeField, Min(1)] private int damage = 1;

    private Action<string, string> onFinished;
    private float remaining;

    public void Launch(Vector3 direction, Action<string, string> finished)
    {
        onFinished = finished;
        remaining = lifetime;
        GetComponent<Rigidbody>().linearVelocity = direction * speed;
    }

    private void Update()
    {
        remaining -= Time.deltaTime;
        if (remaining <= 0) Finish("miss", null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        var target = collision.collider.GetComponentInParent<NetworkNpcAuthorityGate>();
        if (target != null && target.TryApplyDamage(damage))
        {
            Finish("kill", target.gameObject.name);
            return;
        }

        Finish("hit", collision.collider.gameObject.name);
    }

    private void Finish(string status, string objectName)
    {
        if (onFinished == null) return;
        var finished = onFinished;
        onFinished = null;
        finished(status, objectName);
        Destroy(gameObject);
    }
}
