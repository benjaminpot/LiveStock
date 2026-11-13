import { readFileSync } from 'node:fs';
import { InventoryPanel } from '../public/receiver/js/inventory-panel.js';
import { ShopPanel } from '../public/receiver/js/shop-panel.js';
import { CCTVPanel } from '../public/receiver/js/cctv-panel.js';

const ids = ['capture-ball', 'bow', 'sniper', 'bomb'];
const names = ['寶貝球', '弓箭', '狙擊槍', '炸彈'];
const snapshot = (lastRequestId = 0, quantities = [0, 0, 0, 0]) => ({
  type: 'shop-state', lastRequestId,
  items: ids.map((id, i) => ({ id, name: names[i], price: i < 2 ? 10000 : 59900, quantity: quantities[i] }))
});

class Channel extends EventTarget {
  readyState = 'open';
  send() {}
  receive(state) {
    this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(state) }));
  }
  close() {
    this.readyState = 'closed';
    this.dispatchEvent(new Event('close'));
  }
}

describe.each(['live.html', 'index.html'])('inventory consumer on %s', pageName => {
  let root;
  let inventory;
  const slot = id => root.querySelector(`[data-inventory-item="${id}"]`);
  const quantity = id => slot(id).querySelector('[data-inventory-quantity]').textContent;
  const pressed = () => [...root.querySelectorAll('[aria-pressed="true"]')];
  const status = () => root.querySelector('[data-inventory-status]').textContent;

  beforeEach(() => {
    const html = readFileSync(new URL(`../public/receiver/${pageName}`, import.meta.url), 'utf8');
    const parsed = new DOMParser().parseFromString(html, 'text/html');
    document.body.replaceChildren(...parsed.body.childNodes);
    root = document.querySelector('[data-inventory-panel]');
    inventory = new InventoryPanel(root);
  });

  test('does not invent an offline inventory and distinguishes an authoritative empty inventory', () => {
    expect(root.hidden).toBe(true);
    expect(inventory.selectedItemId).toBeNull();
    for (const id of ids) {
      expect(quantity(id)).toBe('—');
      expect(slot(id).disabled).toBe(true);
    }
    inventory.setState(snapshot());
    expect(root.hidden).toBe(false);
    expect(root.querySelector('[data-inventory-empty]').hidden).toBe(false);
    for (const id of ids) {
      expect(quantity(id)).toBe('0');
      expect(slot(id).disabled).toBe(true);
      slot(id).click();
    }
    expect(inventory.selectedItemId).toBeNull();
  });

  test('a purchase snapshot makes the owned slot selectable without selecting it for the player', () => {
    inventory.setState(snapshot());
    inventory.setState(snapshot(1, [1, 0, 0, 0]));
    expect(quantity('capture-ball')).toBe('1');
    expect(slot('capture-ball').disabled).toBe(false);
    expect(slot('bow').disabled).toBe(true);
    expect(root.querySelector('[data-inventory-empty]').hidden).toBe(true);
    expect(inventory.selectedItemId).toBeNull();
    slot('capture-ball').click();
    expect(inventory.selectedItemId).toBe('capture-ball');
    expect(pressed()).toEqual([slot('capture-ball')]);
    expect(status()).toContain('寶貝球');
    expect(quantity('capture-ball')).toBe('1');
  });

  test('only one owned item is selected and cancel clears the selection', () => {
    inventory.setState(snapshot(2, [1, 2, 0, 0]));
    slot('capture-ball').click();
    slot('bow').click();
    expect(inventory.selectedItemId).toBe('bow');
    expect(pressed()).toEqual([slot('bow')]);
    expect(status()).toContain('弓箭');
    expect(status()).toContain('2');
    slot('bomb').click();
    expect(inventory.selectedItemId).toBe('bow');
    expect(quantity('bow')).toBe('2');
    root.querySelector('[data-inventory-clear]').click();
    expect(inventory.selectedItemId).toBeNull();
    expect(pressed()).toEqual([]);
    expect(root.querySelector('[data-inventory-clear]').disabled).toBe(true);
    expect(quantity('bow')).toBe('2');
  });

  test('ready and purchase updates preserve a valid selection but zero stock clears it', () => {
    inventory.setState(snapshot(1, [1, 0, 0, 0]));
    slot('capture-ball').click();
    inventory.setState(snapshot(1, [1, 0, 0, 0]));
    expect(inventory.selectedItemId).toBe('capture-ball');
    inventory.setState(snapshot(2, [3, 1, 0, 0]));
    expect(inventory.selectedItemId).toBe('capture-ball');
    expect(quantity('capture-ball')).toBe('3');
    expect(status()).toContain('3');
    inventory.setState(snapshot(2, [0, 1, 0, 0]));
    expect(inventory.selectedItemId).toBeNull();
    expect(slot('capture-ball').disabled).toBe(true);
    expect(pressed()).toEqual([]);
    expect(slot('bow').disabled).toBe(false);
  });

  test('older or malformed snapshots do not replace a valid inventory or selection', () => {
    inventory.setState(snapshot(5, [2, 1, 0, 0]));
    slot('bow').click();
    const badQuantity = snapshot(6);
    badQuantity.items[0].quantity = -1;
    const duplicate = snapshot(6);
    duplicate.items[1].id = duplicate.items[0].id;
    const badName = snapshot(6);
    badName.items[0].name = null;
    const fraction = snapshot(6);
    fraction.items[1].quantity = 0.5;
    const overflow = snapshot(6);
    overflow.items[1].quantity = 2147483648;
    for (const state of [null, {}, snapshot(4), badQuantity, duplicate, badName, fraction, overflow]) {
      inventory.setState(state);
      expect(quantity('capture-ball')).toBe('2');
      expect(quantity('bow')).toBe('1');
      expect(inventory.selectedItemId).toBe('bow');
      expect(pressed()).toEqual([slot('bow')]);
    }
  });

  test('server names stay text and full int quantities remain readable data', () => {
    const state = snapshot(1, [2147483647, 0, 0, 0]);
    state.items[0].name = '<img src=x onerror=alert(1)>';
    inventory.setState(state);
    slot('capture-ball').click();
    expect(slot('capture-ball').querySelector('[data-inventory-name]').textContent).toBe(state.items[0].name);
    expect(slot('capture-ball').querySelector('img')).toBeNull();
    expect(quantity('capture-ball')).toBe(new Intl.NumberFormat().format(2147483647));
    expect(inventory.selectedItemId).toBe('capture-ball');
  });

  test('disconnect reset removes names quantities and selection before a new player snapshot', () => {
    inventory.setState(snapshot(8, [1, 0, 0, 0]));
    slot('capture-ball').click();
    inventory.reset();
    expect(root.hidden).toBe(true);
    expect(inventory.selectedItemId).toBeNull();
    expect(pressed()).toEqual([]);
    expect(quantity('capture-ball')).toBe('—');
    inventory.setState(snapshot());
    expect(root.hidden).toBe(false);
    expect(quantity('capture-ball')).toBe('0');
    expect(slot('capture-ball').disabled).toBe(true);
    expect(inventory.selectedItemId).toBeNull();
  });

  test('one player selection cannot change another panel selection or inventory', () => {
    const secondRoot = root.cloneNode(true);
    document.body.append(secondRoot);
    const other = new InventoryPanel(secondRoot);
    inventory.setState(snapshot(1, [1, 0, 0, 0]));
    other.setState(snapshot(1, [0, 2, 0, 0]));
    slot('capture-ball').click();
    secondRoot.querySelector('[data-inventory-item="bow"]').click();
    inventory.reset();
    expect(other.selectedItemId).toBe('bow');
    expect(secondRoot.hidden).toBe(false);
    expect(secondRoot.querySelector('[data-inventory-item="bow"] [data-inventory-quantity]').textContent).toBe('2');
  });

  test('late old-channel snapshots and close cannot overwrite the new inventory or selection', () => {
    const cctv = new CCTVPanel(document.getElementById('cctvPanel'), document.getElementById('cctvBlackout'));
    const shop = new ShopPanel(document.querySelector('[data-shop-panel]'), command => cctv.send(command));
    cctv.onShopState = state => { shop.setState(state); inventory.setState(state); };
    cctv.onReset = () => { shop.reset(); inventory.reset(); };
    const a = new Channel();
    const b = new Channel();
    cctv.attach(a);
    a.receive(snapshot(1, [1, 0, 0, 0]));
    slot('capture-ball').click();
    cctv.attach(b);
    expect(root.hidden).toBe(true);
    expect(inventory.selectedItemId).toBeNull();
    b.receive(snapshot(0, [0, 2, 0, 0]));
    slot('bow').click();
    a.receive(snapshot(99, [999, 0, 0, 0]));
    a.close();
    expect(root.hidden).toBe(false);
    expect(inventory.selectedItemId).toBe('bow');
    expect(quantity('capture-ball')).toBe('0');
    expect(quantity('bow')).toBe('2');
    b.close();
    expect(root.hidden).toBe(true);
    expect(inventory.selectedItemId).toBeNull();
  });
});
