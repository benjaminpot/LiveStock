import { chromium } from '../../.tmp/FusionBrowser/node_modules/playwright/index.mjs';
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, createWriteStream, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// Optional local integration companion for TestFusionStandalone.ps1 -WithBrowser.
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = path.resolve(process.argv[2] ?? '.tmp/FusionBrowser/browser-evidence.json');
mkdirSync(path.dirname(output), { recursive: true });
const log = createWriteStream(path.join(path.dirname(output), 'signaling.log'));
const server = spawn(process.execPath, ['build/index.js', '-p', '80', '-l', 'none'], {
  cwd: path.join(root, 'WebApp'), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']
});
server.stdout.pipe(log);
server.stderr.pipe(log);
const evidence = { pass: false, ready: false, completed: false, reconnected: false, errors: [], samples: [] };
const save = () => {
  evidence.pass = evidence.ready && evidence.completed && evidence.reconnected && evidence.errors.length === 0;
  const json = JSON.stringify(evidence, null, 2);
  writeFileSync(output, json);
  writeFileSync(path.join(root, 'Docs/Fusion_CCTV_Browser_Report.json'), json);
};
let browser;
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

async function waitFor(check, label, seconds = 120) {
  const deadline = Date.now() + seconds * 1000;
  while (!await check()) {
    if (Date.now() > deadline) throw new Error(`Timed out: ${label}`);
    await delay(500);
  }
}

async function snapshot(page) {
  return page.evaluate(() => {
    const video = document.querySelector('#player video');
    let pixelRange = 0, nonBlackPixels = 0;
    if (video?.videoWidth) {
      const canvas = document.createElement('canvas');
      canvas.width = 32;
      canvas.height = 18;
      const context = canvas.getContext('2d', { willReadFrequently: true });
      context.drawImage(video, 0, 0, 32, 18);
      const pixels = context.getImageData(0, 0, 32, 18).data;
      let minimum = 255, maximum = 0;
      for (let i = 0; i < pixels.length; i += 4) {
        const value = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
        minimum = Math.min(minimum, value);
        maximum = Math.max(maximum, value);
        if (value > 8) nonBlackPixels++;
      }
      pixelRange = maximum - minimum;
    }
    return {
      mission: document.querySelector('[data-mission-status]')?.textContent,
      blackout: !document.getElementById('cctvBlackout').hidden,
      connection: document.querySelector('[data-connection]')?.textContent,
      width: video?.videoWidth ?? 0,
      frames: video?.getVideoPlaybackQuality?.().totalVideoFrames ?? 0,
      pixelRange, nonBlackPixels
    };
  });
}

async function connect(page) {
  await page.goto('http://127.0.0.1/receiver/live.html');
  await page.locator('#playButton').click();
}

try {
  save();
  await waitFor(async () => {
    try { return (await fetch('http://127.0.0.1/config')).ok; } catch { return false; }
  }, 'local signaling HTTP', 20);
  if (server.exitCode !== null) throw new Error(`Local signaling exited: ${server.exitCode}`);
  browser = await chromium.launch({
    executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    headless: true, args: ['--autoplay-policy=no-user-gesture-required']
  });
  // Keep signaling available during map load, but do not consume camera heat before the video check starts.
  await waitFor(() => existsSync(output + '.connect'), 'map ready for browser viewers', 150);
  const pages = await Promise.all([0, 1].map(async () => {
    const context = await browser.newContext();
    const page = await context.newPage();
    page.on('pageerror', error => { evidence.errors.push(error.message); save(); });
    await connect(page);
    return page;
  }));
  let samples;
  await waitFor(async () => {
    samples = await Promise.all(pages.map(snapshot));
    evidence.latest = samples;
    if (!evidence.firstDecoded && samples.every(s => s.frames > 0)) {
      evidence.firstDecoded = samples;
      writeFileSync(path.join(path.dirname(output), 'server.json.command'), 'capture-cctv');
    }
    save();
    if (samples.every(s => s.frames > 120 && !s.blackout && s.pixelRange === 0))
      throw new Error('Both browsers decode black video while cameras are not overheated.');
    return samples.every(s => s.width > 0 && s.frames > 0 && s.pixelRange > 20 && s.nonBlackPixels > 30 && s.mission?.includes('0/3'));
  }, 'two independent browser video streams and 0/3 snapshot');
  const initial = samples;
  await delay(1500);
  samples = await Promise.all(pages.map(snapshot));
  if (!samples.every((s, i) => s.frames > initial[i].frames)) throw new Error('Video decoding did not advance in both browsers.');
  evidence.samples.push({ stage: 'initial', browsers: samples });
  await pages[0].screenshot({ path: path.join(path.dirname(output), 'browser-initial.png') });
  evidence.ready = true;
  save();
  await waitFor(async () => {
    samples = await Promise.all(pages.map(snapshot));
    return samples.every(s => s.mission === '蘋果已收集完成！');
  }, 'completion on both browser channels');
  evidence.samples.push({ stage: 'completed', browsers: samples });
  evidence.completed = true;
  save();
  await connect(pages[1]);
  await waitFor(async () => {
    const s = await snapshot(pages[1]);
    return s.mission === '蘋果已收集完成！' && s.width > 0 && s.frames > 0 && s.pixelRange > 20 && s.nonBlackPixels > 30;
  }, 'reconnected browser receives current completion and video');
  evidence.samples.push({ stage: 'reconnected', browsers: await Promise.all(pages.map(snapshot)) });
  await pages[1].screenshot({ path: path.join(path.dirname(output), 'browser-reconnected.png') });
  evidence.reconnected = true;
  if (evidence.errors.length) throw new Error('Browser page errors were recorded.');
  save();
  await browser.close();
  browser = null;
  await waitFor(() => existsSync(output + '.stop'), 'standalone process cleanup', 180);
} catch (error) {
  evidence.errors.push(error.stack ?? error.message);
  save();
  process.exitCode = 1;
} finally {
  await browser?.close();
  server.kill();
  log.end();
}
