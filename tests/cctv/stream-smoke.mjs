// Integration check: requires the CCTV Unity scene in Play and WebApp running.
// Mobile viewport uses Chromium; physical iPhone Safari remains a manual check.
import assert from 'node:assert/strict';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { mkdir } from 'node:fs/promises';
const { chromium } = process.env.CCTV_PLAYWRIGHT_MODULE
  ? await import(pathToFileURL(process.env.CCTV_PLAYWRIGHT_MODULE).href)
  : await import('playwright');
const receiverUrl = process.env.CCTV_RECEIVER_URL || 'http://127.0.0.1/receiver/index.html';
const browser = await chromium.launch({headless:true,args:['--autoplay-policy=no-user-gesture-required']});
const pages = [];
try {
  for (const mobile of [false, true]) {
    const context = await browser.newContext(mobile
      ? {viewport:{width:390,height:844},isMobile:true,hasTouch:true}
      : {viewport:{width:1280,height:900}});
    await context.addInitScript(() => {
      window.livePeers = [];
      window.liveStates = [];
      window.liveNegotiations = 0;
      window.RTCPeerConnection = new Proxy(window.RTCPeerConnection, {
        construct(Target, args) {
          const peer = new Target(...args);
          window.livePeers.push(peer);
          peer.addEventListener('signalingstatechange',()=>window.liveNegotiations++);
          const create = peer.createDataChannel.bind(peer);
          peer.createDataChannel = (...channelArgs) => {
            const channel = create(...channelArgs);
            if (channel.label === 'cctv') {
              window.liveChannel = channel;
              channel.addEventListener('message', event => {
              const state = JSON.parse(event.data);
              window.liveState = state;
              window.liveStates.push({time:performance.now(),state});
              });
            }
            return channel;
          };
          return peer;
        }
      });
    });
    const page = await context.newPage();
    pages.push(page);
    page.on('pageerror', e => console.log('PAGE_ERROR',e.message));
    await page.goto(receiverUrl,{waitUntil:'domcontentloaded'});
    await page.locator('#playButton').click();
    await page.waitForFunction(() => window.liveState?.monitors?.length === 2 && window.liveState.selectedIndices?.[1] >= 0, null, {timeout:20000});
    console.log('CONNECTED',mobile?'mobile-size':'desktop',JSON.stringify(await page.evaluate(()=>window.liveState)));
  }
  const [pc,phone]=pages;
  const snapshot = page => page.evaluate(()=>window.liveState);
  const stats = page => page.evaluate(async()=>Array.from((await window.livePeers[0].getStats()).values())
    .filter(s=>s.type==='inbound-rtp')
    .map(s=>({id:s.id,kind:s.kind,bytes:s.bytesReceived,frames:s.framesDecoded})));
  const negotiations=await Promise.all(pages.map(page=>page.evaluate(()=>window.liveNegotiations)));
  await mkdir(new URL('../../Library/CCTVReview/',import.meta.url),{recursive:true});
  for(let i=0;i<pages.length;i++) {
    const page=pages[i];
    assert.deepEqual((await snapshot(page)).selectedIndices,[0,1],'two different cameras appear automatically');
    assert.equal(await page.locator('[data-secondary-toggle]').count(),0,'no second-view switch');
    await page.waitForFunction(()=>['Video','secondaryVideo'].every(id=>{
      const v=document.getElementById(id);
      return v.videoWidth>0 && v.readyState>=2 && v.currentTime>0;
    }),null,{timeout:10000});
    const media=await page.evaluate(()=>({
      primaryAudio:document.getElementById('Video').srcObject.getAudioTracks().length,
      secondaryAudio:document.getElementById('secondaryVideo').srcObject.getAudioTracks().length,
      ids:['Video','secondaryVideo'].map(id=>document.getElementById(id).srcObject.getVideoTracks()[0].id),
      expected:window.liveState.videoMids.map(mid=>window.livePeers[0].getTransceivers().find(t=>t.mid===mid).receiver.track.id)
    }));
    assert.equal(media.primaryAudio,1);
    assert.equal(media.secondaryAudio,0);
    assert.deepEqual(media.ids,media.expected,'videos route by MID');
    const pixels=await page.evaluate(()=>['Video','secondaryVideo'].map(id=>{
      const canvas=document.createElement('canvas');canvas.width=32;canvas.height=24;
      const ctx=canvas.getContext('2d');ctx.drawImage(document.getElementById(id),0,0,32,24);
      const data=ctx.getImageData(0,0,32,24).data;
      let nonBlack=0;
      for(let n=0;n<data.length;n+=4) if(data[n]+data[n+1]+data[n+2]>30) nonBlack++;
      return nonBlack;
    }));
    assert.ok(pixels.every(count=>count>100),'both videos contain actual scene pixels');
    await page.screenshot({path:fileURLToPath(new URL('../../Library/CCTVReview/live-dual-'+i+'.png',import.meta.url)),fullPage:true});
    console.log('DUAL_MEDIA',i,JSON.stringify(media));
  }
  await pc.waitForFunction(()=>window.liveState.monitors.every(m=>m.viewers===2),null,{timeout:3000});
  await phone.locator('[data-monitors] button').nth(1).tap();
  await phone.waitForFunction(()=>window.liveState.selectedIndex===1 && window.liveState.cooldownRemaining>0);
  assert.deepEqual((await snapshot(pc)).selectedIndices,[0,1],'phone leaves PC unchanged');
  assert.equal((await snapshot(pc)).cooldownRemaining,0,'phone does not cool down PC');
  await phone.evaluate(()=>{
    window.liveChannel.send(JSON.stringify({type:'cctv-select',slot:2,index:0}));
    window.liveChannel.send(JSON.stringify({type:'cctv-select',slot:1,index:1}));
  });
  await phone.waitForFunction(()=>window.liveState.cooldownRemaining<2.5);
  assert.deepEqual((await snapshot(phone)).selectedIndices,[1,0],'third slot and cooldown bypass rejected');
  await pc.locator('#secondaryVideo').click();
  await pc.waitForFunction(()=>window.liveState.selectedIndex===1 && window.liveState.cooldownRemaining>0);
  console.log('PASS independent selections, max two slots and shared per-player cooldown');
  await phone.waitForFunction(()=>window.liveState.cooldownRemaining===0);
  await phone.locator('#Video').tap();
  await phone.waitForFunction(()=>window.liveState.selectedIndex===0);
  assert.deepEqual((await snapshot(pc)).selectedIndices,[1,0]);
  await pc.waitForFunction(()=>window.liveState.cooldownRemaining===0);
  await pc.locator('[data-slot]').selectOption('1');
  await pc.locator('[data-monitors] button').nth(1).click();
  await pc.waitForFunction(()=>window.liveState.selectedIndex===0);
  for(let i=0;i<pages.length;i++) assert.equal(await pages[i].evaluate(()=>window.liveNegotiations),negotiations[i]);
  console.log('PASS primary binary input, secondary click, slot selection and no renegotiation');
  await pc.waitForFunction(()=>window.liveState.monitors.every(m=>m.status==='overheated'),null,{timeout:20000});
  await phone.waitForFunction(()=>window.liveState.monitors.every(m=>m.status==='overheated'),null,{timeout:3000});
  for(const page of pages) {
    assert.equal(await page.locator('#cctvBlackout').isVisible(),true);
    assert.equal(await page.locator('#secondaryBlackout').isVisible(),true);
  }
  console.log('PASS both cameras share heat across viewers',JSON.stringify(await snapshot(pc)));
  await pc.waitForFunction(()=>window.liveState.monitors.every(m=>m.status!=='overheated'),null,{timeout:15000});
  await phone.waitForFunction(()=>window.liveState.monitors.every(m=>m.status!=='overheated'),null,{timeout:3000});
  assert.equal(await pc.locator('#cctvBlackout').isVisible(),false);
  assert.equal(await pc.locator('#secondaryBlackout').isVisible(),false);
  const before=await stats(pc);
  await phone.close();
  await pc.waitForFunction(()=>window.liveState.monitors.every(m=>m.viewers===1),null,{timeout:10000});
  const time=await pc.locator('#Video').evaluate(v=>v.currentTime);
  await pc.waitForFunction(time=>document.getElementById('Video').currentTime>time+0.5,time,{timeout:5000});
  const after=await stats(pc);
  for(const report of before) {
    const next=after.find(s=>s.id===report.id);
    assert.ok(next.bytes>report.bytes,'audio and both videos continue after other viewer disconnects');
    if(report.kind==='video') assert.ok(next.frames>report.frames);
  }
  console.log('PASS automatic recovery and disconnect releases both counts without interrupting other viewer');
} catch(error) {
  for(let i=0;i<pages.length;i++) if(!pages[i].isClosed())
    console.log('DIAGNOSTIC',i,JSON.stringify(await pages[i].evaluate(()=>({panel:document.querySelector('#cctvPanel')?.innerText,states:window.liveStates?.slice(-3),peers:window.livePeers?.map(p=>({connection:p.connectionState,ice:p.iceConnectionState,signaling:p.signalingState}))}))));
  throw error;
} finally { await browser.close(); }
