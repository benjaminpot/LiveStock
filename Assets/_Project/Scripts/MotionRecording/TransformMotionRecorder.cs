using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LiveStock.MotionRecording
{
    [DisallowMultipleComponent]
    public sealed class TransformMotionRecorder : MonoBehaviour
    {
        private const string MetadataFileName = "metadata.json";
        private const string BinaryFramesFileName = "frames.bytes";
        private const string JsonFramesFileName = "frames.jsonl";

        [Header("Recording")]
        [SerializeField, Min(1)] private int sampleRate = 60;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool startRecordingOnPlay;
        [SerializeField, Min(1)] private int maxCatchUpSamplesPerFrame = 2;

        [Header("Output")]
        [SerializeField] private RecordingOutputLocation outputLocation = RecordingOutputLocation.ProjectRoot;
        [SerializeField] private RecordingFrameFormat frameFormat = RecordingFrameFormat.BinaryFloat32;
        [SerializeField] private string sessionName = "vr-motion";
        [SerializeField] private string recordingsFolderName = "MotionRecordings";
        [SerializeField, Min(1)] private int flushEveryNFrames = 60;

        [Header("Objects To Record")]
        [Tooltip("Add any number of entries and drag the desired Transforms here.")]
        [SerializeField] private List<TransformRecordingBinding> objectsToRecord = new List<TransformRecordingBinding>();

        private readonly List<RuntimeBinding> _activeBindings = new List<RuntimeBinding>();
        private StreamWriter _jsonFramesWriter;
        private BinaryWriter _binaryFramesWriter;
        private TransformMotionMetadata _metadata;
        private double _startTime;
        private double _nextSampleTime;
        private double _sampleInterval;
        private int _frameCount;
        private int _droppedSampleCount;
        private RecordingFrameFormat _activeFrameFormat;

        public bool IsRecording { get; private set; }
        public string LastRecordingDirectory { get; private set; }
        public int RecordedFrameCount => _frameCount;
        public IReadOnlyList<TransformRecordingBinding> ObjectsToRecord => objectsToRecord;
        public RecordingOutputLocation OutputLocation => outputLocation;

        private sealed class RuntimeBinding
        {
            public string Id;
            public Transform Target;
            public TransformCoordinateSpace CoordinateSpace;
            public Transform ReferenceSpace;
            public bool RecordPosition;
            public bool RecordRotation;
            public bool RecordScale;
        }

        private void Start()
        {
            if (startRecordingOnPlay)
            {
                StartRecording();
            }
        }

        private void LateUpdate()
        {
            if (!IsRecording)
            {
                return;
            }

            double elapsed = CurrentTime - _startTime;
            int samplesThisFrame = 0;

            while (elapsed + 0.000001d >= _nextSampleTime && samplesThisFrame < maxCatchUpSamplesPerFrame)
            {
                CaptureFrame(_nextSampleTime);
                _nextSampleTime += _sampleInterval;
                samplesThisFrame++;
            }

            if (elapsed >= _nextSampleTime)
            {
                int skipped = Math.Max(1, (int)Math.Floor((elapsed - _nextSampleTime) / _sampleInterval) + 1);
                _droppedSampleCount += skipped;
                _nextSampleTime += skipped * _sampleInterval;
            }
        }

        [ContextMenu("Start Recording")]
        public void StartRecording()
        {
            if (IsRecording)
            {
                Debug.LogWarning("TransformMotionRecorder is already recording.", this);
                return;
            }

            if (!BuildRuntimeBindings())
            {
                Debug.LogError("Recording did not start because no valid Transform was assigned.", this);
                return;
            }

            try
            {
#if !UNITY_EDITOR
                if (outputLocation == RecordingOutputLocation.ProjectRoot)
                {
                    Debug.LogWarning(
                        "Project Root output is only available in the Unity Editor. This build will use Application.persistentDataPath instead.",
                        this);
                }
#endif
                int validatedSampleRate = Mathf.Max(1, sampleRate);
                string recordingId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
                string safeSessionName = MakeFileNameSafe(sessionName);
                string directoryName = string.IsNullOrEmpty(safeSessionName)
                    ? recordingId
                    : $"{recordingId}_{safeSessionName}";

                LastRecordingDirectory = Path.Combine(GetRecordingRootPath(), directoryName);

                Directory.CreateDirectory(LastRecordingDirectory);
                _activeFrameFormat = frameFormat;
                _metadata = CreateMetadata(recordingId, validatedSampleRate);
                WriteMetadata();

                string framesPath = Path.Combine(LastRecordingDirectory, _metadata.frameFileName);
                OpenFrameWriter(framesPath, validatedSampleRate);

                _frameCount = 0;
                _droppedSampleCount = 0;
                _sampleInterval = 1d / validatedSampleRate;
                _startTime = CurrentTime;
                _nextSampleTime = 0d;
                IsRecording = true;

                Debug.Log($"Motion recording started. Output: {LastRecordingDirectory}", this);
            }
            catch (Exception exception)
            {
                CloseWriter();
                _activeBindings.Clear();
                Debug.LogException(exception, this);
            }
        }

        [ContextMenu("Stop Recording")]
        public void StopRecording()
        {
            if (!IsRecording)
            {
                return;
            }

            double duration = Math.Max(0d, CurrentTime - _startTime);
            IsRecording = false;

            try
            {
                CloseWriter();
                _metadata.completed = true;
                _metadata.completedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                _metadata.frameCount = _frameCount;
                _metadata.droppedSampleCount = _droppedSampleCount;
                _metadata.durationSeconds = duration;
                WriteMetadata();

                Debug.Log(
                    $"Motion recording stopped. Frames: {_frameCount}, dropped samples: {_droppedSampleCount}, output: {LastRecordingDirectory}",
                    this);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                _activeBindings.Clear();
            }
        }

        public void ToggleRecording()
        {
            if (IsRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        [ContextMenu("Log Recording Root Path")]
        public void LogRecordingRootPath()
        {
            Debug.Log($"Motion recordings are stored under: {GetRecordingRootPath()}", this);
        }

        public string GetRecordingRootPath()
        {
            string folderName = MakeFileNameSafe(recordingsFolderName, "MotionRecordings");

            if (outputLocation == RecordingOutputLocation.ProjectRoot)
            {
#if UNITY_EDITOR
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(projectRoot, folderName);
#else
                return Path.Combine(Application.persistentDataPath, folderName);
#endif
            }

            return Path.Combine(Application.persistentDataPath, folderName);
        }

        private void CaptureFrame(double sampleTime)
        {
            if (_activeFrameFormat == RecordingFrameFormat.BinaryFloat32)
            {
                CaptureBinaryFrame(sampleTime);
            }
            else
            {
                CaptureJsonFrame(sampleTime);
            }

            _frameCount++;

            if (_frameCount % Mathf.Max(1, flushEveryNFrames) == 0)
            {
                FlushWriter();
            }
        }

        private void CaptureJsonFrame(double sampleTime)
        {
            var frame = new TransformMotionFrame
            {
                frameIndex = _frameCount,
                time = sampleTime,
                samples = new RecordedTransformSample[_activeBindings.Count]
            };

            for (int i = 0; i < _activeBindings.Count; i++)
            {
                RuntimeBinding runtimeBinding = _activeBindings[i];
                Transform target = runtimeBinding.Target;
                GetPose(runtimeBinding, out Vector3 position, out Quaternion rotation);

                frame.samples[i] = new RecordedTransformSample
                {
                    id = runtimeBinding.Id,
                    activeInHierarchy = target.gameObject.activeInHierarchy,
                    position = runtimeBinding.RecordPosition ? position : Vector3.zero,
                    rotation = runtimeBinding.RecordRotation ? rotation : Quaternion.identity,
                    localScale = runtimeBinding.RecordScale ? target.localScale : Vector3.one
                };
            }

            _jsonFramesWriter.WriteLine(JsonUtility.ToJson(frame));
        }

        private void CaptureBinaryFrame(double sampleTime)
        {
            _binaryFramesWriter.Write(_frameCount);
            _binaryFramesWriter.Write(sampleTime);

            for (int i = 0; i < _activeBindings.Count; i++)
            {
                RuntimeBinding runtimeBinding = _activeBindings[i];
                Transform target = runtimeBinding.Target;
                GetPose(runtimeBinding, out Vector3 position, out Quaternion rotation);

                _binaryFramesWriter.Write(target.gameObject.activeInHierarchy ? (byte)1 : (byte)0);

                if (runtimeBinding.RecordPosition)
                {
                    WriteVector3(_binaryFramesWriter, position);
                }

                if (runtimeBinding.RecordRotation)
                {
                    WriteQuaternion(_binaryFramesWriter, rotation);
                }

                if (runtimeBinding.RecordScale)
                {
                    WriteVector3(_binaryFramesWriter, target.localScale);
                }
            }
        }

        private static void GetPose(
            RuntimeBinding binding,
            out Vector3 position,
            out Quaternion rotation)
        {
            Transform target = binding.Target;
            bool useReference = binding.CoordinateSpace == TransformCoordinateSpace.RelativeToReference
                && binding.ReferenceSpace != null;

            position = useReference
                ? binding.ReferenceSpace.InverseTransformPoint(target.position)
                : target.position;
            rotation = useReference
                ? Quaternion.Inverse(binding.ReferenceSpace.rotation) * target.rotation
                : target.rotation;
        }

        private bool BuildRuntimeBindings()
        {
            _activeBindings.Clear();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < objectsToRecord.Count; i++)
            {
                TransformRecordingBinding binding = objectsToRecord[i];
                if (binding == null || binding.target == null)
                {
                    Debug.LogWarning($"Objects To Record element {i} is empty and will be skipped.", this);
                    continue;
                }

                string baseId = string.IsNullOrWhiteSpace(binding.id)
                    ? GetHierarchyPath(binding.target)
                    : binding.id.Trim();
                string uniqueId = baseId;
                int suffix = 2;

                while (!usedIds.Add(uniqueId))
                {
                    uniqueId = $"{baseId}_{suffix++}";
                }

                _activeBindings.Add(new RuntimeBinding
                {
                    Id = uniqueId,
                    Target = binding.target,
                    CoordinateSpace = binding.coordinateSpace,
                    ReferenceSpace = binding.referenceSpace,
                    RecordPosition = binding.recordPosition,
                    RecordRotation = binding.recordRotation,
                    RecordScale = binding.recordScale
                });
            }

            return _activeBindings.Count > 0;
        }

        private TransformMotionMetadata CreateMetadata(string recordingId, int validatedSampleRate)
        {
            var tracks = new TransformTrackDefinition[_activeBindings.Count];

            for (int i = 0; i < _activeBindings.Count; i++)
            {
                RuntimeBinding runtimeBinding = _activeBindings[i];
                bool hasReference = runtimeBinding.CoordinateSpace == TransformCoordinateSpace.RelativeToReference
                    && runtimeBinding.ReferenceSpace != null;

                tracks[i] = new TransformTrackDefinition
                {
                    id = runtimeBinding.Id,
                    targetName = runtimeBinding.Target.name,
                    targetHierarchyPath = GetHierarchyPath(runtimeBinding.Target),
                    coordinateSpace = hasReference ? "relativeToReference" : "world",
                    referenceHierarchyPath = hasReference ? GetHierarchyPath(runtimeBinding.ReferenceSpace) : string.Empty,
                    recordsPosition = runtimeBinding.RecordPosition,
                    recordsRotation = runtimeBinding.RecordRotation,
                    recordsLocalScale = runtimeBinding.RecordScale
                };
            }

            var metadata = new TransformMotionMetadata
            {
                recordingId = recordingId,
                sessionName = sessionName,
                createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                completedUtc = string.Empty,
                unityVersion = Application.unityVersion,
                sceneName = SceneManager.GetActiveScene().name,
                outputLocation = GetEffectiveOutputLocationName(),
                frameFormat = _activeFrameFormat == RecordingFrameFormat.BinaryFloat32 ? "binaryFloat32" : "jsonLines",
                frameFileName = _activeFrameFormat == RecordingFrameFormat.BinaryFloat32
                    ? BinaryFramesFileName
                    : JsonFramesFileName,
                binaryFormatVersion = _activeFrameFormat == RecordingFrameFormat.BinaryFloat32
                    ? TransformMotionBinaryFormat.Version
                    : 0,
                byteOrder = _activeFrameFormat == RecordingFrameFormat.BinaryFloat32 ? "littleEndian" : string.Empty,
                sampleRate = validatedSampleRate,
                usesUnscaledTime = useUnscaledTime,
                completed = false,
                frameCount = 0,
                droppedSampleCount = 0,
                durationSeconds = 0d,
                tracks = tracks
            };

            metadata.binaryBytesPerFrame = _activeFrameFormat == RecordingFrameFormat.BinaryFloat32
                ? TransformMotionBinaryFormat.GetBytesPerFrame(tracks)
                : 0;

            return metadata;
        }

        private void WriteMetadata()
        {
            string metadataPath = Path.Combine(LastRecordingDirectory, MetadataFileName);
            File.WriteAllText(metadataPath, JsonUtility.ToJson(_metadata, true), new UTF8Encoding(false));
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && IsRecording)
            {
                FlushWriter();
            }
        }

        private void OnDisable()
        {
            StopRecording();
        }

        private void OnApplicationQuit()
        {
            StopRecording();
        }

        private void CloseWriter()
        {
            FlushWriter();

            if (_jsonFramesWriter != null)
            {
                _jsonFramesWriter.Dispose();
                _jsonFramesWriter = null;
            }

            if (_binaryFramesWriter != null)
            {
                _binaryFramesWriter.Dispose();
                _binaryFramesWriter = null;
            }
        }

        private void OpenFrameWriter(string framesPath, int validatedSampleRate)
        {
            if (_activeFrameFormat == RecordingFrameFormat.BinaryFloat32)
            {
                var stream = new FileStream(
                    framesPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.SequentialScan);
                _binaryFramesWriter = new BinaryWriter(stream, new UTF8Encoding(false), false);
                TransformMotionBinaryFormat.WriteHeader(
                    _binaryFramesWriter,
                    _activeBindings.Count,
                    validatedSampleRate);
                return;
            }

            _jsonFramesWriter = new StreamWriter(framesPath, false, new UTF8Encoding(false), 16 * 1024);
        }

        private void FlushWriter()
        {
            _jsonFramesWriter?.Flush();
            _binaryFramesWriter?.Flush();
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
            writer.Write(value.w);
        }

        private double CurrentTime => useUnscaledTime
            ? Time.realtimeSinceStartupAsDouble
            : Time.timeAsDouble;

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = $"{current.name}/{path}";
                current = current.parent;
            }

            return path;
        }

        private static string MakeFileNameSafe(string value, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            var invalidCharacters = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                builder.Append(invalidCharacters.Contains(character) ? '_' : character);
            }

            string result = builder.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(result) ? fallback : result;
        }

        private string GetEffectiveOutputLocationName()
        {
            if (outputLocation == RecordingOutputLocation.ProjectRoot)
            {
#if UNITY_EDITOR
                return "projectRoot";
#else
                return "persistentDataPathFallback";
#endif
            }

            return "persistentDataPath";
        }
    }
}
