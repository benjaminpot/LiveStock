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

## Sheep playback

Keep VR anchors as the master recording when the motion may be reused on different animals. Anchor data is device-space source motion; IK targets already contain model-specific scale, orientation, offsets, and rig decisions. For a production capture workflow, recording both anchors and the final IK targets is useful, but replacing the anchors with targets makes the data less reusable.

The current sheep player uses a hybrid setup:

- the Animator supplies the base `idle`, `eat`, or in-place `walk` body animation;
- recorded `head` motion drives the head aim target;
- recorded `L-hand` and `R-hand` motion drive the front-foot IK targets;
- the first recorded frame is treated as the calibration origin, so absolute human TrackingSpace coordinates are never assigned directly to the sheep;
- the sheep root is never moved by the player.

Both the live VR sheep and recorded-motion NPCs now pass through
`SheepMotionRetargeter`. The retargeter stores the rig targets' rest pose in the
sheep root's coordinate system and applies only the source motion delta. This
means a sheep can be placed or rotated freely without treating the target's
original local XYZ offset as a universal VR coordinate.

For recordings made with the original scene setup, leave **Position Mode** at
**Preserve Recorded Target Offsets**. The original offsets (`head: 0,-0.62,0.21`
and both hands: `0,0,0.3`) are first rotated by their recorded VR Anchor poses.
Only the resulting motion delta is then converted into sheep-root space. This
preserves the old offset-driven arcs without parenting a rig target to an Anchor
or making the result depend on the sheep's world-facing direction. Switch to
**Direct Anchor Delta** for future recordings that were authored without those
legacy offsets.

For the live VR sheep, run:

```text
Tools > LiveStock > Configure Active Scene VR Sheep
```

This restores the three targets under the sheep's Animation Rigging hierarchy,
adds `SheepVrAnchorDriver`, and connects `TrackingSpace`, `CenterEyeAnchor`,
`LeftHandAnchor`, and `RightHandAnchor`. The current headset/controller pose is
used as the calibration origin each time the component is enabled. Use
`Recalibrate From Current VR Pose` on the driver if the player changes stance.

Recordings used by prefabs are stored under `Assets/MotionRecordings`. Binary files use the `.bytes` extension so Unity imports them as `TextAsset` objects and includes referenced recordings in builds.

To generate the reusable prefab, run:

```text
Tools > LiveStock > Create VR Motion Sheep Prefab
```

This creates `Assets/_Project/model/SK_Sheep_VRMotion.prefab`, three recording assets under `Assets/MotionRecordings/Library`, and a generated Animator Controller under `Assets/_Project/Generated/MotionPlayback`. It does not overwrite `SK_Sheep_white.prefab`.

Instantiate `SK_Sheep_VRMotion.prefab` for any number of NPC sheep. In Play Mode, use the Inspector preview buttons or call:

```csharp
player.Play("idle");
player.Play("eat");
player.Play("walk");
player.Pause();
player.Resume();
player.Stop();
```

To choose a recording directly in the Inspector, either drag a
`MotionRecordingAsset` into **Custom Recording**, or drag that session's
`metadata.json` and `frames.bytes` into **Custom Metadata** and
**Custom Binary Frames**. In Play Mode press **Play Dragged Recording**, or
enable **Use Custom Recording On Start** together with **Play On Start**.

Disable **Use Base Animation** when the recording should drive the rig without
an idle/eat/walk clip advancing underneath it. The Animator remains present for
Animation Rigging, but its clip playback is frozen and restored on Stop.

Use **Source To Sheep Euler**, **Position Scale**, and **Head Translation Weight**
on `SheepMotionRetargeter` to correct axis orientation, motion amplitude, and
how much headset translation affects the head target without changing the
recording files. Set **Head Translation Weight** to `0` when only head rotation
should affect aim direction.
