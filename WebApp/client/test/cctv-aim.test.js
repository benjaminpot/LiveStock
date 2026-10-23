import { formatFireResult, videoPoint } from '../public/receiver/js/cctv-aim.js';

describe('CCTV aiming', () => {
  test('maps clicks inside a letterboxed video into Unity viewport coordinates', () => {
    const video = {
      videoWidth: 1920,
      videoHeight: 1080,
      getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 1000 }),
    };

    expect(videoPoint(video, 500, 500)).toEqual({ x: 0.5, y: 0.5 });
    expect(videoPoint(video, 500, 100)).toBeNull();
  });

  test('formats authoritative kill feedback with the per-viewer score', () => {
    expect(formatFireResult({ status: 'kill', objectName: 'NPC_sheep', kills: 3 }))
      .toBe('擊殺：NPC_sheep（累計 3）');
    expect(formatFireResult({ status: 'miss' })).toBe('未命中（測試球已清除）');
  });
});
