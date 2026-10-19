using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveStock.MotionRecording
{
    [CreateAssetMenu(
        fileName = "MotionRecording",
        menuName = "LiveStock/Motion Recording",
        order = 210)]
    public sealed class MotionRecordingAsset : ScriptableObject
    {
        [SerializeField] private string motionName;
        [SerializeField] private TextAsset metadata;
        [SerializeField] private TextAsset binaryFrames;

        public string MotionName => motionName;
        public TextAsset Metadata => metadata;
        public TextAsset BinaryFrames => binaryFrames;

        public List<TransformMotionFrame> LoadFrames(out TransformMotionMetadata loadedMetadata)
        {
            if (metadata == null || binaryFrames == null)
            {
                throw new InvalidOperationException(
                    $"Motion recording '{name}' requires both metadata and binary frame assets.");
            }

            return TransformMotionBinaryFormat.ReadAllFrames(
                metadata.text,
                binaryFrames.bytes,
                out loadedMetadata);
        }

#if UNITY_EDITOR
        public void Configure(string newMotionName, TextAsset newMetadata, TextAsset newBinaryFrames)
        {
            motionName = newMotionName;
            metadata = newMetadata;
            binaryFrames = newBinaryFrames;
        }
#endif
    }
}
