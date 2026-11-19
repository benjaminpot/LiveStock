import { readFileSync } from 'node:fs';
import { ShopPanel } from '../public/receiver/js/shop-panel.js';
import { CCTVPanel } from '../public/receiver/js/cctv-panel.js';
import { MoneyPanel } from '../public/receiver/js/money-panel.js';

const ids = ['capture-ball', 'bow', 'sniper', 'bomb'];
const format = value => new Intl.NumberFormat().format(value);
const snapshot = (lastRequestId = 0, quantity = 0) => ({
  type: 'shop-state', lastRequestId,
  items: ids.map((id, index) => ({ id, name: ['寶貝球', '弓箭', '狙擊槍', '炸彈'][index],
    price: index < 2 ? 100 : 599, quantity }))
});
const result = (requestId, status = 'success', itemId = ids[0]) => ({
  type: 'shop-result', requestId, itemId, status
});
const cameraState = {
  type: 'cctv-state', selectedIndex: 0, selectedIndices: [0, -1],
  cooldownRemaining: 0, heatLimit: 100,
  monitors: [{ name: 'Farm', status: 'watching', heat: 0, viewers: 1 },
    { name: 'Barn', status: 'ready', heat: 0, viewers: 0 }]
};

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

describe.each(['index.html', 'live.html'])('shop consumer on %s', pageName => {
  let root;
  let shop;
  let commands;
  const card = id => root.querySelector(`[data-shop-item="${id}"]`);
  const text = (id, field) => card(id).querySelector(`[data-shop-${field}]`).textContent;
  const click = (id = ids[0]) => card(id).querySelector('[data-shop-buy]').click();
  const buttons = () => [...root.querySelectorAll('[data-shop-buy]')];
  const feedback = () => root.querySelector('[data-shop-status]').textContent;

  beforeEach(() => {
    const html = readFileSync(new URL(`../public/receiver/${pageName}`, import.meta.url), 'utf8');
    const page = new DOMParser().parseFromString(html, 'text/html');
    document.body.replaceChildren(...page.body.childNodes);
    root = document.querySelector('[data-shop-panel]');
    commands = [];
    shop = new ShopPanel(root, command => commands.push(command));
  });

  function connect() {
    const cctv = new CCTVPanel(document.getElementById('cctvPanel'), document.getElementById('cctvBlackout'));
    const money = new MoneyPanel(document.querySelector('[data-money-panel]'));
    shop.onPurchase = command => cctv.send(command);
    cctv.onShopState = state => shop.setState(state);
    cctv.onShopResult = state => shop.showResult(state);
    cctv.onMoneyState = state => money.setState(state);
    cctv.onReset = () => { shop.reset(); money.reset(); };
    return cctv;
  }

  test('waits for a complete server catalog without inventing prices or inventory', () => {
    expect(buttons().every(button => button.disabled && button.textContent === '尚未開放')).toBe(true);
    for (const id of ids) {
      expect(text(id, 'price')).toBe('—');
      expect(text(id, 'quantity')).toBe('—');
    }
    click();
    expect(commands).toEqual([]);
    shop.showResult(result(1));
    expect(text(ids[0], 'quantity')).toBe('—');
  });

  test('renders only authoritative names prices and integer-sized quantities', () => {
    const state = snapshot(0, 2147483647);
    state.items[0].name = '<img src=x onerror=alert(1)>';
    state.items[0].price = 2147483647;
    shop.setState(state);
    expect(text(ids[0], 'name')).toBe(state.items[0].name);
    expect(card(ids[0]).querySelector('img')).toBeNull();
    expect(text(ids[0], 'price')).toBe(format(2147483647));
    expect(text(ids[0], 'quantity')).toBe(format(2147483647));
    expect(buttons().every(button => !button.disabled)).toBe(true);
    shop.setState(snapshot(0, 3));
    expect(text(ids[0], 'quantity')).toBe('3');
  });

  test('allows only one pending purchase and snapshots do not release it', () => {
    shop.setState(snapshot());
    click();
    expect(commands).toEqual([{ type: 'shop-buy', itemId: ids[0], requestId: 1 }]);
    click(ids[1]);
    shop.setState(snapshot(1, 1));
    shop.setState(snapshot(1, 1));
    expect(buttons().every(button => button.disabled)).toBe(true);
    expect(feedback()).toContain('處理中');
    click(ids[2]);
    expect(commands).toHaveLength(1);
    shop.showResult(result(1));
    expect(buttons().every(button => !button.disabled)).toBe(true);
    expect(feedback()).toContain('購買成功');
    expect(text(ids[0], 'quantity')).toBe('1');
  });

  test.each(['success', 'insufficient-funds'])('%s gives feedback without locally granting inventory or changing money', status => {
    const cctv = connect();
    const channel = new Channel();
    cctv.attach(channel);
    channel.receive({ type: 'money-state', balance: 0 });
    channel.receive(snapshot());
    click();
    expect(channel.sent).toEqual([{ type: 'shop-buy', requestId: 1, itemId: ids[0] }]);
    channel.receive(result(1, status));
    expect(feedback()).toContain(status === 'success' ? '購買成功' : '餘額不足');
    expect(text(ids[0], 'quantity')).toBe('0');
    expect(document.querySelector('[data-money-balance]').textContent).toBe('0');
    expect(buttons().every(button => !button.disabled)).toBe(true);
    channel.receive(snapshot(1, status === 'success' ? 1 : 0));
    expect(text(ids[0], 'quantity')).toBe(status === 'success' ? '1' : '0');
  });

  test('invalid snapshots cannot reveal or overwrite a catalog', () => {
    const invalid = [null, {}, { ...snapshot(), lastRequestId: -1 },
      { ...snapshot(), lastRequestId: 2147483648 }, { ...snapshot(), lastRequestId: 0.5 },
      { ...snapshot(), items: snapshot().items.slice(1) }];
    for (const field of ['price', 'quantity']) {
      for (const value of [-1, 1.5, 2147483648, '100', null]) {
        const state = snapshot();
        state.items[0][field] = value;
        invalid.push(state);
      }
    }
    for (const item of [null, { ...snapshot().items[0], id: 'other' },
      { ...snapshot().items[0], name: '' }, { ...snapshot().items[0], name: 100 }]) {
      const state = snapshot();
      state.items[0] = item;
      invalid.push(state);
    }
    const duplicate = snapshot();
    duplicate.items[1] = duplicate.items[0];
    invalid.push(duplicate);
    for (const state of invalid) shop.setState(state);
    expect(text(ids[0], 'quantity')).toBe('—');
    expect(buttons().every(button => button.disabled)).toBe(true);
    shop.setState(snapshot(5, 7));
    for (const state of [...invalid, snapshot(4, 99)]) shop.setState(state);
    expect(text(ids[0], 'quantity')).toBe('7');
    expect(text(ids[0], 'price')).toBe('100');
    expect(buttons().every(button => !button.disabled)).toBe(true);
  });

  test('invalid late and unmatched results preserve pending and valid feedback', () => {
    shop.setState(snapshot());
    click();
    const pendingFeedback = feedback();
    for (const state of [null, {}, result(0), result(-1), result(1.5), result('1'),
      result(2147483648), result(2), result(1, 'unexpected'), result(1, 'success', ids[1]),
      result(1, ['success']), result(1, null), result(1, {}),
      { ...result(1), type: 'other' }]) shop.showResult(state);
    expect(feedback()).toBe(pendingFeedback);
    expect(buttons().every(button => button.disabled)).toBe(true);
    shop.showResult(result(1, 'insufficient-funds'));
    const validFeedback = feedback();
    shop.showResult(result(1));
    expect(feedback()).toBe(validFeedback);
    click();
    shop.showResult(result(1));
    expect(buttons().every(button => button.disabled)).toBe(true);
    shop.showResult(result(2));
    expect(feedback()).toContain('購買成功');
    expect(text(ids[0], 'quantity')).toBe('0');
  });

  test('repeated ready snapshots never reuse locally used request IDs', () => {
    shop.setState(snapshot(40));
    click();
    shop.setState(snapshot(40));
    shop.showResult(result(41, 'insufficient-funds'));
    shop.setState(snapshot(40));
    click();
    shop.showResult(result(42));
    shop.setState(snapshot(100));
    click(ids[1]);
    expect(commands.map(command => command.requestId)).toEqual([41, 42, 101]);
  });

  test('integer request exhaustion never wraps and reconnect can restart', () => {
    shop.setState(snapshot(2147483646));
    click();
    expect(commands[0].requestId).toBe(2147483647);
    shop.showResult(result(2147483647));
    click();
    expect(commands).toHaveLength(1);
    expect(buttons().every(button => button.disabled)).toBe(true);
    expect(feedback()).toContain('請重新連線');
    shop.reset();
    shop.setState(snapshot());
    click();
    expect(commands[1].requestId).toBe(1);
  });

  test('reset forgets the previous player inventory pending and feedback', () => {
    shop.setState(snapshot(8, 25));
    click();
    shop.reset();
    shop.showResult(result(9));
    expect(text(ids[0], 'quantity')).toBe('—');
    expect(text(ids[0], 'price')).toBe('—');
    expect(feedback()).not.toContain('成功');
    expect(feedback()).not.toContain('處理中');
    expect(buttons().every(button => button.disabled)).toBe(true);
    shop.setState(snapshot(0, 0));
    expect(text(ids[0], 'quantity')).toBe('0');
  });

  test('old channel messages and close cannot overwrite or unlock a new player shop', () => {
    const cctv = connect();
    const oldChannel = new Channel();
    const current = new Channel();
    cctv.attach(oldChannel);
    oldChannel.receive(snapshot(0, 5));
    click();
    cctv.attach(current);
    expect(text(ids[0], 'quantity')).toBe('—');
    current.receive(snapshot(0, 2));
    click();
    oldChannel.receive(snapshot(1, 99));
    oldChannel.receive(result(1));
    oldChannel.close();
    expect(text(ids[0], 'quantity')).toBe('2');
    expect(buttons().every(button => button.disabled)).toBe(true);
    current.receive(snapshot(1, 3));
    current.receive(result(1));
    expect(text(ids[0], 'quantity')).toBe('3');
    expect(feedback()).toContain('購買成功');
    current.close();
    expect(text(ids[0], 'quantity')).toBe('—');
    expect(buttons().every(button => button.disabled)).toBe(true);
  });

  test('shop money and CCTV states coexist while retaining one ready handshake', () => {
    const cctv = connect();
    const channel = new Channel();
    cctv.attach(channel);
    channel.receive(snapshot());
    channel.receive({ type: 'money-state', balance: 10000 });
    expect(channel.sent).toEqual([]);
    channel.receive(cameraState);
    channel.receive(cameraState);
    expect(channel.sent).toEqual([{ type: 'cctv-ready' }]);
    click();
    channel.receive({ type: 'money-state', balance: 9900 });
    channel.receive(snapshot(1, 1));
    channel.receive(result(1));
    expect(document.querySelector('[data-current]').textContent).toBe('Farm');
    expect(document.querySelector('[data-next]').disabled).toBe(false);
    expect(document.querySelector('[data-money-balance]').textContent).toBe(format(9900));
    expect(text(ids[0], 'quantity')).toBe('1');
    channel.receive(cameraState);
    expect(channel.sent.filter(command => command.type === 'cctv-ready')).toHaveLength(1);
    expect(text(ids[0], 'quantity')).toBe('1');
    expect(document.querySelector('[data-money-balance]').textContent).toBe(format(9900));
  });
});
