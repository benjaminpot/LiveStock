using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LiveStock.MotionRecording
{
    public static class TransformMotionBinaryFormat
    {
        public const ushort Version = 1;
        public const int HeaderSize = 16;

        private static readonly byte[] Magic = { (byte)'L', (byte)'S', (byte)'M', (byte)'R' };

        public static void WriteHeader(BinaryWriter writer, int trackCount, int sampleRate)
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write((ushort)0);
            writer.Write(trackCount);
            writer.Write(sampleRate);
        }

        public static int GetBytesPerFrame(TransformTrackDefinition[] tracks)
        {
            int size = sizeof(int) + sizeof(double);
            if (tracks == null)
            {
                return size;
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                TransformTrackDefinition track = tracks[i];
                size += sizeof(byte);
                if (track.recordsPosition)
                {
                    size += sizeof(float) * 3;
                }

                if (track.recordsRotation)
                {
                    size += sizeof(float) * 4;
                }

                if (track.recordsLocalScale)
                {
                    size += sizeof(float) * 3;
                }
            }

            return size;
        }

        public static List<TransformMotionFrame> ReadAllFrames(string recordingDirectory)
        {
            if (string.IsNullOrWhiteSpace(recordingDirectory))
            {
                throw new ArgumentException("Recording directory is required.", nameof(recordingDirectory));
            }

            string metadataPath = Path.Combine(recordingDirectory, "metadata.json");
            TransformMotionMetadata metadata = JsonUtility.FromJson<TransformMotionMetadata>(File.ReadAllText(metadataPath));
            if (metadata == null || metadata.tracks == null)
            {
                throw new InvalidDataException("Recording metadata is missing its track definitions.");
            }

            if (!string.Equals(metadata.frameFormat, "binaryFloat32", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The recording does not use the binaryFloat32 frame format.");
            }

            string frameFileName = string.IsNullOrEmpty(metadata.frameFileName) ? "frames.bytes" : metadata.frameFileName;
            string framesPath = Path.Combine(recordingDirectory, frameFileName);

            using (var stream = new FileStream(framesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream))
            {
                ValidateHeader(reader, metadata);

                int bytesPerFrame = GetBytesPerFrame(metadata.tracks);
                long completeFrameCount = (stream.Length - stream.Position) / bytesPerFrame;
                int capacity = completeFrameCount > int.MaxValue ? int.MaxValue : (int)completeFrameCount;
                var frames = new List<TransformMotionFrame>(capacity);

                for (long frameNumber = 0; frameNumber < completeFrameCount; frameNumber++)
                {
                    var frame = new TransformMotionFrame
                    {
                        frameIndex = reader.ReadInt32(),
                        time = reader.ReadDouble(),
                        samples = new RecordedTransformSample[metadata.tracks.Length]
                    };

                    for (int trackIndex = 0; trackIndex < metadata.tracks.Length; trackIndex++)
                    {
                        TransformTrackDefinition track = metadata.tracks[trackIndex];
                        byte flags = reader.ReadByte();
                        frame.samples[trackIndex] = new RecordedTransformSample
                        {
                            id = track.id,
                            activeInHierarchy = (flags & 1) != 0,
                            position = track.recordsPosition ? ReadVector3(reader) : Vector3.zero,
                            rotation = track.recordsRotation ? ReadQuaternion(reader) : Quaternion.identity,
                            localScale = track.recordsLocalScale ? ReadVector3(reader) : Vector3.one
                        };
                    }

                    frames.Add(frame);
                }

                return frames;
            }
        }

        public static List<TransformMotionFrame> ReadAllFrames(
            string metadataJson,
            byte[] frameBytes,
            out TransformMotionMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                throw new ArgumentException("Recording metadata JSON is required.", nameof(metadataJson));
            }

            if (frameBytes == null || frameBytes.Length == 0)
            {
                throw new ArgumentException("Recording frame bytes are required.", nameof(frameBytes));
            }

            metadata = JsonUtility.FromJson<TransformMotionMetadata>(metadataJson);
            if (metadata == null || metadata.tracks == null)
            {
                throw new InvalidDataException("Recording metadata is missing its track definitions.");
            }

            if (!string.Equals(metadata.frameFormat, "binaryFloat32", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The recording does not use the binaryFloat32 frame format.");
            }

            using (var stream = new MemoryStream(frameBytes, false))
            using (var reader = new BinaryReader(stream))
            {
                ValidateHeader(reader, metadata);
                return ReadFrames(reader, stream, metadata);
            }
        }

        private static List<TransformMotionFrame> ReadFrames(
            BinaryReader reader,
            Stream stream,
            TransformMotionMetadata metadata)
        {
            int bytesPerFrame = GetBytesPerFrame(metadata.tracks);
            long completeFrameCount = (stream.Length - stream.Position) / bytesPerFrame;
            int capacity = completeFrameCount > int.MaxValue ? int.MaxValue : (int)completeFrameCount;
            var frames = new List<TransformMotionFrame>(capacity);

            for (long frameNumber = 0; frameNumber < completeFrameCount; frameNumber++)
            {
                var frame = new TransformMotionFrame
                {
                    frameIndex = reader.ReadInt32(),
                    time = reader.ReadDouble(),
                    samples = new RecordedTransformSample[metadata.tracks.Length]
                };

                for (int trackIndex = 0; trackIndex < metadata.tracks.Length; trackIndex++)
                {
                    TransformTrackDefinition track = metadata.tracks[trackIndex];
                    byte flags = reader.ReadByte();
                    frame.samples[trackIndex] = new RecordedTransformSample
                    {
                        id = track.id,
                        activeInHierarchy = (flags & 1) != 0,
                        position = track.recordsPosition ? ReadVector3(reader) : Vector3.zero,
                        rotation = track.recordsRotation ? ReadQuaternion(reader) : Quaternion.identity,
                        localScale = track.recordsLocalScale ? ReadVector3(reader) : Vector3.one
                    };
                }

                frames.Add(frame);
            }

            return frames;
        }

        private static void ValidateHeader(BinaryReader reader, TransformMotionMetadata metadata)
        {
            byte[] magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length
                || magic[0] != Magic[0]
                || magic[1] != Magic[1]
                || magic[2] != Magic[2]
                || magic[3] != Magic[3])
            {
                throw new InvalidDataException("Invalid motion recording binary magic.");
            }

            ushort version = reader.ReadUInt16();
            reader.ReadUInt16();
            int trackCount = reader.ReadInt32();
            int sampleRate = reader.ReadInt32();

            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported motion binary version {version}.");
            }

            if (trackCount != metadata.tracks.Length || sampleRate != metadata.sampleRate)
            {
                throw new InvalidDataException("Binary header does not match metadata.json.");
            }
        }

        private static Vector3 ReadVector3(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        private static Quaternion ReadQuaternion(BinaryReader reader)
        {
            return new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
