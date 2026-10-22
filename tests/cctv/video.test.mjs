import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CCTVVideo } from '../../WebApp/client/public/receiver/js/cctv-video.js';

class Stream {
  constructor(tracks = []) { this.tracks = [...tracks]; }
  getVideoTracks() { return this.tracks.filter(track => track.kind === 'video'); }
  addTrack(track) { this.tracks.push(track); }
  removeTrack(track) { this.tracks = this.tracks.filter(item => item !== track); }
}
globalThis.MediaStream = Stream;

test('out-of-order video tracks route by mid and audio is attached only once', () => {
  const primary = { videoElement: {srcObject:new Stream()},
    addTrack(track) { this.videoElement.srcObject.addTrack(track); } };
  const secondary = {srcObject:null,play:async()=>{},pause:()=>{}};
  const video = new CCTVVideo(primary,secondary);
  const first = {kind:'video',id:'first'};
  const second = {kind:'video',id:'second'};
  const audio = {kind:'audio',id:'audio'};
  video.addTrack({track:second,transceiver:{mid:'v2'}});
  video.setState({selectedIndices:[0,1],videoMids:['v1','v2']});
  video.addTrack({track:first,transceiver:{mid:'v1'}});
  video.addTrack({track:audio});
  video.addTrack({track:audio});
  assert.deepEqual(primary.videoElement.srcObject.tracks,[first,audio]);
  assert.deepEqual(secondary.srcObject.tracks,[second]);
  video.setState({selectedIndices:[0,-1],videoMids:['v1','v2']});
  assert.equal(secondary.srcObject,null);
  assert.deepEqual(primary.videoElement.srcObject.tracks,[first,audio]);
  video.reset();
  assert.equal(video.tracks.size,0);
});
