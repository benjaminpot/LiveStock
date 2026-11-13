import { readFileSync } from 'node:fs';
import { MoneyPanel } from '../public/receiver/js/money-panel.js';
import { CCTVPanel } from '../public/receiver/js/cctv-panel.js';

let moneyPanel;
let root;

beforeEach(() => {
  const html = readFileSync(new URL('../public/receiver/index.html', import.meta.url), 'utf8');
  const page = new DOMParser().parseFromString(html, 'text/html');
  document.body.replaceChildren(...page.body.childNodes);
  root = document.querySelector('[data-money-panel]');
  moneyPanel = new MoneyPanel(root);
});

const shownBalance = () => root.querySelector('[data-money-balance]').textContent;
const formatted = value => new Intl.NumberFormat().format(value);

test('waits hidden for an authoritative snapshot instead of inventing zero', () => {
  expect(root.hidden).toBe(true);
  expect(shownBalance()).toBe('—');
});

test('displays zero and consecutive server balances including spending', () => {
  for (const value of [0, 10000, 20000, 5000, 2147483647]) {
    moneyPanel.setState({ type: 'money-state', balance: value });
    expect(root.hidden).toBe(false);
    expect(shownBalance()).toBe(formatted(value));
  }
});

test('reset removes the previous balance until a new snapshot', () => {
  moneyPanel.setState({ type: 'money-state', balance: 10000 });
  moneyPanel.reset();
  expect(root.hidden).toBe(true);
  expect(shownBalance()).toBe('—');
  moneyPanel.setState({ type: 'money-state', balance: 0 });
  expect(root.hidden).toBe(false);
  expect(shownBalance()).toBe('0');
});

test('invalid snapshots neither reveal a wallet nor overwrite valid money', () => {
  const invalid = [null, {}, { type: 'cctv-state', balance: 2 },
    ...[-1, 0.5, NaN, Infinity, '10000', null, Number.MAX_SAFE_INTEGER + 1]
      .map(balance => ({ type: 'money-state', balance }))];
  for (const value of invalid) moneyPanel.setState(value);
  expect(root.hidden).toBe(true);
  expect(shownBalance()).toBe('—');
  moneyPanel.setState({ type: 'money-state', balance: 10000 });
  for (const value of invalid) moneyPanel.setState(value);
  expect(root.hidden).toBe(false);
  expect(shownBalance()).toBe(formatted(10000));
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

function connectedPanel() {
  const panel = new CCTVPanel(document.getElementById('cctvPanel'), document.getElementById('cctvBlackout'));
  panel.onMoneyState = state => moneyPanel.setState(state);
  panel.onReset = () => moneyPanel.reset();
  return panel;
}

test('replacing a channel isolates late messages and close from the old wallet', () => {
  const panel = connectedPanel();
  const a = new Channel();
  const b = new Channel();
  panel.attach(a);
  a.receive({ type: 'money-state', balance: 10000 });
  expect(shownBalance()).toBe(formatted(10000));
  panel.attach(b);
  expect(root.hidden).toBe(true);
  b.receive({ type: 'money-state', balance: 0 });
  a.receive({ type: 'money-state', balance: 99999 });
  a.close();
  expect(root.hidden).toBe(false);
  expect(shownBalance()).toBe('0');
  b.receive({ type: 'money-state', balance: 20000 });
  expect(shownBalance()).toBe(formatted(20000));
  b.close();
  expect(root.hidden).toBe(true);
  expect(shownBalance()).toBe('—');
});

test('money updates preserve camera controls and camera updates preserve money', () => {
  const panel = connectedPanel();
  const channel = new Channel();
  panel.attach(channel);
  channel.receive({ type: 'money-state', balance: 10000 });
  channel.receive({
    type: 'cctv-state', selectedIndex: 0, selectedIndices: [0, -1],
    cooldownRemaining: 0, heatLimit: 100,
    monitors: [{ name: 'Farm', status: 'watching', heat: 0, viewers: 1 },
      { name: 'Barn', status: 'ready', heat: 0, viewers: 0 }],
  });
  expect(document.querySelector('[data-current]').textContent).toBe('Farm');
  expect(document.querySelector('[data-next]').disabled).toBe(false);
  expect(shownBalance()).toBe(formatted(10000));
  channel.receive({ type: 'money-state', balance: 0 });
  expect(document.querySelector('[data-current]').textContent).toBe('Farm');
  expect(document.querySelector('[data-next]').disabled).toBe(false);
  expect(shownBalance()).toBe('0');
});
