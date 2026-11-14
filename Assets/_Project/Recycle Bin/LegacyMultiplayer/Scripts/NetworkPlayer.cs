using Fusion;
using UnityEngine;

namespace LiveStock.Multiplayer
{
    public struct MoveInput : INetworkInput
    {
        public NetworkBool IsVR;
        public NetworkBool HasPose;
        public Vector3 Root, Head, Left, Right;
        public Quaternion RootRotation, HeadRotation, LeftRotation, RightRotation;
    }

    public class NetworkPlayer : NetworkBehaviour
    {
        public GameObject LocalVRPrefab, RemoteVRPrefab;
        [Header("VR Stationary Pose")]
        [Tooltip("Keep the VR network root at its server-assigned position. Local headset tracking remains natural.")]
        public bool LockVRRoot = true;
        [Min(0f)] public float MaxHeadHorizontalOffset = .2f;
        [Min(0f)] public float MaxHeadVerticalOffset = .15f;
        [Networked] public NetworkBool IsVR { get; set; }
        [Networked] Vector3 Head { get; set; }
        [Networked] Vector3 Left { get; set; }
        [Networked] Vector3 Right { get; set; }
        [Networked] Quaternion HeadRotation { get; set; }
        [Networked] Quaternion LeftRotation { get; set; }
        [Networked] Quaternion RightRotation { get; set; }
        GameObject visual;
        RemoteVRPoseTargets targets;
        bool visualRole;
        bool visualInitialized;
        Material observerMaterial;
        bool hasHeadReference;
        Vector3 headReference;
        public override void Spawned()
        {
            if (Object.HasInputAuthority) FindFirstObjectByType<SessionLauncher>().CreateLocalRole(Runner, transform.position);
        }
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !GetInput(out MoveInput data) || !data.HasPose) return;
            IsVR = data.IsVR;
            if (!IsVR || !LockVRRoot)
                transform.SetPositionAndRotation(data.Root, data.RootRotation);
            if (IsVR && LockVRRoot)
            {
                if (!hasHeadReference)
                {
                    headReference = data.Head;
                    hasHeadReference = true;
                }
                var limitedHead = LimitHeadOffset(data.Head, headReference,
                    MaxHeadHorizontalOffset, MaxHeadVerticalOffset);
                // Shift the hands by the same excess translation so leaning/walking
                // beyond the limit does not leave the remote arms behind the head.
                var correction = limitedHead - data.Head;
                data.Head = limitedHead;
                data.Left += correction;
                data.Right += correction;
            }
            else hasHeadReference = false;
            Head = data.Head; Left = data.Left; Right = data.Right;
            HeadRotation = data.HeadRotation; LeftRotation = data.LeftRotation; RightRotation = data.RightRotation;
        }
        public static Vector3 LimitHeadOffset(Vector3 head, Vector3 reference, float horizontalLimit, float verticalLimit)
        {
            var offset = head - reference;
            var horizontal = Vector2.ClampMagnitude(new Vector2(offset.x, offset.z), Mathf.Max(0f, horizontalLimit));
            return reference + new Vector3(horizontal.x, Mathf.Clamp(offset.y, -Mathf.Max(0f, verticalLimit), Mathf.Max(0f, verticalLimit)), horizontal.y);
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
                    targets = visual.GetComponentInChildren<RemoteVRPoseTargets>(true);
                    Runner.AddVisibilityNodes(visual);
                    if (targets == null || !targets.IsConfigured)
                        Debug.LogError("[NetworkPlayer] RemoteVRPrefab requires configured RemoteVRPoseTargets (head/left/right).", this);
                }
                else if (IsVR) Debug.LogError("[NetworkPlayer] RemoteVRPrefab is missing.", this);
                else if (!IsVR) CreateObserverVisual();
            }
            if (targets == null) return;
            targets.ApplyPose(transform, Head, HeadRotation, Left, LeftRotation, Right, RightRotation);
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
