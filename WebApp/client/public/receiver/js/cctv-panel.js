// Layout is kept separate from VideoPlayer so the page can be redesigned later.
export class CCTVPanel {
  constructor(root, blackout) {
    this.root = root;
    this.blackout = blackout;
    this.channel = null;
    this.state = null;
    this.rows = [];
    this.connection = root.querySelector('[data-connection]');
    this.current = root.querySelector('[data-current]');
    this.cooldown = root.querySelector('[data-cooldown]');
    this.monitors = root.querySelector('[data-monitors]');
    this.next = root.querySelector('[data-next]');
    this.slot = root.querySelector('[data-slot]');
    this.slotControl = root.querySelector('[data-slot-control]');
    this.secondaryCurrent = root.querySelector('[data-secondary-current]');
    this.secondaryScreen = document.getElementById('secondaryScreen');
    this.secondaryBlackout = document.getElementById('secondaryBlackout');
    this.slot.value = '0';
    this.slot.addEventListener('change', () => this.render());
    this.next.addEventListener('click', () => this.send({ type: 'cctv-next', slot: Number(this.slot.value) }));
    this.setConnection('尚未連線');
  }

  attach(channel) {
    this.reset();
    this.channel = channel;
    this.setConnection('等待監視器資料');
    channel.addEventListener('open', () => {
      if (this.channel === channel) this.send({ type: 'cctv-ready' });
    });
    channel.addEventListener('message', event => {
      if (this.channel !== channel) return;
      let state;
      try { state = JSON.parse(event.data); } catch { return; }
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
    this.setConnection('尚未連線');
    this.current.textContent = '—';
    this.cooldown.textContent = '—';
    this.monitors.replaceChildren();
    this.rows = [];
    this.blackout.hidden = true;
    this.secondaryScreen.hidden = true;
    this.secondaryBlackout.hidden = true;
    this.secondaryCurrent.hidden = true;
    this.slotControl.hidden = true;
    this.slot.value = '0';
  }

  render() {
    const state = this.state;
    if (!state) return;
    const indices = state.selectedIndices ?? [state.selectedIndex, -1];
    const dual = indices[1] >= 0;
    if (!dual) this.slot.value = '0';
    const selectedIndex = indices[Number(this.slot.value)];
    const selected = state.monitors[indices[0]];
    const secondary = state.monitors[indices[1]];
    const cooldown = Math.max(0, state.cooldownRemaining);
    this.connection.textContent = '已連線';
    this.current.textContent = selected?.name ?? '—';
    this.cooldown.textContent = cooldown > 0 ? `${cooldown.toFixed(1)} 秒` : '可切換';
    this.slotControl.hidden = !dual;
    this.secondaryScreen.hidden = !dual;
    this.secondaryCurrent.hidden = !dual;
    this.secondaryCurrent.textContent = `第二畫面：${secondary?.name ?? '—'}`;
    this.next.disabled = cooldown > 0 || !state.monitors.some((monitor, index) =>
      index !== selectedIndex && monitor.status !== 'overheated');
    this.blackout.hidden = selected?.status !== 'overheated';
    this.blackout.textContent = selected?.status === 'overheated'
      ? `監視器過熱，${Math.ceil(selected.recoveryRemaining)} 秒後恢復` : '';
    this.secondaryBlackout.hidden = !dual || secondary?.status !== 'overheated';
    this.secondaryBlackout.textContent = secondary?.status === 'overheated'
      ? `監視器過熱，${Math.ceil(secondary.recoveryRemaining)} 秒後恢復` : '';
    if (this.rows.length !== state.monitors.length) this.createRows(state.monitors.length);
    state.monitors.forEach((monitor, index) => {
      const { button, heat, status } = this.rows[index];
      button.textContent = monitor.name;
      button.setAttribute('aria-pressed', String(index === selectedIndex));
      button.disabled = index === selectedIndex || cooldown > 0 || monitor.status === 'overheated';
      heat.max = state.heatLimit;
      heat.value = monitor.heat;
      heat.setAttribute('aria-label', `${monitor.name} 過熱值`);
      const label = monitor.status === 'overheated'
        ? `過熱停用 · ${Math.ceil(monitor.recoveryRemaining)} 秒後恢復`
        : monitor.status === 'watching' ? `觀看中 · ${monitor.viewers} 人` : '可使用';
      status.textContent = `${label} · 過熱 ${Math.round(monitor.heat)}/${state.heatLimit}`;
    });
  }

  createRows(count) {
    this.rows = [];
    this.monitors.replaceChildren();
    for (let index = 0; index < count; index++) {
      const row = document.createElement('div');
      row.className = 'cctv-monitor';
      const button = document.createElement('button');
      button.type = 'button';
      button.addEventListener('click', () => this.send({ type: 'cctv-select', index, slot: Number(this.slot.value) }));
      const heat = document.createElement('progress');
      const status = document.createElement('span');
      row.append(button, heat, status);
      this.monitors.append(row);
      this.rows.push({ button, heat, status });
    }
  }
}
