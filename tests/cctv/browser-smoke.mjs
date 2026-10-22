// Real browser/DOM checks with fixture status packets, not a Unity/WebRTC test.
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { dirname, resolve, extname, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const publicRoot = resolve(projectRoot, 'WebApp/client/public');
const artifactRoot = resolve(projectRoot, 'Library/CCTVReview');
const { chromium } = process.env.CCTV_PLAYWRIGHT_MODULE
  ? await import(pathToFileURL(resolve(process.env.CCTV_PLAYWRIGHT_MODULE)).href)
  : await import('playwright');

const server = createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const target = resolve(publicRoot, '.' + pathname);
    if (!target.startsWith(publicRoot + sep)) throw new Error('Invalid fixture path');
    let content = await readFile(target);
    if (extname(target) === '.html') {
      // Isolate the panel: do not connect to Unity or load external sample scripts.
      content = content.toString().replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '');
    }
    response.setHeader('Content-Type', ({ '.html': 'text/html; charset=utf-8',
      '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.ico': 'image/x-icon' })[extname(target)] || 'application/octet-stream');
    response.end(content);
  } catch {
    response.writeHead(404).end();
  }
});
await new Promise(resolveListen => server.listen(0, '127.0.0.1', resolveListen));
const url = `http://127.0.0.1:${server.address().port}/receiver/index.html`;
let browser;
try {
  browser = await chromium.launch({ headless: true });
  const desktop = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const mobile = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const pc = await desktop.newPage();
  const phone = await mobile.newPage();
  const pageErrors = [];
  for (const page of [pc, phone]) {
    page.on('pageerror', error => pageErrors.push(error.message));
    await page.goto(url);
    await page.evaluate(async () => {
      const { CCTVPanel } = await import('./js/cctv-panel.js');
      const channel = new EventTarget();
      channel.readyState = 'open';
      window.sentCommands = [];
      channel.send = value => window.sentCommands.push(JSON.parse(value));
      document.querySelector('#player').innerHTML = '<video id="Video" playsinline></video>';
      const panel = new CCTVPanel(document.querySelector('#cctvPanel'), document.querySelector('#cctvBlackout'));
      panel.attach(channel);
      window.receiveState = state => channel.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(state) }));
    });
  }
  const monitors = [
    { name: 'CCTV 01', status: 'watching', heat: 25, recoveryRemaining: 0, viewers: 1 },
    { name: 'CCTV 02', status: 'watching', heat: 25, recoveryRemaining: 0, viewers: 1 }
  ];
  const packet = (selectedIndex, cooldownRemaining = 0) =>
    ({ type: 'cctv-state', selectedIndex, cooldownRemaining, heatLimit: 100, monitors });
  await pc.evaluate(state => window.receiveState(state), packet(0));
  await phone.evaluate(state => window.receiveState(state), packet(1, 3));
  await pc.getByRole('button', { name: 'CCTV 02', exact: true }).click();
  assert.deepEqual(await pc.evaluate(() => window.sentCommands.at(-1)), { type: 'cctv-select', index: 1, slot: 0 });
  assert.equal(await phone.locator('[data-current]').textContent(), 'CCTV 02');
  assert.equal(await phone.locator('[data-next]').isDisabled(), true);
  assert.deepEqual(await phone.evaluate(() => window.sentCommands), [{ type: 'cctv-ready' }]);
  // A request must wait for Unity's reply; the UI cannot change selection itself.
  assert.equal(await pc.locator('[data-current]').textContent(), 'CCTV 01');
  await phone.evaluate(state => window.receiveState(state), packet(1));
  await phone.getByRole('button', { name: 'CCTV 01', exact: true }).tap();
  assert.deepEqual(await phone.evaluate(() => window.sentCommands.at(-1)), { type: 'cctv-select', index: 0, slot: 0 });
  for (const page of [pc, phone]) {
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true,
      'panel should fit its viewport without horizontal overflow');
  }
  await mkdir(artifactRoot, { recursive: true });
  await pc.screenshot({ path: resolve(artifactRoot, 'desktop.png'), fullPage: true });
  monitors[1] = { name: 'CCTV 02', status: 'overheated', heat: 100, recoveryRemaining: 8, viewers: 0 };
  await phone.evaluate(state => window.receiveState(state), packet(1));
  assert.equal(await phone.locator('#cctvBlackout').isVisible(), true);
  assert.match(await phone.locator('#cctvBlackout').textContent(), /8 秒/);
  await phone.screenshot({ path: resolve(artifactRoot, 'mobile-overheated.png'), fullPage: true });
  for (const page of [pc, phone]) {
    await page.evaluate(state => window.receiveState(state), { ...packet(0), selectedIndices:[0,1] });
    assert.equal(await page.locator('#secondaryScreen').isVisible(),true);
    await page.locator('[data-slot]').selectOption('1');
    await page.getByRole('button', {name:'CCTV 01',exact:true}).click();
    assert.deepEqual(await page.evaluate(()=>window.sentCommands.at(-1)), {type:'cctv-select',index:0,slot:1});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);
    assert.equal(await page.locator('#cctvBlackout').isVisible(),false);
    assert.equal(await page.locator('#secondaryBlackout').isVisible(),true);
  }
  await pc.screenshot({ path: resolve(artifactRoot,'desktop-dual.png'), fullPage:true });
  await phone.screenshot({ path: resolve(artifactRoot,'mobile-dual.png'), fullPage:true });
  assert.deepEqual(pageErrors, []);
  console.log('Browser panel smoke checks passed: independent commands, cooldown, touch, overheat and viewport layout.');
  console.log(`Screenshots: ${artifactRoot}`);
} finally {
  if (browser) await browser.close();
  await new Promise(resolveClose => server.close(resolveClose));
}
