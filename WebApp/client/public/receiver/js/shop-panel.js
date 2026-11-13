import { MAX_INT, ITEM_IDS, isShopInt, readShopSnapshot } from './shop-snapshot.js';
const RESULT_MESSAGES = {
  success: '購買成功；庫存以伺服器資料為準。',
  'insufficient-funds': '餘額不足，未購買。',
  'invalid-item': '商品無效，未購買。',
  'invalid-request': '購買請求無效，請重新購買。',
  'unknown-player': '玩家尚未就緒，請重新連線。',
  'inventory-full': '庫存已滿，未購買。',
  'stale-request': '購買請求已過期，請重新購買。',
  unavailable: '此場景未開放商店。'
};
const format = value => new Intl.NumberFormat().format(value);

export class ShopPanel {
  constructor(root, onPurchase) {
    this.root = root;
    this.onPurchase = onPurchase;
    this.status = root.querySelector('[data-shop-status]');
    this.cards = ITEM_IDS.map(id => {
      const card = root.querySelector(`[data-shop-item="${id}"]`);
      const button = card.querySelector('[data-shop-buy]');
      button.addEventListener('click', () => this.buy(id));
      return { id, button, name: card.querySelector('[data-shop-name]'),
        price: card.querySelector('[data-shop-price]'), quantity: card.querySelector('[data-shop-quantity]') };
    });
    this.reset();
  }

  setState(state) {
    const items = readShopSnapshot(state, this.lastRequestId);
    if (!items) return;
    this.lastRequestId = state.lastRequestId;
    this.sequence = Math.max(this.sequence, state.lastRequestId);
    this.available = true;
    for (const card of this.cards) {
      const item = items.get(card.id);
      card.name.textContent = item.name;
      card.price.textContent = format(item.price);
      card.quantity.textContent = format(item.quantity);
    }
    if (!this.pending) this.status.textContent = '商店已就緒；每次購買一件。';
    this.renderButtons();
  }

  buy(itemId) {
    if (!this.available || this.pending || this.sequence >= MAX_INT) return;
    const requestId = ++this.sequence;
    this.pending = { requestId, itemId };
    this.status.textContent = '購買處理中，請稍候…';
    this.renderButtons();
    this.onPurchase({ type: 'shop-buy', itemId, requestId });
  }

  showResult(result) {
    if (result?.type !== 'shop-result' || !isShopInt(result.requestId) || result.requestId === 0 ||
        typeof result.status !== 'string' || !Object.hasOwn(RESULT_MESSAGES, result.status) || !this.pending ||
        result.requestId !== this.pending.requestId || result.itemId !== this.pending.itemId) return;
    this.pending = null;
    this.status.textContent = RESULT_MESSAGES[result.status];
    this.renderButtons();
  }

  renderButtons() {
    for (const card of this.cards) {
      card.button.disabled = !this.available || !!this.pending || this.sequence >= MAX_INT;
      card.button.textContent = !this.available ? 'COMING SOON' : this.pending ? '處理中…' : '購買 1 件';
    }
    if (this.available && !this.pending && this.sequence >= MAX_INT) {
      this.status.textContent += ' 請重新連線後繼續購買（請求編號已用完）。';
    }
  }

  reset() {
    this.available = false;
    this.pending = null;
    this.lastRequestId = 0;
    this.sequence = 0;
    for (const card of this.cards) {
      card.name.textContent = '—';
      card.price.textContent = '—';
      card.quantity.textContent = '—';
    }
    this.status.textContent = '商店尚未開放，等待伺服器商品資料。';
    this.renderButtons();
  }
}
