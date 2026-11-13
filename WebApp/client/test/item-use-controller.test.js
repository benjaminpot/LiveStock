import { jest } from '@jest/globals';
import { readFileSync } from 'node:fs';
import { CCTVPanel } from '../public/receiver/js/cctv-panel.js';
import { CCTVAim } from '../public/receiver/js/cctv-aim.js';
import { InventoryPanel } from '../public/receiver/js/inventory-panel.js';
import { ShopPanel } from '../public/receiver/js/shop-panel.js';
import { ItemUseController } from '../public/receiver/js/item-use-controller.js';
import { sfx } from '../public/receiver/js/sfx.js';

const ids = ['capture-ball', 'bow', 'sniper', 'bomb'];
const snapshot = (lastRequestId = 0, quantities = [1, 1, 1, 1]) => ({
  type: 'shop-state', lastRequestId,
  items: ids.map((id, i) => ({ id, name: id, price: 10000, quantity: quantities[i] }))
});
const itemState = lastRequestId => ({ type: 'item-state', lastRequestId });
const cameraState = {
  type: 'cctv-state', selectedIndex: 0, selectedIndices: [0, -1],
  cooldownRemaining: 0, heatLimit: 100,
  monitors: [{ name: 'Farm', status: 'watching', heat: 0, viewers: 1 }]
};
const result = (shot, status, overrides = {}) => ({
  type: 'item-result', requestId: shot.requestId, itemId: shot.itemId,
  slot: shot.slot, index: shot.index, status, objectName: 'NPC_sheep', kills: 1, ...overrides
});

class Channel extends EventTarget {
  readyState = 'open';
  sent = [];
  send(data) { this.sent.push(JSON.parse(data)); }
  receive(state) {
    this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(state) }));
  }
  close() {
    this.readyState = 'closed';
    this.dispatchEvent(new Event('close'));
  }
}

describe.each(['live.html', 'index.html'])('item use consumer on %s', pageName => {
  let cctv;
  let aim;
  let inventory;
  let shop;
  let controller;
  let channel;
  let video;
  let now;
  let sound;
  const feedback = () => document.getElementById('fireResult').textContent;
  const select = (id = ids[0]) => document.querySelector(`[data-inventory-item="${id}"]`).click();
  const quantity = (id = ids[0]) => document.querySelector(
    `[data-inventory-item="${id}"] [data-inventory-quantity]`).textContent;
  const shots = () => channel.sent.filter(command => ['cctv-fire', 'item-use'].includes(command.type));
  const click = () => {
    now += 250;
    video.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 400, clientY: 225 }));
  };
  const ready = (lastRequestId = 0) => {
    channel.receive(snapshot());
    channel.receive(itemState(lastRequestId));
    select();
  };

  beforeEach(() => {
    const html = readFileSync(new URL(`../public/receiver/${pageName}`, import.meta.url), 'utf8');
    const page = new DOMParser().parseFromString(html, 'text/html');
    document.body.replaceChildren(...page.body.childNodes);
    sound = jest.spyOn(sfx, 'play').mockImplementation(() => {});
    now = 0;
    jest.spyOn(performance, 'now').mockImplementation(() => now);
    cctv = new CCTVPanel(document.getElementById('cctvPanel'), document.getElementById('cctvBlackout'));
    inventory = new InventoryPanel(document.querySelector('[data-inventory-panel]'));
    shop = new ShopPanel(document.querySelector('[data-shop-panel]'), command => cctv.send(command));
    aim = new CCTVAim([...document.querySelectorAll('.cctv-screen')],
      document.getElementById('fireResult'), command => controller.fire(command));
    controller = new ItemUseController({
      send: command => cctv.send(command), getSelectedItemId: () => inventory.selectedItemId,
      onResult: state => aim.showResult(state), onFeedback: text => aim.showFeedback(text)
    });
    cctv.onState = state => aim.setState(state);
    cctv.onItemState = state => controller.setState(state);
    cctv.onItemResult = state => controller.showResult(state);
    cctv.onFireResult = state => { if (!controller.active) aim.showResult(state); };
    cctv.onShopState = state => { inventory.setState(state); shop.setState(state); };
    cctv.onReset = () => { controller.reset(); inventory.reset(); shop.reset(); aim.reset(); };
    channel = new Channel();
    cctv.attach(channel);
    channel.receive(cameraState);
    video = document.createElement('video');
    document.getElementById('player').append(video);
    Object.defineProperties(video, { videoWidth: { value: 800 }, videoHeight: { value: 450 } });
    video.getBoundingClientRect = () => ({ left: 0, top: 0, width: 800, height: 450 });
  });

  afterEach(() => { jest.restoreAllMocks(); });

  test('legacy scenes fire unchanged until a valid item-state opts in', () => {
    for (const state of [itemState(-1), itemState(0.5), itemState('0'), itemState(2147483648), {}]) {
      channel.receive(state);
    }
    channel.receive(snapshot(99));
    click();
    expect(shots()).toEqual([{ type: 'cctv-fire', slot: 0, index: 0, x: 0.5, y: 0.5 }]);
    expect(sound).toHaveBeenCalledWith('shoot');
    channel.receive({ type: 'cctv-fire-result', slot: 0, index: 0, status: 'kill',
      objectName: 'NPC_sheep', kills: 3 });
    expect(feedback()).toBe('Target down: NPC_sheep · Score 3');
    channel.receive({ type: 'cctv-fire-result', slot: 0, index: 0, status: 'item-required' });
    expect(feedback()).toContain('inventory item');
  });

  test('requires owned selection and never decrements stock optimistically', () => {
    channel.receive(snapshot(0, [0, 2, 0, 0]));
    channel.receive(itemState(0));
    select();
    click();
    expect(shots()).toEqual([]);
    expect(sound).not.toHaveBeenCalledWith('shoot');
    expect(feedback()).toContain('選取');
    select('bow');
    click();
    expect(shots()).toEqual([{ type: 'item-use', requestId: 1, itemId: 'bow',
      slot: 0, index: 0, x: 0.5, y: 0.5 }]);
    expect(quantity('bow')).toBe('2');
    expect(sound).toHaveBeenCalledWith('shoot');
    click();
    expect(shots()).toHaveLength(1);
    expect(sound.mock.calls.filter(([name]) => name === 'shoot')).toHaveLength(1);
  });

  test('last-stock snapshot clears selection before launch and impact feedback still arrives', () => {
    ready();
    click();
    const shot = shots()[0];
    channel.receive(snapshot(0, [0, 1, 1, 1]));
    expect(inventory.selectedItemId).toBeNull();
    expect(quantity()).toBe('0');
    channel.receive(result(shot, 'launched'));
    expect(feedback()).toBe('Projectile launched');
    channel.receive(result(shot, 'kill'));
    expect(feedback()).toBe('Target down: NPC_sheep · Score 1');
    channel.receive(result(shot, 'launched'));
    expect(feedback()).toBe('Target down: NPC_sheep · Score 1');
    expect(quantity()).toBe('0');
  });

  test('late requests and mismatched item or camera results cannot replace the current shot', () => {
    ready();
    click();
    const first = shots()[0];
    channel.receive(result(first, 'launched'));
    select('bow');
    click();
    const second = shots()[1];
    expect(second.requestId).toBe(2);
    const pending = feedback();
    for (const message of [result(first, 'kill'), result(second, 'hit', { itemId: 'bomb' }),
      result(second, 'hit', { slot: 1 }), result(second, 'hit', { index: 1 }),
      result(second, 'invented-status')]) channel.receive(message);
    expect(feedback()).toBe(pending);
    click();
    expect(shots()).toHaveLength(2);
    channel.receive(result(second, 'miss'));
    expect(feedback()).toBe('Missed · projectile cleared');
    channel.receive(result(second, 'launched'));
    channel.receive(result(second, 'kill'));
    expect(feedback()).toBe('Missed · projectile cleared');
    click();
    expect(shots()[2].requestId).toBe(3);
  });

  test('repeat ready syncs the item counter without dropping pending selection or mixing shop requests', () => {
    ready(7);
    channel.receive(snapshot(70));
    document.querySelector('[data-shop-item="bow"] [data-shop-buy]').click();
    expect(channel.sent.find(command => command.type === 'shop-buy').requestId).toBe(71);
    click();
    const first = shots()[0];
    expect(first.requestId).toBe(8);
    channel.receive(itemState(50));
    channel.receive(itemState(2));
    channel.receive(itemState(null));
    channel.receive(snapshot(70));
    expect(inventory.selectedItemId).toBe(ids[0]);
    click();
    expect(shots()).toHaveLength(1);
    channel.receive(result(first, 'launched'));
    expect(feedback()).toBe('Projectile launched');
    click();
    expect(shots()[1].requestId).toBe(51);
  });

  test.each(['no-stock', 'invalid-item', 'invalid-request', 'unknown-player', 'cooldown',
    'stale-request', 'unavailable'])('%s rejects without changing inventory and releases pending', status => {
    ready();
    click();
    channel.receive(result(shots()[0], status));
    expect(feedback()).not.toContain('firing');
    expect(quantity()).toBe('1');
    click();
    expect(shots()[1].requestId).toBe(2);
  });

  test('offline send rejection causes no shot feedback or sound and does not leave pending stuck', () => {
    ready();
    channel.readyState = 'closed';
    click();
    expect(shots()).toEqual([]);
    expect(sound).not.toHaveBeenCalledWith('shoot');
    expect(feedback()).toContain('連線');
    channel.readyState = 'open';
    click();
    expect(shots()).toHaveLength(1);
    expect(sound).toHaveBeenCalledWith('shoot');
  });

  test('exhausted item request ids require reconnect rather than overflow or reuse', () => {
    ready(2147483646);
    click();
    const shot = shots()[0];
    expect(shot.requestId).toBe(2147483647);
    channel.receive(result(shot, 'launched'));
    click();
    expect(shots()).toHaveLength(1);
    expect(feedback()).toContain('重新連線');
    expect(feedback()).toContain('編號已用完');
  });

  test('disconnect clears stock pending mode and counters while old-channel results stay isolated', () => {
    ready(99);
    click();
    const old = channel;
    const oldShot = shots()[0];
    old.close();
    expect(inventory.selectedItemId).toBeNull();
    expect(document.querySelector('[data-inventory-panel]').hidden).toBe(true);
    channel = new Channel();
    cctv.attach(channel);
    channel.receive(cameraState);
    click();
    expect(shots()[0].type).toBe('cctv-fire');
    ready();
    click();
    const current = shots()[1];
    expect(current.requestId).toBe(1);
    old.receive(result(oldShot, 'kill'));
    old.receive(itemState(2147483647));
    old.close();
    channel.receive(result(current, 'launched'));
    expect(feedback()).toBe('Projectile launched');
    expect(inventory.selectedItemId).toBe(ids[0]);
    click();
    expect(shots()[2].requestId).toBe(2);
  });

  test('undefined legacy callbacks remain successful while explicit false rejects before firing UI', () => {
    aim.send = () => false;
    click();
    expect(feedback()).toBe('Click inside the video to fire');
    expect(document.querySelector('[data-crosshair]').hidden).toBe(true);
    expect(sound).not.toHaveBeenCalledWith('shoot');
    aim.send = () => undefined;
    video.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 400, clientY: 225 }));
    expect(feedback()).toBe('Target locked · firing…');
    expect(document.querySelector('[data-crosshair]').hidden).toBe(false);
    expect(sound).toHaveBeenCalledWith('shoot');
  });
});
