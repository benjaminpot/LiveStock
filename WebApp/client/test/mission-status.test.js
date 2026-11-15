import { readFileSync } from 'node:fs';
import { CCTVPanel, formatMissionStatus } from '../public/receiver/js/cctv-panel.js';

test('mission feedback distinguishes waiting, shared progress, completion and invalid snapshots', () => {
  expect(formatMissionStatus({ collected: 0, required: 3, running: false })).toBe('等待羊方開始收集');
  expect(formatMissionStatus({ collected: 2, required: 3, running: true })).toContain('2/3');
  expect(formatMissionStatus({ collected: 3, required: 3, completed: true })).toBe('蘋果已收集完成！');
  expect(formatMissionStatus({ collected: 4, required: 3, completed: true })).toBeNull();
  expect(formatMissionStatus({ collected: 1.5, required: 3 })).toBeNull();
});

test.each([
  { collected: 3, required: 3, completed: 'false' },
  { collected: 1, required: 3, running: 'false' },
  { collected: 3, required: 3, completed: 1 },
  { collected: 1, required: 3, running: null },
  { collected: 2, required: 3, completed: true, running: true }
])('rejects malformed flags and inconsistent completion: %j', snapshot => {
  expect(formatMissionStatus(snapshot)).toBeNull();
});

test.each(['index.html', 'live.html'])('%s consumes reconnect snapshots and ignores an old channel', page => {
  document.body.innerHTML = readFileSync(new URL(`../public/receiver/${page}`, import.meta.url), 'utf8');
  const panel = new CCTVPanel(document.getElementById('cctvPanel'), document.getElementById('cctvBlackout'));
  const channel = () => { const c = new EventTarget(); c.readyState = 'open'; c.send = () => {}; return c; };
  const send = (c, snapshot) => c.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(snapshot) }));
  const old = channel();
  panel.attach(old);
  send(old, { type: 'mission-status', collected: 2, required: 3, running: true });
  expect(panel.mission.textContent).toContain('2/3');
  const current = channel();
  panel.attach(current);
  send(current, { type: 'mission-status', collected: 3, required: 3, completed: true });
  expect(panel.mission.textContent).toBe('蘋果已收集完成！');
  send(old, { type: 'mission-status', collected: 0, required: 3 });
  expect(panel.mission.textContent).toBe('蘋果已收集完成！');
  send(current, { type: 'mission-status', collected: 0, required: 3, running: 'false' });
  expect(panel.mission.textContent).toBe('蘋果已收集完成！');
  panel.reset();
  expect(panel.mission.textContent).toBe('等待任務資料');
});
