using System;
using UnityEngine;

namespace LiveStock.MotionRecording
{
    public enum TransformCoordinateSpace
    {
        World,
        RelativeToReference
    }

    public enum RecordingOutputLocation
    {
        ProjectRoot,
        PersistentDataPath
    }

    public enum RecordingFrameFormat
    {
        BinaryFloat32,
        JsonLines
    }

    [Serializable]
    public sealed class TransformRecordingBinding
    {
        [Tooltip("Stable name written to the recording. Leave blank to use the target hierarchy path.")]
        public string id;

        [Tooltip("Drag the HMD, controller, IK target, animal bone, or any other Transform to record.")]
        public Transform target;

        [Tooltip("World records Unity world coordinates. Relative To Reference makes the recording reusable elsewhere.")]
        public TransformCoordinateSpace coordinateSpace = TransformCoordinateSpace.RelativeToReference;

        [Tooltip("Origin used by Relative To Reference, such as TrackingSpace or the animal root. If empty, world space is used.")]
        public Transform referenceSpace;

        public bool recordPosition = true;
        public bool recordRotation = true;

        [Tooltip("Scale is always target.localScale because non-uniform relative scale is ambiguous.")]
        public bool recordScale;
    }

    [Serializable]
    public sealed class TransformTrackDefinition
    {
        public string id;
        public string targetName;
        public string targetHierarchyPath;
        public string coordinateSpace;
        public string referenceHierarchyPath;
        public bool recordsPosition;
        public bool recordsRotation;
        public bool recordsLocalScale;
    }

    [Serializable]
    public sealed class TransformMotionMetadata
    {
        public int formatVersion = 2;
        public string recordingId;
        public string sessionName;
        public string createdUtc;
        public string completedUtc;
        public string unityVersion;
        public string sceneName;
        public string outputLocation;
        public string frameFormat;
        public string frameFileName;
        public int binaryFormatVersion;
        public string byteOrder;
        public int binaryBytesPerFrame;
        public int sampleRate;
        public bool usesUnscaledTime;
        public bool completed;
        public int frameCount;
        public int droppedSampleCount;
        public double durationSeconds;
        public TransformTrackDefinition[] tracks;
    }

    [Serializable]
    public sealed class RecordedTransformSample
    {
        public string id;
        public bool activeInHierarchy;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
    }

    [Serializable]
    public sealed class TransformMotionFrame
    {
        public int frameIndex;
        public double time;
        public RecordedTransformSample[] samples;
    }
}
