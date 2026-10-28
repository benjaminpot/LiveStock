import { sfx } from './sfx.js';

// Video coordinates use Unity's viewport convention: bottom-left (0, 0).
export function videoPoint(video, clientX, clientY) {
  if (!video.videoWidth || !video.videoHeight) return null;
  const box = video.getBoundingClientRect();
  const scale = Math.min(box.width / video.videoWidth, box.height / video.videoHeight);
  const width = video.videoWidth * scale;
  const height = video.videoHeight * scale;
  const left = box.left + (box.width - width) / 2;
  const top = box.top + (box.height - height) / 2;
  if (!width || !height || clientX < left || clientX > left + width ||
      clientY < top || clientY > top + height) return null;
  return { x: (clientX - left) / width, y: 1 - (clientY - top) / height };
}

export function formatFireResult(message) {
  return message.status === 'kill'
    ? `Target down: ${message.objectName} · Score ${message.kills ?? 1}`
    : message.status === 'hit' ? `Hit confirmed: ${message.objectName}`
    : message.status === 'launched' ? 'Projectile launched'
    : message.status === 'miss' ? 'Missed · projectile cleared'
    : 'Feed changed or unavailable · reacquire target';
}

export class CCTVAim {
  constructor(screens, result, send) {
    this.screens = screens;
    this.result = result;
    this.send = send;
    this.state = null;
    this.aim = null;
    this.lastFireAt = Number.NEGATIVE_INFINITY;
    for (let slot = 0; slot < screens.length; slot++) {
      screens[slot].addEventListener('click', event => {
        if (event.target.tagName !== 'VIDEO') return;
        const index = this.state?.selectedIndices?.[slot];
        if (!(index >= 0) || this.state.monitors[index].status === 'overheated') {
          sfx.play('unavailable');
          return;
        }
        const point = videoPoint(event.target, event.clientX, event.clientY);
        if (!point) {
          sfx.play('unavailable');
          return;
        }
        const now = performance.now();
        if (now - this.lastFireAt < 200) return;
        this.lastFireAt = now;
        this.aim = { slot, index, ...point };
        this.result.textContent = 'Target locked · firing…';
        this.render();
        sfx.play('shoot');
        this.send({ type: 'cctv-fire', ...this.aim });
      });
    }
    this.reset();
    this.resize = new ResizeObserver(() => this.render());
    screens.forEach(screen => this.resize.observe(screen));
  }

  setState(state) {
    this.state = state;
    if (this.aim && (state.selectedIndices?.[this.aim.slot] !== this.aim.index ||
        state.monitors[this.aim.index]?.status === 'overheated')) {
      this.aim = null;
      this.result.textContent = 'Feed changed · reacquire target';
    }
    this.render();
  }

  showResult(message) {
    if (!this.aim || message.slot !== this.aim.slot || message.index !== this.aim.index) return;
    if (!['kill', 'hit', 'launched', 'miss'].includes(message.status)) sfx.play('unavailable');
    this.result.textContent = formatFireResult(message);
  }

  reset() {
    this.state = null;
    this.aim = null;
    this.result.textContent = 'Click inside the video to fire';
    this.render();
  }

  render() {
    this.screens.forEach((screen, slot) => {
      const marker = screen.querySelector('[data-crosshair]');
      marker.hidden = !this.aim || this.aim.slot !== slot;
      if (marker.hidden) return;
      const video = screen.querySelector('video');
      if (!video?.videoWidth || !video.videoHeight) { marker.hidden = true; return; }
      const box = video.getBoundingClientRect();
      const parent = screen.getBoundingClientRect();
      const scale = Math.min(box.width / video.videoWidth, box.height / video.videoHeight);
      const width = video.videoWidth * scale;
      const height = video.videoHeight * scale;
      marker.style.left = `${box.left - parent.left + (box.width - width) / 2 + this.aim.x * width}px`;
      marker.style.top = `${box.top - parent.top + (box.height - height) / 2 + (1 - this.aim.y) * height}px`;
    });
  }
}
