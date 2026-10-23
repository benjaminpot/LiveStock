using Fusion;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    public struct MoveInput : INetworkInput
    {
        public NetworkBool IsVR;
        public Vector3 Root, Head, Left, Right;
        public Quaternion RootRotation, HeadRotation, LeftRotation, RightRotation;
    }

    public class NetworkPlayer : NetworkBehaviour
    {
        public GameObject LocalVRPrefab, RemoteVRPrefab;
        [Networked] public NetworkBool IsVR { get; set; }
        [Networked] Vector3 Head { get; set; }
        [Networked] Vector3 Left { get; set; }
        [Networked] Vector3 Right { get; set; }
        [Networked] Quaternion HeadRotation { get; set; }
        [Networked] Quaternion LeftRotation { get; set; }
        [Networked] Quaternion RightRotation { get; set; }
        GameObject visual;
        LiveStock.MotionRecording.VRRecordingTargetPlayer targets;
        bool visualRole;
        bool visualInitialized;
        Material observerMaterial;
        public override void Spawned()
        {
            if (Object.HasInputAuthority) FindFirstObjectByType<SessionLauncher>().CreateLocalRole(Runner, transform.position);
        }
        public override void FixedUpdateNetwork()
        {
            if (!GetInput(out MoveInput data)) return;
            IsVR = data.IsVR;
            transform.SetPositionAndRotation(data.Root, data.RootRotation);
            Head = data.Head; Left = data.Left; Right = data.Right;
            HeadRotation = data.HeadRotation; LeftRotation = data.LeftRotation; RightRotation = data.RightRotation;
        }
        public override void Render()
        {
            if (Object.HasInputAuthority) return;
            if (!visualInitialized || visualRole != (bool)IsVR)
            {
                visualInitialized = true;
                visualRole = IsVR;
                if (visual != null) Destroy(visual);
                targets = null;
                if (observerMaterial != null) Destroy(observerMaterial);
                if (IsVR && RemoteVRPrefab != null)
                {
                    visual = Instantiate(RemoteVRPrefab, transform.position, transform.rotation, transform);
                    targets = System.Array.Find(visual.GetComponentsInChildren<LiveStock.MotionRecording.VRRecordingTargetPlayer>(true), t => t.headTarget != null && t.leftHandTarget != null && t.rightHandTarget != null);
                }
                else if (!IsVR) CreateObserverVisual();
            }
            if (targets == null) return;
            targets.headTarget.SetPositionAndRotation(Head, HeadRotation);
            targets.leftHandTarget.SetPositionAndRotation(Left, LeftRotation);
            targets.rightHandTarget.SetPositionAndRotation(Right, RightRotation);
        }
        void CreateObserverVisual()
        {
            visual = new GameObject("PC Observer Visual");
            visual.transform.SetParent(transform, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                observerMaterial = new Material(shader);
                observerMaterial.color = Color.HSVToRGB(Mathf.Repeat(Object.InputAuthority.PlayerId * .23f, 1), .65f, .95f);
            }
            AddPart("Body", PrimitiveType.Capsule, new Vector3(0, -.8f, 0), new Vector3(.6f, .8f, .6f));
            AddPart("Facing", PrimitiveType.Cube, new Vector3(0, -.15f, .4f), new Vector3(.22f, .15f, .45f));
            // These are visual markers only. The observer still uses a free-flight camera.
            Runner.AddVisibilityNodes(visual);
        }
        void AddPart(string label, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(type); part.name = label;
            part.transform.SetParent(visual.transform, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            if (observerMaterial != null) part.GetComponent<Renderer>().sharedMaterial = observerMaterial;
        }
        void OnDestroy() { if (observerMaterial != null) Destroy(observerMaterial); }
    }
}
