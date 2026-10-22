import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { CCTVPanel } from '../../WebApp/client/public/receiver/js/cctv-panel.js';

// Small DOM stand-in: verifies state/command behavior without adding dependencies.
class Element extends EventTarget {
  constructor() { super(); this.children = []; this.attributes = {}; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  setAttribute(name, value) { this.attributes[name] = value; }
  click() { if (!this.disabled) this.dispatchEvent(new Event('click')); }
}
class Channel extends EventTarget {
  readyState = 'open';
  sent = [];
  send(value) { this.sent.push(JSON.parse(value)); }
  receive(state) {
    this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(state) }));
  }
}
function setup() {
  const elements = Object.fromEntries(['secondaryScreen', 'secondaryBlackout'].map(id => [id, new Element()]));
  globalThis.document = { createElement: () => new Element(), getElementById: id => elements[id] };
  const fields = Object.fromEntries(['connection', 'current', 'cooldown', 'monitors', 'next',
    'slot', 'slot-control', 'secondary-current']
    .map(name => [`[data-${name}]`, new Element()]));
  const panel = new CCTVPanel({ querySelector: selector => fields[selector] }, new Element());
  const channel = new Channel();
  panel.attach(channel);
  return { panel, channel };
}
function snapshot(cooldown = 0, overheated = false) {
  return { type: 'cctv-state', selectedIndex: 0, cooldownRemaining: cooldown, heatLimit: 100,
    monitors: [
      { name: 'CCTV 01', status: overheated ? 'overheated' : 'watching', heat: overheated ? 100 : 25,
        recoveryRemaining: overheated ? 8 : 0, viewers: overheated ? 0 : 2 },
      { name: 'CCTV 02', status: 'ready', heat: 0, recoveryRemaining: 0, viewers: 0 }
    ] };
}

test('server state drives controls; cooldown blocks UI commands', () => {
  const { panel, channel } = setup();
  channel.receive(snapshot(3));
  panel.rows[1].button.click();
  panel.next.click();
  assert.deepEqual(channel.sent, [{ type: 'cctv-ready' }]);
  channel.receive(snapshot());
  panel.rows[1].button.click();
  assert.deepEqual(channel.sent.at(-1), { type: 'cctv-select', index: 1, slot: 0 });
  assert.match(panel.rows[0].status.textContent, /2 人/);
});

test('updates preserve button identity so polling cannot interrupt a mobile tap', () => {
  const { panel, channel } = setup();
  channel.receive(snapshot());
  const button = panel.rows[1].button;
  channel.receive(snapshot());
  assert.equal(panel.rows[1].button, button);
});

test('two receiver panels keep their own selection, cooldown and command channel', () => {
  const pc = setup();
  const phone = setup();
  const pcState = snapshot();
  const phoneState = snapshot(3);
  phoneState.selectedIndex = 1;
  pc.channel.receive(pcState);
  phone.channel.receive(phoneState);
  assert.equal(pc.panel.current.textContent, 'CCTV 01');
  assert.equal(phone.panel.current.textContent, 'CCTV 02');
  assert.equal(pc.panel.next.disabled, false);
  assert.equal(phone.panel.next.disabled, true);
  pc.panel.rows[1].button.click();
  assert.deepEqual(pc.channel.sent.at(-1), { type: 'cctv-select', index: 1, slot: 0 });
  assert.deepEqual(phone.channel.sent, [{ type: 'cctv-ready' }]);
  assert.equal(phone.panel.state.selectedIndex, 1);
});

test('overheat overlay follows Unity; another available camera remains selectable', () => {
  const { panel, channel } = setup();
  channel.receive(snapshot(0, true));
  assert.equal(panel.blackout.hidden, false);
  assert.match(panel.blackout.textContent, /8 秒/);
  assert.equal(panel.rows[1].button.disabled, false);
  channel.receive(snapshot());
  assert.equal(panel.blackout.hidden, true);
});

test('disconnected or replaced channels cannot leave active controls or stale state', () => {
  const { panel, channel } = setup();
  channel.receive(snapshot());
  const replacement = new Channel();
  panel.attach(replacement);
  channel.receive(snapshot());
  assert.equal(panel.state, null);
  replacement.receive(snapshot());
  replacement.dispatchEvent(new Event('close'));
  assert.equal(panel.state, null);
  assert.equal(panel.next.disabled, true);
  assert.equal(panel.monitors.children.length, 0);
});

test('receiver HTML contains all panel hooks', async () => {
  const html = await readFile(new URL('../../WebApp/client/public/receiver/index.html', import.meta.url), 'utf8');
  for (const hook of ['data-connection', 'data-current', 'data-cooldown', 'data-monitors', 'data-next', 'id="cctvBlackout"'])
    assert.ok(html.includes(hook), `missing ${hook}`);
});

test('two views appear from server state and share cooldown without a second switch', () => {
  const { panel, channel } = setup();
  const dual = snapshot();
  dual.selectedIndices = [0, 1];
  channel.receive(dual);
  assert.equal(panel.secondaryScreen.hidden, false);
  panel.slot.value = '1';
  panel.slot.dispatchEvent(new Event('change'));
  panel.rows[0].button.click();
  assert.deepEqual(channel.sent.at(-1), {type:'cctv-select',index:0,slot:1});
  dual.cooldownRemaining = 3;
  channel.receive(dual);
  assert.equal(panel.next.disabled, true);
  panel.rows[0].button.click();
  assert.equal(channel.sent.filter(command=>command.type==='cctv-select').length,1);
  channel.receive(snapshot());
  assert.equal(panel.secondaryScreen.hidden, true);
  assert.equal(panel.slot.value, '0');
});

test('secondary overheat does not cover primary; disconnect clears both slots', () => {
  const { panel, channel } = setup();
  const dual = snapshot();
  dual.selectedIndices = [0,1];
  dual.monitors[1].status = 'overheated';
  dual.monitors[1].recoveryRemaining = 7;
  channel.receive(dual);
  assert.equal(panel.blackout.hidden, true);
  assert.equal(panel.secondaryBlackout.hidden, false);
  assert.match(panel.secondaryBlackout.textContent, /7 秒/);
  channel.dispatchEvent(new Event('close'));
  assert.equal(panel.secondaryScreen.hidden, true);
  assert.equal(panel.secondaryBlackout.hidden, true);
});
