using System;
using UnityEngine;

namespace LiveStock.Economy
{
    [DisallowMultipleComponent]
    public sealed class ShopManager : MonoBehaviour
    {
        [Serializable]
        private sealed class ProductSettings
        {
            public string id;
            public string displayName;
            [Min(1)] public int price;
            public ProductSettings(string id, string name, int price)
            { this.id = id; displayName = name; this.price = price; }
        }

        [SerializeField] private MoneyManager moneyManager;
        [SerializeField] private ProductSettings[] products = {
            new ProductSettings("capture-ball", "寶貝球", 100),
            new ProductSettings("bow", "弓箭", 100),
            new ProductSettings("sniper", "狙擊槍", 599),
            new ProductSettings("bomb", "炸彈", 599)
        };
        private ShopState state;

        public int ProductCount => state == null ? 0 : state.ProductCount;
        public ShopState.Product GetProduct(int index) => state.GetProduct(index);

        private void Awake()
        {
            if (moneyManager == null)
            {
                Debug.LogError("[ShopManager] Assign the scene's MoneyManager before starting.", this);
                enabled = false;
                return;
            }
            try
            {
                if (products == null) throw new ArgumentException("Products are missing.");
                var definitions = new ShopState.Product[products.Length];
                for (int i = 0; i < products.Length; i++)
                {
                    var product = products[i] ?? throw new ArgumentException("Product is missing.");
                    definitions[i] = new ShopState.Product(product.id, product.displayName, product.price);
                }
                state = new ShopState(definitions, moneyManager.TrySpendMoney);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError("[ShopManager] Invalid catalog: " + exception.Message, this);
                enabled = false;
            }
        }

        public bool IsConfiguredFor(MoneyManager wallet) => isActiveAndEnabled && state != null &&
            moneyManager != null && moneyManager.isActiveAndEnabled && moneyManager == wallet;

        public bool RegisterPlayer(string playerId) => IsConfiguredFor(moneyManager) &&
            moneyManager.TryGetBalance(playerId, out _) && state.RegisterPlayer(playerId);
        public bool RemovePlayer(string playerId) => state != null && state.RemovePlayer(playerId);

        public bool TryGetQuantity(string playerId, string itemId, out int quantity)
        {
            quantity = 0;
            return state != null && state.TryGetQuantity(playerId, itemId, out quantity);
        }

        public bool TryConsumeItem(string playerId, string itemId) => IsConfiguredFor(moneyManager) &&
            moneyManager.TryGetBalance(playerId, out _) && state.TryConsumeItem(playerId, itemId);

        public bool TryGetLastRequestId(string playerId, out int requestId)
        {
            requestId = 0;
            return state != null && state.TryGetLastRequestId(playerId, out requestId);
        }

        public ShopState.PurchaseResult Purchase(string playerId, string itemId, int requestId)
        {
            if (!IsConfiguredFor(moneyManager))
                return new ShopState.PurchaseResult(requestId, itemId, ShopState.PurchaseStatus.Unavailable);
            if (!moneyManager.TryGetBalance(playerId, out _))
                return new ShopState.PurchaseResult(requestId, itemId, ShopState.PurchaseStatus.UnknownPlayer);
            return state.Purchase(playerId, itemId, requestId);
        }

        public void Clear() => state?.Clear();
        private void OnDestroy() => Clear();
    }
}
