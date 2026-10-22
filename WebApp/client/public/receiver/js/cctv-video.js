// Route by negotiated media ID, never by arrival order. Audio belongs only to
// the primary player so the second video cannot double the sound.
export class CCTVVideo {
  constructor(primary, secondary) {
    this.primary = primary;
    this.secondary = secondary;
    this.tracks = new Map();
    this.state = null;
    this.audio = null;
  }

  addTrack(data) {
    if (data.track.kind === 'audio') {
      if (this.audio !== data.track) this.primary.addTrack(data.track);
      this.audio = data.track;
      return;
    }
    this.tracks.set(data.transceiver.mid, data.track);
    this.render();
  }

  setState(state) {
    this.state = state;
    this.render();
  }

  render() {
    if (!this.state) return;
    const primaryTrack = this.tracks.get(this.state.videoMids?.[0]);
    const secondaryTrack = this.tracks.get(this.state.videoMids?.[1]);
    const video = this.primary.videoElement;
    if (primaryTrack && video?.srcObject && !video.srcObject.getVideoTracks().includes(primaryTrack)) {
      for (const old of video.srcObject.getVideoTracks()) video.srcObject.removeTrack(old);
      this.primary.addTrack(primaryTrack);
    }
    const active = this.state.selectedIndices?.[1] >= 0;
    if (active && secondaryTrack) {
      if (!this.secondary.srcObject?.getVideoTracks().includes(secondaryTrack))
        this.secondary.srcObject = new MediaStream([secondaryTrack]);
      // No sound is attached to this element; normal muted inline playback.
      this.secondary.play().catch(() => {});
    } else {
      this.secondary.pause();
      this.secondary.srcObject = null;
    }
  }

  reset() {
    this.tracks.clear();
    this.state = null;
    this.audio = null;
    this.secondary.pause();
    this.secondary.srcObject = null;
  }
}
