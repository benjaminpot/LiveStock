"""Read-only integrity and motion-range audit of LiveStock binary recordings."""
import json
import math
import struct
from pathlib import Path


def audit(folder):
    metadata = json.loads((folder / "metadata.json").read_text(encoding="utf-8-sig"))
    data = (folder / metadata["frameFileName"]).read_bytes()
    magic, version, reserved, count, rate = struct.unpack_from("<4sHHii", data)
    tracks = metadata["tracks"]
    stride = 12 + sum(1 + 12 * t["recordsPosition"] + 16 * t["recordsRotation"]
                      + 12 * t["recordsLocalScale"] for t in tracks)
    errors = []
    if (magic, version, count, rate) != (b"LSMR", 1, len(tracks), metadata["sampleRate"]):
        errors.append("header mismatch")
    frame_count, remainder = divmod(len(data) - 16, stride)
    times, indices = [], []
    poses = [[] for _ in tracks]
    for frame in range(frame_count):
        offset = 16 + frame * stride
        index, timestamp = struct.unpack_from("<id", data, offset)
        indices.append(index)
        times.append(timestamp)
        offset += 12
        for track_index, track in enumerate(tracks):
            active = data[offset] & 1
            offset += 1
            values = {"active": bool(active)}
            for key, enabled, components in [("position", track["recordsPosition"], 3),
                                             ("rotation", track["recordsRotation"], 4),
                                             ("scale", track["recordsLocalScale"], 3)]:
                if enabled:
                    values[key] = struct.unpack_from("<" + "f" * components, data, offset)
                    offset += components * 4
                    if not all(math.isfinite(x) for x in values[key]):
                        errors.append("non-finite pose")
            poses[track_index].append(values)
    if remainder:
        errors.append("incomplete trailing frame")
    if frame_count != metadata["frameCount"]:
        errors.append("frame count mismatch")
    if any(b <= a for a, b in zip(times, times[1:])):
        errors.append("non-monotonic timestamps")
    if any(b != a + 1 for a, b in zip(indices, indices[1:])):
        errors.append("non-contiguous indices")
    result = dict(folder=folder.name, session=metadata["sessionName"], frames=frame_count,
                  completed=metadata["completed"], seconds=round(times[-1] - times[0], 4),
                  dropped=metadata["droppedSampleCount"], trailing_bytes=remainder,
                  largest_gap_seconds=round(max(b-a for a,b in zip(times,times[1:])), 4),
                  errors=sorted(set(errors)), tracks=[])
    for track, samples in zip(tracks, poses):
        positions = [s["position"] for s in samples]
        rotations = [s["rotation"] for s in samples]
        norms = [math.sqrt(sum(x*x for x in q)) for q in rotations]
        q0 = rotations[0]
        excursions = [math.degrees(2 * math.acos(min(1.0, abs(sum(a*b for a,b in zip(q0,q)))
                                                   / (norms[0] * norm))))
                      if norm > 0 and norms[0] > 0 else None
                      for q, norm in zip(rotations, norms)]
        result["tracks"].append(dict(
            id=track["id"], active_frames=sum(s["active"] for s in samples),
            position_range_metres=[round(max(p[i] for p in positions)-min(p[i] for p in positions),4)
                                   for i in range(3)],
            changed_adjacent_frames=sum(a != b for a,b in zip(samples,samples[1:])),
            max_rotation_from_first_degrees=round(max(x for x in excursions if x is not None),2),
            quaternion_norm_range=[round(min(norms),6), round(max(norms),6)]))
    return result


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1] / "Assets" / "MotionRecordings"
    print(json.dumps([audit(path.parent) for path in sorted(root.glob("*/metadata.json"))],
                     ensure_ascii=False, indent=2))
