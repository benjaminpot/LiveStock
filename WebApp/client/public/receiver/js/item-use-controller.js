import { MAX_INT, ITEM_IDS, isShopInt } from './shop-snapshot.js';

const RESULT_STATUSES = new Set([
  'launched', 'hit', 'kill', 'miss', 'no-stock', 'invalid-item', 'invalid-request',
  'unknown-player', 'cooldown', 'stale-request', 'unavailable'
]);

export class ItemUseController {
  constructor({ send, getSelectedItemId, onResult, onFeedback, onModeChange }) {
    this.send = send;
    this.getSelectedItemId = getSelectedItemId;
    this.onResult = onResult;
    this.onFeedback = onFeedback;
    this.onModeChange = onModeChange;
    this.reset();
  }

  setState(state) {
    if (state?.type !== 'item-state' || !isShopInt(state.lastRequestId)) return;
    this.sequence = Math.max(this.sequence, state.lastRequestId);
    if (!this.active) {
      this.active = true;
      this.onModeChange?.(true);
    }
  }

  fire(command) {
    if (!this.active) return this.send(command) !== false;
    if (this.current?.phase === 'pending') return false;
    if (this.sequence >= MAX_INT) {
      this.onFeedback?.('請重新連線後繼續使用道具（請求編號已用完）。');
      return false;
    }
    const itemId = this.getSelectedItemId();
    if (!ITEM_IDS.includes(itemId)) {
      this.onFeedback?.('請先選取持有的道具。');
      return false;
    }
    const requestId = ++this.sequence;
    const { slot, index, x, y } = command;
    const previous = this.current;
    this.current = { requestId, itemId, slot, index, phase: 'pending' };
    if (this.send({ type: 'item-use', requestId, itemId, slot, index, x, y }) === false) {
      this.current = previous;
      this.onFeedback?.('連線尚未就緒，未使用道具。');
      return false;
    }
    return true;
  }

  showResult(result) {
    const shot = this.current;
    if (!this.active || result?.type !== 'item-result' || !shot ||
        result.requestId !== shot.requestId || result.itemId !== shot.itemId ||
        result.slot !== shot.slot || result.index !== shot.index ||
        !RESULT_STATUSES.has(result.status) || shot.phase === 'terminal') return;
    if (result.status === 'launched') {
      if (shot.phase !== 'pending') return;
      shot.phase = 'launched';
    } else {
      shot.phase = 'terminal';
    }
    this.onResult?.(result);
  }

  reset() {
    this.active = false;
    this.sequence = 0;
    this.current = null;
    this.onModeChange?.(false);
  }
}
