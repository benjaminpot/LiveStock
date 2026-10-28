import { sfx } from './sfx.js';

// Layout is kept separate from VideoPlayer so the page can be redesigned later.
export class CCTVPanel {
  constructor(root, blackout) {
    this.root = root;
    this.blackout = blackout;
    this.channel = null;
    this.state = null;
    this.rows = [];
    this.connection = document.querySelector('[data-connection]');
    this.current = document.querySelector('[data-current]');
    this.cooldown = root.querySelector('[data-cooldown]');
    this.cooldownCard = root.querySelector('[data-cooldown-card]');
    this.cooldownProgress = root.querySelector('[data-cooldown-progress]');
    this.cooldownHint = root.querySelector('[data-cooldown-hint]');
    this.monitors = root.querySelector('[data-monitors]');
    this.next = root.querySelector('[data-next]');
    this.slot = root.querySelector('[data-slot]');
    this.slotControl = root.querySelector('[data-slot-control]');
    this.secondaryCurrent = root.querySelector('[data-secondary-current]');
    this.secondaryScreen = document.getElementById('secondaryScreen');
    this.secondaryBlackout = document.getElementById('secondaryBlackout');
    this.switching = document.getElementById('switchingOverlay');
    this.switchingCamera = this.switching?.querySelector('[data-switching-camera]');
    this.pendingIndex = -1;
    this.pendingSlot = 0;
    this.switchTimeout = null;
    this.lastMonitorStatuses = null;
    this.slot.value = '0';
    this.slot.addEventListener('change', () => this.render());
    this.next.addEventListener('click', () => this.send({ type: 'cctv-next', slot: Number(this.slot.value) }));
    this.createRows(8);
    this.renderRows([]);
    this.setConnection('OFFLINE');
  }

  attach(channel) {
    this.reset();
    this.channel = channel;
    this.setConnection('WAITING FOR FEED');
    channel.addEventListener('open', () => {
      if (this.channel === channel) this.send({ type: 'cctv-ready' });
    });
    channel.addEventListener('message', event => {
      if (this.channel !== channel) return;
      let state;
      try { state = JSON.parse(event.data); } catch { return; }
      if (state.type === 'cctv-fire-result') {
        this.onFireResult?.(state);
        return;
      }
      if (state.type !== 'cctv-state' || !Array.isArray(state.monitors)) return;
      this.state = state;
      this.render();
      this.onState?.(state);
    });
    channel.addEventListener('close', () => {
      if (this.channel === channel) this.reset();
    });
    if (channel.readyState === 'open') this.send({ type: 'cctv-ready' });
  }

  send(command) {
    if (this.channel?.readyState === 'open') this.channel.send(JSON.stringify(command));
  }

  setConnection(text) {
    this.connection.textContent = text;
    this.next.disabled = true;
  }

  reset() {
    this.channel = null;
    this.state = null;
    this.setConnection('OFFLINE');
    this.current.textContent = 'Waiting for signal';
    this.cooldown.textContent = '—';
    this.cooldownCard?.classList.remove('is-cooling');
    if (this.cooldownProgress) this.cooldownProgress.value = 0;
    if (this.cooldownHint) this.cooldownHint.textContent = 'Switch available';
    if (this.rows.length !== 8) this.createRows(8);
    this.renderRows([]);
    this.blackout.hidden = true;
    this.secondaryScreen.hidden = true;
    this.secondaryBlackout.hidden = true;
    this.secondaryCurrent.hidden = true;
    this.slotControl.hidden = true;
    this.slot.value = '0';
    this.finishSwitch();
    this.lastMonitorStatuses = null;
    this.onReset?.();
  }

  render() {
    const state = this.state;
    if (!state) return;
    const indices = state.selectedIndices ?? [state.selectedIndex, -1];
    this.slot.value = '0';
    const selectedIndex = indices[Number(this.slot.value)];
    const selected = state.monitors[indices[0]];
    const secondary = state.monitors[indices[1]];
    const cooldown = Math.max(0, state.cooldownRemaining);
    const cooldownDuration = Math.max(0.1, state.cooldownDuration ?? 3);
    if (this.lastMonitorStatuses) {
      const becameHot = state.monitors.some((monitor, index) =>
        monitor.status === 'overheated' && this.lastMonitorStatuses[index] !== 'overheated');
      const recovered = state.monitors.some((monitor, index) =>
        monitor.status !== 'overheated' && this.lastMonitorStatuses[index] === 'overheated');
      if (becameHot) sfx.play('overheated');
      else if (recovered) sfx.play('recovered');
    }
    this.lastMonitorStatuses = state.monitors.map(monitor => monitor.status);
    this.connection.textContent = 'CONNECTED';
    this.current.textContent = selected?.name ?? 'No signal';
    this.cooldown.textContent = cooldown > 0 ? `${cooldown.toFixed(1)}s` : 'READY';
    this.cooldownCard?.classList.toggle('is-cooling', cooldown > 0);
    if (this.cooldownProgress) {
      this.cooldownProgress.max = cooldownDuration;
      this.cooldownProgress.value = cooldown;
      this.cooldownProgress.setAttribute('aria-label', cooldown > 0
        ? `Camera switch available in ${cooldown.toFixed(1)} seconds` : 'Camera switch available');
    }
    if (this.cooldownHint) this.cooldownHint.textContent = cooldown > 0
      ? `Next switch in ${cooldown.toFixed(1)} seconds` : 'Switch available';
    this.slotControl.hidden = true;
    this.secondaryScreen.hidden = true;
    this.secondaryCurrent.hidden = true;
    this.secondaryCurrent.textContent = `Secondary: ${secondary?.name ?? '—'}`;
    this.next.disabled = cooldown > 0 || !state.monitors.some((monitor, index) =>
      index !== selectedIndex && monitor.status !== 'overheated');
    this.blackout.hidden = selected?.status !== 'overheated';
    this.blackout.textContent = selected?.status === 'overheated'
      ? `CAMERA OVERHEATED · RECOVERING IN ${Math.ceil(selected.recoveryRemaining)}s` : '';
    this.secondaryBlackout.hidden = true;
    this.secondaryBlackout.textContent = secondary?.status === 'overheated'
      ? `監視器過熱，${Math.ceil(secondary.recoveryRemaining)} 秒後恢復` : '';
    if (this.pendingIndex >= 0 && indices[this.pendingSlot] === this.pendingIndex) this.finishSwitch();
    if (this.rows.length !== 8) this.createRows(8);
    this.renderRows(state.monitors, state, selectedIndex, cooldown);
  }

  renderRows(monitors, state = null, selectedIndex = -1, cooldown = 0) {
    this.rows.forEach(({ row, button, heat, status, title }, index) => {
      const monitor = monitors[index];
      row.classList.toggle('is-null', !monitor);
      title.textContent = monitor?.name ?? `CAM ${String(index + 1).padStart(2, '0')} · NULL`;
      button.textContent = monitor?.name ?? `Camera ${index + 1} unavailable`;
      button.setAttribute('aria-pressed', String(Boolean(monitor) && index === selectedIndex));
      const blocked = Boolean(monitor) && (index === selectedIndex || cooldown > 0 || monitor.status === 'overheated');
      // Keep unavailable cards clickable so the UI can explain the refusal
      // with sound instead of feeling unresponsive.
      button.disabled = false;
      button.setAttribute('aria-disabled', String(!monitor || blocked));
      heat.hidden = !monitor;
      if (!monitor) {
        status.textContent = 'NO DATA';
        return;
      }
      heat.max = state.heatLimit;
      heat.value = monitor.heat;
      heat.setAttribute('aria-label', `${monitor.name} heat level`);
      const label = monitor.status === 'overheated'
        ? `OVERHEATED · ${Math.ceil(monitor.recoveryRemaining)}s`
        : monitor.status === 'watching' ? `LIVE · ${monitor.viewers} VIEWERS` : 'STANDBY';
      status.textContent = `${label} · HEAT ${Math.round(monitor.heat)}/${state.heatLimit}`;
    });
  }

  requestSwitch(index) {
    const monitor = this.state?.monitors[index];
    if (!monitor) {
      sfx.play('unavailable');
      this.showDeniedSwitch();
      return;
    }
    const selectedIndex = this.state.selectedIndices?.[0] ?? this.state.selectedIndex;
    if (index === selectedIndex || this.state.cooldownRemaining > 0 || monitor.status === 'overheated') {
      sfx.play('unavailable');
      this.showDeniedSwitch();
      return;
    }
    this.pendingIndex = index;
    this.pendingSlot = Number(this.slot.value);
    if (this.switching) this.switching.hidden = false;
    if (this.switchingCamera) this.switchingCamera.textContent = `Routing ${monitor.name} to primary feed…`;
    clearTimeout(this.switchTimeout);
    this.switchTimeout = setTimeout(() => this.finishSwitch(), 10000);
    sfx.play('switch');
    this.send({ type: 'cctv-select', index, slot: this.pendingSlot });
  }

  showDeniedSwitch() {
    this.cooldownCard?.classList.remove('denied');
    void this.cooldownCard?.offsetWidth;
    this.cooldownCard?.classList.add('denied');
  }

  finishSwitch() {
    this.pendingIndex = -1;
    clearTimeout(this.switchTimeout);
    this.switchTimeout = null;
    if (this.switching) this.switching.hidden = true;
  }

  createRows(count) {
    this.rows = [];
    this.monitors.replaceChildren();
    for (let index = 0; index < count; index++) {
      const row = document.createElement('div');
      row.className = 'cctv-monitor';
      const thumb = document.createElement('div');
      thumb.className = 'cctv-thumb';
      const copy = document.createElement('div');
      copy.className = 'camera-copy';
      const heading = document.createElement('div');
      heading.className = 'camera-title';
      const title = document.createElement('b');
      const indicator = document.createElement('i');
      const button = document.createElement('button');
      button.type = 'button';
      button.addEventListener('click', () => this.requestSwitch(index));
      const heat = document.createElement('progress');
      const status = document.createElement('span');
      status.className = 'camera-status';
      heading.append(title, indicator);
      copy.append(heading, status);
      row.append(thumb, copy, heat, button);
      this.monitors.append(row);
      this.rows.push({ row, button, heat, status, title });
    }
  }
}
