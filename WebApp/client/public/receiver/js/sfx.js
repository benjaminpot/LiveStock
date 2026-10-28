const files = {
  switch: '/SFX/switch_channel.mp3',
  shoot: '/SFX/shoot.mp3',
  overheated: '/SFX/overheated.mp3',
  recovered: '/SFX/overheated_recover.mp3',
  unavailable: '/SFX/cannot_choose.mp3',
};

class SFXPlayer {
  constructor() {
    this.audio = new Map(Object.entries(files).map(([name, src]) => {
      const element = new Audio(src);
      element.preload = 'auto';
      return [name, element];
    }));
    this.unlocked = false;
  }

  // Browsers allow later status-driven sounds after this is called from the
  // user's initial Start Streaming click.
  unlock() {
    if (this.unlocked) return;
    this.unlocked = true;
    for (const audio of this.audio.values()) {
      const volume = audio.volume;
      audio.volume = 0;
      audio.play().then(() => {
        audio.pause();
        audio.currentTime = 0;
        audio.volume = volume;
      }).catch(() => { audio.volume = volume; });
    }
  }

  play(name) {
    const source = this.audio.get(name);
    if (!source) return;
    const audio = source.paused ? source : source.cloneNode();
    audio.currentTime = 0;
    audio.play().catch(() => {});
  }
}

export const sfx = new SFXPlayer();
