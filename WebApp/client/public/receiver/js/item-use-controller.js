import { MAX_INT, ITEM_IDS, isShopInt } from './shop-snapshot.js';

const RESULT_STATUSES = new Set([
  'launched', 'hit', 'kill', 'miss', 'no-stock', 'invalid-item', 'invalid-request',
  'unknown-player', 'cooldown', 'stale-request', 'unavailable', 'insufficient-funds', 'round-ended'
]);

export class ItemUseController {
  constructor({ send, getSelectedItemId, onResult, onFeedback, onModeChange, onStopFiring }) {
    this.send = send;
    this.getSelectedItemId = getSelectedItemId;
    this.onResult = onResult;
    this.onFeedback = onFeedback;
    this.onModeChange = onModeChange;
    this.onStopFiring = onStopFiring;
    this.reset();
  }

  setState(state) {
    if (state?.type !== 'item-state' || !isShopInt(state.lastRequestId) ||
        !['normal', 'crazy', 'ended'].includes(state.mode) || state.gunItemId !== 'crazy-gun' ||
        state.shotCost !== 100 || !Number.isFinite(state.fireInterval) || state.fireInterval <= 0) return;
    const rank = { normal: 0, crazy: 1, ended: 2 };
    if (this.modeState && rank[state.mode] < rank[this.modeState.mode]) return;
    this.sequence = Math.max(this.sequence, state.lastRequestId);
    const changed = !this.active || this.modeState.mode !== state.mode ||
      this.modeState.fireInterval !== state.fireInterval;
    this.active = true;
    this.modeState = { mode: state.mode, gunItemId: state.gunItemId,
      shotCost: state.shotCost, fireInterval: state.fireInterval };
    if (changed) {
      this.onStopFiring?.();
      this.onModeChange?.(true, this.modeState);
    }
  }

  setMoneyState(state) {
    if (state?.type !== 'money-state' || !Number.isSafeInteger(state.balance) || state.balance < 0) return;
    this.balance = state.balance;
    if (this.modeState?.mode === 'crazy' && this.balance < this.modeState.shotCost) {
      this.onStopFiring?.();
      this.onFeedback?.('餘額不足，連發已停止。');
    }
  }

  fire(command) {
    if (!this.active) return this.send(command) !== false;
    if (this.modeState.mode === 'ended') return false;
    if (this.modeState.mode === 'crazy' &&
        (this.balance === null || this.balance < this.modeState.shotCost)) {
      this.onStopFiring?.();
      this.onFeedback?.(this.balance === null ? '等待餘額資料，暫時無法發射。' : '餘額不足，連發已停止。');
      return false;
    }
    if (this.current?.phase === 'pending') return false;
    if (this.sequence >= MAX_INT) {
      this.onStopFiring?.();
      this.onFeedback?.('請重新連線後繼續使用道具（請求編號已用完）。');
      return false;
    }
    const itemId = this.modeState.mode === 'crazy' ? this.modeState.gunItemId : this.getSelectedItemId();
    if (this.modeState.mode !== 'crazy' && !ITEM_IDS.includes(itemId)) {
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
      this.onStopFiring?.();
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
    if (['insufficient-funds', 'round-ended'].includes(result.status)) this.onStopFiring?.();
    this.onResult?.(result);
  }

  reset() {
    this.active = false;
    this.modeState = null;
    this.balance = null;
    this.sequence = 0;
    this.current = null;
    this.onStopFiring?.();
    this.onModeChange?.(false, null);
  }
}
