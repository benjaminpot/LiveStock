import { ITEM_IDS, readShopSnapshot } from './shop-snapshot.js';

export class InventoryPanel {
  constructor(root) {
    this.root = root;
    this.status = root.querySelector('[data-inventory-status]');
    this.empty = root.querySelector('[data-inventory-empty]');
    this.clear = root.querySelector('[data-inventory-clear]');
    this.formatter = new Intl.NumberFormat();
    this.slots = ITEM_IDS.map(id => {
      const button = root.querySelector(`[data-inventory-item="${id}"]`);
      button.addEventListener('click', () => this.select(id));
      return { id, button, name: button.querySelector('[data-inventory-name]'),
        quantity: button.querySelector('[data-inventory-quantity]') };
    });
    this.clear.addEventListener('click', () => this.clearSelection());
    this.reset();
  }

  get selectedItemId() {
    return this.mode === 'crazy' ? this.gunItemId : this.mode === 'ended' ? null : this.selection;
  }

  setState(state) {
    const items = readShopSnapshot(state, this.lastRequestId);
    if (!items) return;
    this.items = items;
    this.lastRequestId = state.lastRequestId;
    if (!items.get(this.selection)?.quantity) this.selection = null;
    this.root.hidden = this.mode !== 'normal';
    this.render();
  }

  setMode(state) {
    this.mode = state?.mode ?? 'normal';
    this.gunItemId = state?.gunItemId ?? null;
    this.root.hidden = this.mode !== 'normal' || !this.items;
    this.render();
  }

  select(itemId) {
    if (this.mode !== 'normal' || !this.items?.get(itemId)?.quantity) return;
    this.selection = itemId;
    this.render();
  }

  clearSelection() {
    if (this.mode !== 'normal') return;
    this.selection = null;
    this.render();
  }

  render() {
    let hasItems = false;
    for (const slot of this.slots) {
      const item = this.items?.get(slot.id);
      const owned = !!item?.quantity;
      slot.name.textContent = item?.name ?? '—';
      hasItems = hasItems || owned;
      slot.quantity.textContent = item ? this.formatter.format(item.quantity) : '—';
      slot.button.disabled = this.mode !== 'normal' || !owned;
      slot.button.classList.toggle('is-empty', !owned);
      slot.button.setAttribute('aria-pressed', String(slot.id === this.selection));
    }
    const selected = this.items?.get(this.selection);
    this.status.textContent = selected
      ? `已選擇：${selected.name}（持有 ${this.formatter.format(selected.quantity)}）`
      : this.items ? '未選擇物品' : '等待伺服器物品資料';
    this.empty.hidden = !this.items || hasItems;
    this.clear.disabled = this.mode !== 'normal' || !selected;
  }

  reset() {
    this.mode = 'normal';
    this.gunItemId = null;
    this.items = null;
    this.selection = null;
    this.lastRequestId = 0;
    this.root.hidden = true;
    this.render();
  }
}
