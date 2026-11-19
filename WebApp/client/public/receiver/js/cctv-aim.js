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
    ? `已擊倒：${message.objectName} · 分數 ${message.kills ?? 1}`
    : message.status === 'hit' ? `已命中：${message.objectName}`
    : message.status === 'launched' ? '已發射'
    : message.status === 'miss' ? '未命中，彈丸已回收'
    : message.status === 'item-required' ? '請先選取持有的道具'
    : message.status === 'no-stock' ? '道具不足，請選擇其他道具'
    : message.status === 'invalid-item' ? '此道具無法使用，請重新選擇'
    : message.status === 'invalid-request' ? '操作無效，請重新瞄準'
    : message.status === 'unknown-player' ? '玩家尚未就緒，請重新連線'
    : message.status === 'cooldown' ? '發射間隔尚未結束，請稍候'
    : message.status === 'stale-request' ? '操作已過期，請重試'
    : message.status === 'unavailable' ? '暫時無法使用道具，請重新瞄準'
    : message.status === 'insufficient-funds' ? '餘額不足，連發已停止'
    : message.status === 'round-ended' ? '本局已結束，無法發射'
    : '畫面已切換或無法使用，請重新瞄準';
}

export class CCTVAim {
  constructor(screens, result, send) {
    this.screens = screens;
    this.result = result;
    this.send = send;
    this.listeners = [];
    const listen = (target, type, handler) => {
      target.addEventListener(type, handler);
      this.listeners.push(() => target.removeEventListener(type, handler));
    };
    for (let slot = 0; slot < screens.length; slot++) {
      const screen = screens[slot];
      listen(screen, 'click', event => {
        if (this.mode !== 'normal' || event.target.tagName !== 'VIDEO') return;
        this.fireAt(event.target, slot, event.clientX, event.clientY);
      });
      listen(screen, 'pointerdown', event => {
        if (this.mode !== 'crazy' || event.isPrimary === false || event.button !== 0 ||
            event.target.tagName !== 'VIDEO' || this.hold) return;
        if (!this.readAim(event.target, slot, event.clientX, event.clientY)) {
          sfx.play('unavailable');
          return;
        }
        event.preventDefault();
        this.hold = { video: event.target, slot, pointerId: event.pointerId,
          clientX: event.clientX, clientY: event.clientY };
        event.target.setPointerCapture?.(event.pointerId);
        this.tick();
      });
      listen(screen, 'pointermove', event => {
        const hold = this.hold;
        if (!hold || hold.slot !== slot || hold.pointerId !== event.pointerId) return;
        if (event.buttons === 0 || !this.readAim(hold.video, slot, event.clientX, event.clientY)) {
          this.stopFiring();
          return;
        }
        hold.clientX = event.clientX;
        hold.clientY = event.clientY;
      });
      listen(screen, 'pointerleave', () => this.stopFiring());
      listen(screen, 'lostpointercapture', () => this.stopFiring());
    }
    const release = event => {
      if (this.hold?.pointerId === event.pointerId) this.stopFiring();
    };
    listen(window, 'pointerup', release);
    listen(window, 'pointercancel', release);
    listen(window, 'blur', () => this.stopFiring());
    listen(document, 'visibilitychange', () => {
      if (document.hidden) this.stopFiring();
    });
    this.reset();
    this.resize = new ResizeObserver(() => this.render());
    screens.forEach(screen => this.resize.observe(screen));
  }

  readAim(video, slot, clientX, clientY) {
    const index = this.state?.selectedIndices?.[slot] ??
      (slot === 0 ? this.state?.selectedIndex : -1);
    const monitor = this.state?.monitors?.[index];
    if (!(index >= 0) || !monitor || monitor.status === 'overheated') return null;
    const point = videoPoint(video, clientX, clientY);
    return point ? { slot, index, ...point } : null;
  }

  fireAt(video, slot, clientX, clientY) {
    const aim = this.readAim(video, slot, clientX, clientY);
    if (!aim) {
      this.stopFiring();
      sfx.play('unavailable');
      return false;
    }
    const now = performance.now();
    if (now - this.lastFireAt < this.fireInterval * 1000) return false;
    if (this.send({ type: 'cctv-fire', ...aim }) === false) return false;
    this.lastFireAt = now;
    this.aim = aim;
    this.result.textContent = '已瞄準，發射中…';
    this.render();
    sfx.play('shoot');
    return true;
  }

  tick() {
    const hold = this.hold;
    if (!hold || this.mode !== 'crazy') return;
    this.fireAt(hold.video, hold.slot, hold.clientX, hold.clientY);
    if (this.hold) this.fireTimer = setTimeout(() => this.tick(), this.fireInterval * 1000);
  }

  stopFiring() {
    clearTimeout(this.fireTimer);
    this.fireTimer = null;
    const hold = this.hold;
    this.hold = null;
    if (hold?.video.hasPointerCapture?.(hold.pointerId)) {
      hold.video.releasePointerCapture(hold.pointerId);
    }
  }

  setItemState(state) {
    const mode = state?.mode ?? 'normal';
    const interval = mode === 'crazy' ? state.fireInterval : 0.2;
    if (this.mode === mode && this.fireInterval === interval && this.itemActive === !!state) return;
    this.stopFiring();
    this.mode = mode;
    this.fireInterval = interval;
    this.itemActive = !!state;
    this.aim = null;
    this.result.textContent = mode === 'ended' ? '本局已結束，無法發射'
      : mode === 'crazy' ? '按住影片畫面即可連發，放開即停止'
      : state ? '選取持有的道具後，點擊影片使用' : '點擊影片即可發射';
    this.render();
  }

  setState(state) {
    const previous = this.state?.selectedIndices ?? [this.state?.selectedIndex, -1];
    const indices = state.selectedIndices ?? [state.selectedIndex, -1];
    this.state = state;
    if (this.hold && (indices[this.hold.slot] !== previous[this.hold.slot] ||
        !this.readAim(this.hold.video, this.hold.slot, this.hold.clientX, this.hold.clientY))) {
      this.stopFiring();
    }
    if (this.aim && (indices[this.aim.slot] !== this.aim.index ||
        state.monitors[this.aim.index]?.status === 'overheated')) {
      this.aim = null;
      this.result.textContent = '畫面已切換或過熱，請重新瞄準';
    }
    this.render();
  }

  showResult(message) {
    if (this.mode === 'ended' || !this.aim ||
        message.slot !== this.aim.slot || message.index !== this.aim.index) return;
    if (['insufficient-funds', 'round-ended'].includes(message.status)) this.stopFiring();
    if (!['kill', 'hit', 'launched', 'miss'].includes(message.status)) sfx.play('unavailable');
    this.result.textContent = formatFireResult(message);
  }

  showFeedback(text) {
    this.result.textContent = text;
    sfx.play('unavailable');
  }

  reset() {
    this.stopFiring();
    this.state = null;
    this.aim = null;
    this.mode = 'normal';
    this.itemActive = false;
    this.fireInterval = 0.2;
    this.lastFireAt = Number.NEGATIVE_INFINITY;
    this.result.textContent = '點擊影片即可發射';
    this.render();
  }

  dispose() {
    this.stopFiring();
    this.listeners.forEach(remove => remove());
    this.resize.disconnect();
  }

  render() {
    this.screens.forEach((screen, slot) => {
      screen.classList.toggle('is-crazy', this.mode === 'crazy');
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
