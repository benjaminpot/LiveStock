import { getServerConfig, getRTCConfiguration } from "../../js/config.js";
import { createDisplayStringArray } from "../../js/stats.js";
import { VideoPlayer } from "../../js/videoplayer.js";
import { RenderStreaming } from "../../module/renderstreaming.js";
import { Signaling, WebSocketSignaling } from "../../module/signaling.js";
import { CCTVPanel } from "./cctv-panel.js";
import { CCTVVideo } from "./cctv-video.js";

/** @type {Element} */
let playButton;
/** @type {RenderStreaming} */
let renderstreaming;
/** @type {boolean} */
let useWebSocket;

const codecPreferences = document.getElementById('codecPreferences');
const supportsSetCodecPreferences = window.RTCRtpTransceiver &&
  'setCodecPreferences' in window.RTCRtpTransceiver.prototype;
const messageDiv = document.getElementById('message');
messageDiv.style.display = 'none';

const playerDiv = document.getElementById('player');
const lockMouseCheck = document.getElementById('lockMouseCheck');
const videoPlayer = new VideoPlayer();
const cctvPanel = new CCTVPanel(document.getElementById('cctvPanel'), document.getElementById('cctvBlackout'));
const cctvVideo = new CCTVVideo(videoPlayer, document.getElementById('secondaryVideo'));
cctvPanel.onState = state => {
  cctvVideo.setState(state);
  videoPlayer.resizeVideo();
};
document.getElementById('secondaryVideo').addEventListener('click', () =>
  cctvPanel.send({ type: 'cctv-next', slot: 1 }));

setup();

window.document.oncontextmenu = function () {
  return false;     // cancel default menu
};

window.addEventListener('resize', function () {
  videoPlayer.resizeVideo();
}, true);

window.addEventListener('beforeunload', async () => {
  if(!renderstreaming)
    return;
  await renderstreaming.stop();
}, true);

async function setup() {
  const res = await getServerConfig();
  useWebSocket = res.useWebSocket;
  showWarningIfNeeded(res.startupMode);
  showCodecSelect();
  showPlayButton();
}

function showWarningIfNeeded(startupMode) {
  const warningDiv = document.getElementById("warning");
  if (startupMode == "private") {
    warningDiv.innerHTML = "<h4>Warning</h4> This sample is not working on Private Mode.";
    warningDiv.hidden = false;
  }
}

function showPlayButton() {
  if (!document.getElementById('playButton')) {
    const elementPlayButton = document.createElement('img');
    elementPlayButton.id = 'playButton';
    elementPlayButton.src = '../../images/Play.png';
    elementPlayButton.alt = 'Start Streaming';
    playButton = document.getElementById('player').appendChild(elementPlayButton);
    playButton.addEventListener('click', onClickPlayButton);
  }
}

function onClickPlayButton() {
  playButton.style.display = 'none';
  cctvPanel.setConnection('連線中');

  // add video player
  videoPlayer.createPlayer(playerDiv, lockMouseCheck);
  setupRenderStreaming();
}

async function setupRenderStreaming() {
  codecPreferences.disabled = true;

  const signaling = useWebSocket ? new WebSocketSignaling() : new Signaling();
  const config = getRTCConfiguration();
  renderstreaming = new RenderStreaming(signaling, config);
  renderstreaming.onConnect = onConnect;
  renderstreaming.onDisconnect = onDisconnect;
  renderstreaming.onTrackEvent = (data) => cctvVideo.addTrack(data);
  renderstreaming.onGotOffer = setCodecPreferences;

  await renderstreaming.start();
  await renderstreaming.createConnection();
}

function onConnect() {
  const channel = renderstreaming.createDataChannel("input");
  videoPlayer.setupInput(channel);
  const connection = renderstreaming;
  // Unity routes input and game commands by this connection's identity.
  // Keep input ready before starting the status/command channel.
  channel.addEventListener('open', () => {
    if (renderstreaming === connection)
      cctvPanel.attach(connection.createDataChannel('cctv'));
  }, { once: true });
  showStatsMessage();
}

async function onDisconnect(connectionId) {
  cctvPanel.reset();
  cctvVideo.reset();
  clearStatsMessage();
  messageDiv.style.display = 'block';
  messageDiv.innerText = `Disconnect peer on ${connectionId}.`;

  await renderstreaming.stop();
  renderstreaming = null;
  videoPlayer.deletePlayer();
  if (supportsSetCodecPreferences) {
    codecPreferences.disabled = false;
  }
  showPlayButton();
}

function setCodecPreferences() {
  /** @type {RTCRtpCodecCapability[] | null} */
  let selectedCodecs = null;
  if (supportsSetCodecPreferences) {
    const preferredCodec = codecPreferences.options[codecPreferences.selectedIndex];
    if (preferredCodec.value !== '') {
      const [mimeType, sdpFmtpLine] = preferredCodec.value.split(' ');
      const { codecs } = RTCRtpSender.getCapabilities('video');
      const selectedCodecIndex = codecs.findIndex(c => c.mimeType === mimeType && c.sdpFmtpLine === sdpFmtpLine);
      const selectCodec = codecs[selectedCodecIndex];
      selectedCodecs = [selectCodec];
    }
  }

  if (selectedCodecs == null) {
    return;
  }
  const transceivers = renderstreaming.getTransceivers().filter(t => t.receiver.track.kind == "video");
  if (transceivers && transceivers.length > 0) {
    transceivers.forEach(t => t.setCodecPreferences(selectedCodecs));
  }
}

function showCodecSelect() {
  if (!supportsSetCodecPreferences) {
    messageDiv.style.display = 'block';
    messageDiv.innerHTML = `Current Browser does not support <a href="https://developer.mozilla.org/en-US/docs/Web/API/RTCRtpTransceiver/setCodecPreferences">RTCRtpTransceiver.setCodecPreferences</a>.`;
    return;
  }

  const codecs = RTCRtpSender.getCapabilities('video').codecs;
  codecs.forEach(codec => {
    if (['video/red', 'video/ulpfec', 'video/rtx'].includes(codec.mimeType)) {
      return;
    }
    const option = document.createElement('option');
    option.value = (codec.mimeType + ' ' + (codec.sdpFmtpLine || '')).trim();
    option.innerText = option.value;
    codecPreferences.appendChild(option);
  });
  codecPreferences.disabled = false;
}

/** @type {RTCStatsReport} */
let lastStats;
/** @type {number} */
let intervalId;

function showStatsMessage() {
  intervalId = setInterval(async () => {
    if (renderstreaming == null) {
      return;
    }

    const stats = await renderstreaming.getStats();
    if (stats == null) {
      return;
    }

    const array = createDisplayStringArray(stats, lastStats);
    if (array.length) {
      messageDiv.style.display = 'block';
      messageDiv.innerHTML = array.join('<br>');
    }
    lastStats = stats;
  }, 1000);
}

function clearStatsMessage() {
  if (intervalId) {
    clearInterval(intervalId);
  }
  lastStats = null;
  intervalId = null;
  messageDiv.style.display = 'none';
  messageDiv.innerHTML = '';
}
