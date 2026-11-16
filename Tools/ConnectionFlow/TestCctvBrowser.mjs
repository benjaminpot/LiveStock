import { chromium } from '../../.tmp/FusionBrowser/node_modules/playwright/index.mjs';
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync, createWriteStream, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// Optional local integration companion for TestFusionStandalone.ps1 -WithBrowser.
const root = fileURLToPath(new URL('../../', import.meta.url));
const output = path.resolve(process.argv[2] ?? '.tmp/FusionBrowser/browser-evidence.json');
const host = process.argv[3] ?? 'StandaloneHidden';
const reportName = host === 'EditorMultiPeer' ? 'Fusion_CCTV_Editor_Browser_Report.json'
  : host === 'StandaloneInteractiveServer' ? 'Fusion_CCTV_Interactive_Browser_Report.json' : 'Fusion_CCTV_Browser_Report.json';
mkdirSync(path.dirname(output), { recursive: true });
const log = createWriteStream(path.join(path.dirname(output), 'signaling.log'));
const server = spawn(process.execPath, ['build/index.js', '-p', '80', '-l', 'none'], {
  cwd: path.join(root, 'WebApp'), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe']
});
server.stdout.pipe(log);
server.stderr.pipe(log);
const evidence = { pass: false, host, ready: false, completed: false, reconnected: false, errors: [], samples: [] };
const save = () => {
  evidence.pass = evidence.ready && evidence.completed && evidence.reconnected && evidence.errors.length === 0;
  const json = JSON.stringify(evidence, null, 2);
  writeFileSync(output, json);
  return json;
};
let browser;
const pages = [];
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

// Publish at the boundaries; repeatedly replacing a tracked report can collide with Windows file readers.
async function publish() {
  for (let attempt = 0; ; attempt++) {
    try { writeFileSync(path.join(root, 'Docs', reportName), save()); return; }
    catch (error) {
      if (attempt >= 9 || !['UNKNOWN', 'EBUSY', 'EPERM'].includes(error.code)) throw error;
      await delay(100);
    }
  }
}

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
    const quality = video?.getVideoPlaybackQuality?.();
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
      camera: document.querySelector('[data-current]')?.textContent,
      blackout: !document.getElementById('cctvBlackout').hidden,
      connection: document.querySelector('[data-connection]')?.textContent,
      width: video?.videoWidth ?? 0,
      frames: (quality?.totalVideoFrames ?? 0) - (quality?.droppedVideoFrames ?? 0),
      droppedFrames: quality?.droppedVideoFrames ?? 0,
      missionHistory: window.__fusionMissionHistory ?? [],
      pixelRange, nonBlackPixels
    };
  });
}

async function connect(page, cameraIndex = 0) {
  await page.goto('http://127.0.0.1/receiver/live.html');
  await page.locator('#playButton').click();
  if (cameraIndex === 1) {
    // A reconnect can restore this view; its selected button is intentionally aria-disabled.
    await page.waitForFunction(() => {
      const button = document.querySelectorAll('.cctv-monitor button')[1];
      return button?.getAttribute('aria-pressed') === 'true' || button?.getAttribute('aria-disabled') === 'false';
    }, null, { timeout: 30000 });
    const button = page.locator('.cctv-monitor button').nth(1);
    if (await button.getAttribute('aria-pressed') !== 'true') await button.click();
    await page.evaluate(() => window.scrollTo(0, 0));
  }
}

try {
  await publish();
  await waitFor(async () => {
    try { return (await fetch('http://127.0.0.1/config')).ok; } catch { return false; }
  }, 'local signaling HTTP', 20);
  if (server.exitCode !== null) throw new Error(`Local signaling exited: ${server.exitCode}`);
  browser = await chromium.launch({
    executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe',
    headless: true, args: ['--autoplay-policy=no-user-gesture-required']
  });
  evidence.browserVersion = browser.version();
  // Keep signaling available during map load, but do not consume camera heat before the video check starts.
  await waitFor(() => existsSync(output + '.connect'), 'map ready for browser viewers', 150);
  await Promise.all([0, 1].map(async (_, index) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    pages[index] = page;
    await page.addInitScript(() => document.addEventListener('DOMContentLoaded', () => {
      const status = document.querySelector('[data-mission-status]');
      window.__fusionMissionHistory = [status.textContent];
      new MutationObserver(() => {
        if (window.__fusionMissionHistory.at(-1) !== status.textContent)
          window.__fusionMissionHistory.push(status.textContent);
      }).observe(status, { childList: true, subtree: true, characterData: true });
    }));
    page.on('pageerror', error => { evidence.errors.push(error.message); save(); });
    await connect(page, index);
    return page;
  }));
  let samples;
  await waitFor(async () => {
    samples = await Promise.all(pages.map(snapshot));
    evidence.latest = samples;
    if (!evidence.firstDecoded && samples.some(s => s.frames > 0)) {
      evidence.firstDecoded = samples;
      writeFileSync(path.join(path.dirname(output), 'server.json.command'), 'capture-cctv');
    }
    save();
    if (samples.every(s => s.frames > 120 && !s.blackout && s.pixelRange === 0))
      throw new Error('Both browsers decode black video while cameras are not overheated.');
    return samples.every(s => s.width > 0 && s.frames > 0 && s.pixelRange > 20 && s.nonBlackPixels > 30 && s.mission?.includes('0/3'))
      && samples[0].camera !== samples[1].camera;
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
  if (!samples.every(s => s.missionHistory.some(text => text.includes('1/3')) && s.missionHistory.some(text => text.includes('2/3'))))
    throw new Error('A browser missed intermediate shared mission progress.');
  evidence.completed = true;
  save();
  await connect(pages[1], 1);
  await waitFor(async () => {
    const s = await snapshot(pages[1]);
    return s.mission === '蘋果已收集完成！' && s.width > 0 && s.frames > 0 && s.pixelRange > 20 && s.nonBlackPixels > 30;
  }, 'reconnected browser receives current completion and video');
  evidence.samples.push({ stage: 'reconnected', browsers: await Promise.all(pages.map(snapshot)) });
  await pages[1].screenshot({ path: path.join(path.dirname(output), 'browser-reconnected.png') });
  evidence.reconnected = true;
  if (evidence.errors.length) throw new Error('Browser page errors were recorded.');
  await publish();
  await browser.close();
  browser = null;
  await waitFor(() => existsSync(output + '.stop'), 'standalone process cleanup', 180);
} catch (error) {
  evidence.errors.push(error.stack ?? error.message);
  // Keep the live sender state before closing pages, including failures during the second viewer's handshake.
  writeFileSync(path.join(path.dirname(output), 'server.json.command'), 'capture-cctv');
  await delay(1000);
  const states = await Promise.race([Promise.allSettled(pages.map(snapshot)), delay(5000).then(() => [])]);
  evidence.latest = states.map(s => s.status === 'fulfilled' ? s.value : { error: String(s.reason) });
  if (pages[0] && !pages[0].isClosed()) {
    try { await pages[0].screenshot({ path: path.join(path.dirname(output), 'browser-failure.png'), timeout: 5000 }); } catch {}
  }
  await publish();
  process.exitCode = 1;
} finally {
  await browser?.close();
  server.kill();
  log.end();
}
