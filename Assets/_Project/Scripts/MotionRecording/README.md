# Transform Motion Recorder

`TransformMotionRecorder` records any Transforms selected in the Inspector. It has no dependency on Meta XR or Animation Rigging, so the same component can record HMD/controller anchors now and animal IK targets or bones later.

## Scene setup

1. Create an empty GameObject, for example `Motion Recorder`.
2. Add the `TransformMotionRecorder` component.
3. Expand **Objects To Record** and set the list size.
4. Drag any desired Transform into each **Target** slot.
5. For reusable VR motion, choose **Relative To Reference** and drag the Camera Rig's `TrackingSpace` into **Reference Space** for every HMD/controller entry.
6. Leave **Frame Format** at **Binary Float32** for compact recordings. Select **Json Lines** only when human-readable frames are useful for debugging.
7. Leave **Output Location** at **Project Root** to save under the current Unity project, or select **Persistent Data Path** for a build-safe user-data location.
8. Enter Play Mode and use the Inspector's **Start Recording** and **Stop Recording** buttons.

Suggested Meta XR entries for the current project:

| ID | Target | Reference Space | Data |
|---|---|---|---|
| `head` | `CenterEyeAnchor` | `TrackingSpace` | Position + Rotation |
| `left_controller` | `LeftControllerAnchor` | `TrackingSpace` | Position + Rotation |
| `right_controller` | `RightControllerAnchor` | `TrackingSpace` | Position + Rotation |

You can add or remove entries freely. For a later animal rig, add `HeadTarget`, foot IK targets, the animal root, or individual bones in exactly the same way.

## Output

The default **Project Root** setting writes recordings below:

```text
<Unity project>/MotionRecordings/<timestamp>_<session>/
├── metadata.json
└── frames.bin
```

For this project that is normally:

```text
C:\dataC\元宇宙遊戲設計\LiveStock\MotionRecordings\
```

Select **Persistent Data Path** to use `Application.persistentDataPath/MotionRecordings` instead. A Quest/Android or other non-Editor build cannot write into the Unity project, so **Project Root** automatically falls back to `Application.persistentDataPath` there. The Inspector's **Open Recording Folder** button opens the effective location.

- `metadata.json` describes the session, binary layout, and every recorded track.
- `frames.bin` contains a small header followed by fixed-layout frames using little-endian Float32 transform values.
- The metadata is written once at start and finalized at stop. If the app exits unexpectedly, complete binary frames remain recoverable, an incomplete final frame is ignored, and `completed` stays `false`.

The binary frame layout is:

```text
file header: "LSMR", version, track count, sample rate
each frame: frame index, timestamp, then each track in metadata order
each track: active flag, enabled position floats, enabled rotation floats, enabled scale floats
```

Disabled Position, Rotation, or Scale channels take no frame space. The runtime helper `TransformMotionBinaryFormat.ReadAllFrames(...)` validates the header and ignores an incomplete final frame after an interrupted write.

The recorder samples in `LateUpdate`, after ordinary `Update` animation/control changes. It uses timestamped fixed-rate sampling rather than assuming the render frame rate is stable. Disk output is buffered and flushed periodically.

## Runtime controls

The following public methods can be connected to a Unity UI Button, Input System callback wrapper, or another script:

```csharp
recorder.StartRecording();
recorder.StopRecording();
recorder.ToggleRecording();
```

## Important limitations

- A Transform alone cannot report whether an XR device is genuinely tracked. `activeInHierarchy` only records whether the GameObject is active. XR tracking validity and controller buttons should be added as separate data channels when their exact input API is selected.
- Scale is saved as `target.localScale`. Relative scale between arbitrary non-uniformly scaled objects is intentionally not inferred.
- Do not rename track IDs between recording and playback code. Blank IDs are generated from the hierarchy path and duplicate IDs receive a numeric suffix.
