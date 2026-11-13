export const MAX_INT = 2147483647;
export const ITEM_IDS = Object.freeze(['capture-ball', 'bow', 'sniper', 'bomb']);
export const isShopInt = value => Number.isInteger(value) && value >= 0 && value <= MAX_INT;

// Both views consume the same authoritative catalog/inventory contract.
export function readShopSnapshot(state, lastRequestId = 0) {
  if (state?.type !== 'shop-state' || !isShopInt(state.lastRequestId) ||
      state.lastRequestId < lastRequestId || !Array.isArray(state.items) ||
      state.items.length !== ITEM_IDS.length) return null;
  const items = new Map();
  for (const item of state.items) {
    if (!item || !ITEM_IDS.includes(item.id) || items.has(item.id) ||
        typeof item.name !== 'string' || !item.name.trim() ||
        !isShopInt(item.price) || !isShopInt(item.quantity)) return null;
    items.set(item.id, item);
  }
  return items;
}
