using System;
using System.Collections.Generic;

namespace LiveStock.Economy
{
    // Purchase and inventory rules; no Unity, transport, or browser dependency.
    public sealed class ShopState
    {
        public sealed class Product
        {
            public string Id { get; }
            public string Name { get; }
            public int Price { get; }

            public Product(string id, string name, int price)
            {
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException(nameof(id));
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
                if (price <= 0) throw new ArgumentOutOfRangeException(nameof(price));
                Id = id;
                Name = name;
                Price = price;
            }
        }

        public enum PurchaseStatus
        {
            Success, InsufficientFunds, InvalidItem, InvalidRequest, UnknownPlayer,
            InventoryFull, StaleRequest, Unavailable
        }

        public readonly struct PurchaseResult
        {
            public readonly int RequestId;
            public readonly string ItemId;
            public readonly PurchaseStatus Status;

            public PurchaseResult(int requestId, string itemId, PurchaseStatus status)
            {
                RequestId = requestId;
                ItemId = itemId;
                Status = status;
            }
        }

        private sealed class Inventory
        {
            public readonly int[] quantities;
            public int lastRequestId;
            public string lastItemId;
            public PurchaseStatus lastStatus;
            public bool purchasing;
            public Inventory(int count) { quantities = new int[count]; }
        }

        private readonly Product[] products;
        private readonly Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, Inventory> inventories = new Dictionary<string, Inventory>();
        private readonly Func<string, int, bool> trySpendMoney;

        public int ProductCount => products.Length;
        public Product GetProduct(int index) => products[index];

        public ShopState(IReadOnlyList<Product> products, Func<string, int, bool> trySpendMoney)
        {
            if (products == null || products.Count == 0) throw new ArgumentException(nameof(products));
            this.trySpendMoney = trySpendMoney ?? throw new ArgumentNullException(nameof(trySpendMoney));
            this.products = new Product[products.Count];
            for (int i = 0; i < products.Count; i++)
            {
                var product = products[i] ?? throw new ArgumentException(nameof(products));
                if (indices.ContainsKey(product.Id)) throw new ArgumentException("Duplicate product ID.", nameof(products));
                indices.Add(product.Id, i);
                this.products[i] = product;
            }
        }

        public bool RegisterPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || inventories.ContainsKey(playerId)) return false;
            inventories.Add(playerId, new Inventory(products.Length));
            return true;
        }

        public bool RemovePlayer(string playerId) =>
            !string.IsNullOrWhiteSpace(playerId) && inventories.Remove(playerId);

        public bool TryGetQuantity(string playerId, string itemId, out int quantity)
        {
            quantity = 0;
            if (!TryGetInventory(playerId, out var inventory) || itemId == null ||
                !indices.TryGetValue(itemId, out int index)) return false;
            quantity = inventory.quantities[index];
            return true;
        }

        public bool TryConsumeItem(string playerId, string itemId)
        {
            if (!TryGetInventory(playerId, out var inventory) || inventory.purchasing ||
                itemId == null || !indices.TryGetValue(itemId, out int index) ||
                inventory.quantities[index] <= 0) return false;
            inventory.quantities[index]--;
            return true;
        }

        public bool TryGetLastRequestId(string playerId, out int requestId)
        {
            requestId = 0;
            if (!TryGetInventory(playerId, out var inventory)) return false;
            requestId = inventory.lastRequestId;
            return true;
        }

        public PurchaseResult Purchase(string playerId, string itemId, int requestId)
        {
            if (!TryGetInventory(playerId, out var inventory))
                return new PurchaseResult(requestId, itemId, PurchaseStatus.UnknownPlayer);
            if (requestId <= 0)
                return new PurchaseResult(requestId, itemId, PurchaseStatus.InvalidRequest);
            if (inventory.purchasing)
                return new PurchaseResult(requestId, itemId, PurchaseStatus.Unavailable);
            if (requestId == inventory.lastRequestId)
                return new PurchaseResult(requestId, itemId, itemId == inventory.lastItemId
                    ? inventory.lastStatus : PurchaseStatus.InvalidRequest);
            if (requestId < inventory.lastRequestId)
                return new PurchaseResult(requestId, itemId, PurchaseStatus.StaleRequest);

            // One bounded replay record per connection. Older requests never charge again.
            inventory.lastRequestId = requestId;
            inventory.lastItemId = itemId;
            if (itemId == null || !indices.TryGetValue(itemId, out int index))
                inventory.lastStatus = PurchaseStatus.InvalidItem;
            else if (inventory.quantities[index] == int.MaxValue)
                inventory.lastStatus = PurchaseStatus.InventoryFull;
            else
            {
                // Stage the count before spending so successful balance observers see
                // the committed inventory. A rejected spend has no balance event.
                inventory.quantities[index]++;
                inventory.lastStatus = PurchaseStatus.Success;
                inventory.purchasing = true;
                try
                {
                    if (!trySpendMoney(playerId, products[index].Price))
                    {
                        inventory.quantities[index]--;
                        inventory.lastStatus = PurchaseStatus.InsufficientFunds;
                    }
                }
                finally { inventory.purchasing = false; }
            }
            return new PurchaseResult(requestId, itemId, inventory.lastStatus);
        }

        public void Clear() => inventories.Clear();

        private bool TryGetInventory(string playerId, out Inventory inventory)
        {
            inventory = null;
            return !string.IsNullOrWhiteSpace(playerId) && inventories.TryGetValue(playerId, out inventory);
        }
    }
}
