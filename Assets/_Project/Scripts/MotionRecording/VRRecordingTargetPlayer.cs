using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveStock.MotionRecording
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class VRRecordingTargetPlayer : MonoBehaviour
    {
        [Serializable]
        public sealed class MotionEntry
        {
            public string actionName = "New Motion";
            public MotionRecordingAsset recording;
            [Tooltip("Used when Recording is empty: a matching metadata.json and frames.bytes pair.")]
            public TextAsset metadata;
            public TextAsset binaryFrames;
        }

        [Header("Motion List")]
        public List<MotionEntry> motions = new List<MotionEntry>();
        [Tooltip("Index played on start. Zero selects the first motion.")]
        [Min(0)] public int defaultMotionIndex;

        [Header("Recording")]
        public MotionRecordingAsset recording;
        [Tooltip("Used when Recording is empty: a matching metadata.json and frames.bytes pair.")]
        public TextAsset metadata;
        public TextAsset binaryFrames;

        [Header("Three Targets")]
        public Transform headTarget;
        public Transform leftHandTarget;
        public Transform rightHandTarget;
        [Tooltip("Local space used for mapping. Defaults to this component's Transform.")]
        public Transform mappingSpace;
        public bool applyHandRotation;

        [Header("Mapping")]
        [Tooltip("Apply recorded changes to each target's initial pose. Disable to map recorded local poses directly.")]
        public bool alignToInitialTargets = true;
        [Tooltip("Optional shared calibration, such as Idle. Otherwise uses the selected recording's first frame. Only used when Align To Initial Targets is enabled.")]
        public MotionRecordingAsset calibrationRecording;
        [Min(0.01f)] public float positionScale = 1;
        public Vector3 axisCorrectionEuler;

        [Header("Playback")]
        public bool playOnStart = true;
        public bool loop = true;
        [Min(0.01f)] public float playbackSpeed = 1;

        public bool IsPlaying { get; private set; }
        public string CurrentMotionName { get; private set; } = "";
        public float PlaybackTime { get; private set; }
        public float Duration => _frames == null ? 0 : (float)(_frames[_frames.Count - 1].time - _frames[0].time);

        private static readonly string[] TrackIds = { "head", "L-hand", "R-hand" };
        private List<TransformMotionFrame> _frames;
        private int[] _tracks;
        private RecordedTransformSample[] _neutral;
        private Transform[] _targets;
        private Pose[] _initial;
        private Transform _space;

        private void Awake()
        {
            if (headTarget != null && leftHandTarget != null && rightHandTarget != null)
                CaptureTargets();
        }

        private void Start()
        {
            if (playOnStart) Play();
        }

        private void Update()
        {
            if (_frames == null) return;
            if (IsPlaying)
            {
                PlaybackTime += Time.deltaTime * Mathf.Max(0.01f, playbackSpeed);
                if (PlaybackTime >= Duration)
                {
                    if (loop && Duration > 0) PlaybackTime %= Duration;
                    else { PlaybackTime = Duration; IsPlaying = false; }
                }
            }
            ApplyFrame();
        }

        [ContextMenu("Play Recording")]
        public void Play()
        {
            if (motions != null && motions.Count > 0) Play(defaultMotionIndex);
            else PlaySource(recording != null ? recording.MotionName : "Custom", recording, metadata, binaryFrames);
        }

        public void Play(int index)
        {
            if (motions == null || index < 0 || index >= motions.Count || motions[index] == null)
            {
                Debug.LogWarning("VR target playback: invalid motion index " + index, this);
                return;
            }
            var entry = motions[index];
            string actionName = string.IsNullOrWhiteSpace(entry.actionName) ? "Motion " + (index + 1) : entry.actionName;
            PlaySource(actionName, entry.recording, entry.metadata, entry.binaryFrames);
        }

        private void PlaySource(string actionName, MotionRecordingAsset source, TextAsset sourceMetadata, TextAsset sourceFrames)
        {
            try
            {
                ValidateTargets();
                if (_initial == null) CaptureTargets();
                List<TransformMotionFrame> frames;
                TransformMotionMetadata info;
                if (source != null) frames = source.LoadFrames(out info);
                else
                {
                    if (sourceMetadata == null || sourceFrames == null)
                        throw new InvalidOperationException("Assign Recording, or Metadata + Binary Frames.");
                    frames = TransformMotionBinaryFormat.ReadAllFrames(sourceMetadata.text, sourceFrames.bytes, out info);
                }
                if (frames.Count == 0) throw new InvalidOperationException("Recording is empty.");
                var tracks = FindTracks(info);
                var calibration = frames;
                var calibrationTracks = tracks;
                if (alignToInitialTargets && calibrationRecording != null)
                {
                    calibration = calibrationRecording.LoadFrames(out var calibrationInfo);
                    calibrationTracks = FindTracks(calibrationInfo);
                }
                if (calibration.Count == 0) throw new InvalidOperationException("Calibration recording is empty.");
                var neutral = new RecordedTransformSample[3];
                for (int i = 0; i < 3; i++)
                {
                    neutral[i] = calibration[0].samples[calibrationTracks[i]];
                    if (alignToInitialTargets && !neutral[i].activeInHierarchy)
                        throw new InvalidOperationException("Calibration track is inactive: " + TrackIds[i]);
                }
                _frames = frames;
                _tracks = tracks;
                _neutral = neutral;
                PlaybackTime = 0;
                CurrentMotionName = actionName;
                IsPlaying = true;
                ApplyFrame();
            }
            catch (Exception error)
            {
                _frames = null;
                IsPlaying = false;
                CurrentMotionName = "";
                PlaybackTime = 0;
                Debug.LogError("VR target playback: " + error.Message, this);
            }
        }

        [ContextMenu("Pause")]
        public void Pause() => IsPlaying = false;

        [ContextMenu("Resume")]
        public void Resume() { if (_frames != null) IsPlaying = true; }

        public void Seek(float seconds)
        {
            if (_frames == null) return;
            PlaybackTime = Mathf.Clamp(seconds, 0, Duration);
            ApplyFrame();
        }

        [ContextMenu("Stop and Restore Targets")]
        public void Stop()
        {
            _frames = null;
            IsPlaying = false;
            PlaybackTime = 0;
            CurrentMotionName = "";
            if (_initial == null) return;
            for (int i = 0; i < 3; i++)
                if (_targets[i] != null) SetTarget(i, _initial[i].position, _initial[i].rotation, true);
        }

        private void ValidateTargets()
        {
            var targets = new[] { headTarget, leftHandTarget, rightHandTarget };
            Transform space = mappingSpace != null ? mappingSpace : transform;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) throw new InvalidOperationException("Assign all three Targets.");
                if (space == targets[i] || space.IsChildOf(targets[i]))
                    throw new InvalidOperationException("Mapping Space cannot be a Target or its child.");
                for (int j = 0; j < i; j++)
                    if (targets[i] == targets[j] || targets[i].IsChildOf(targets[j]) || targets[j].IsChildOf(targets[i]))
                        throw new InvalidOperationException("Targets must be distinct and independent of one another.");
            }
            if (_initial != null && (_space != space || _targets[0] != headTarget ||
                _targets[1] != leftHandTarget || _targets[2] != rightHandTarget))
                throw new InvalidOperationException("Assign Targets and Mapping Space before playback, then restart Play Mode.");
        }

        private void CaptureTargets()
        {
            _space = mappingSpace != null ? mappingSpace : transform;
            _targets = new[] { headTarget, leftHandTarget, rightHandTarget };
            _initial = new Pose[3];
            for (int i = 0; i < 3; i++)
                _initial[i] = new Pose(_space.InverseTransformPoint(_targets[i].position),
                    Quaternion.Inverse(_space.rotation) * _targets[i].rotation);
        }

        private void ApplyFrame()
        {
            double time = _frames[0].time + PlaybackTime;
            int low = 0, high = _frames.Count - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (_frames[middle].time < time) low = middle + 1;
                else high = middle;
            }
            var a = _frames[Mathf.Max(0, low - 1)];
            var b = _frames[low];
            float blend = b.time > a.time ? Mathf.Clamp01((float)((time - a.time) / (b.time - a.time))) : 0;
            Quaternion basis = Quaternion.Euler(axisCorrectionEuler);
            for (int i = 0; i < 3; i++)
            {
                if (_targets[i] == null) continue;
                var before = a.samples[_tracks[i]];
                var after = b.samples[_tracks[i]];
                if (!before.activeInHierarchy || !after.activeInHierarchy) continue;
                Vector3 position = Vector3.Lerp(before.position, after.position, blend);
                Quaternion rotation = Quaternion.Slerp(before.rotation, after.rotation, blend);
                if (alignToInitialTargets)
                {
                    position = _initial[i].position + basis * (position - _neutral[i].position) * positionScale;
                    rotation = basis * (rotation * Quaternion.Inverse(_neutral[i].rotation)) *
                        Quaternion.Inverse(basis) * _initial[i].rotation;
                }
                else
                {
                    position = basis * position * positionScale;
                    rotation = basis * rotation;
                }
                SetTarget(i, position, rotation, i == 0 || applyHandRotation);
            }
        }

        private void SetTarget(int index, Vector3 position, Quaternion rotation, bool rotate)
        {
            _targets[index].position = _space.TransformPoint(position);
            if (rotate) _targets[index].rotation = _space.rotation * rotation;
        }

        private static int[] FindTracks(TransformMotionMetadata info)
        {
            var result = new int[3];
            for (int i = 0; i < 3; i++)
            {
                result[i] = Array.FindIndex(info.tracks, x => x.id == TrackIds[i]);
                if (result[i] < 0) throw new InvalidOperationException("Missing track: " + TrackIds[i]);
                var track = info.tracks[result[i]];
                if (!track.recordsPosition || !track.recordsRotation || track.coordinateSpace != "relativeToReference")
                    throw new InvalidOperationException("Expected TrackingSpace-relative position/rotation: " + TrackIds[i]);
            }
            return result;
        }
    }
}
