using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace LiveStock.Economy.Tests
{
    public sealed class ShopStateTests
    {
        private MoneyState money;
        private ShopState shop;
        private int balanceEvents;
        private static readonly ShopState.Product[] Products = {
            new ShopState.Product("capture-ball", "寶貝球", 100),
            new ShopState.Product("bow", "弓箭", 100),
            new ShopState.Product("sniper", "狙擊槍", 599),
            new ShopState.Product("bomb", "炸彈", 599)
        };

        [SetUp]
        public void SetUp()
        {
            money = new MoneyState(0, 10000);
            shop = new ShopState(Products, money.TrySpendMoney);
            money.BalanceChanged += (id, value) => balanceEvents++;
            Register("A");
        }

        private void Register(string id) { money.RegisterPlayer(id); shop.RegisterPlayer(id); }
        private int Balance(string id = "A")
        {
            Assert.That(money.TryGetBalance(id, out int value), Is.True);
            return value;
        }
        private int Quantity(string item = "capture-ball", string id = "A")
        {
            Assert.That(shop.TryGetQuantity(id, item, out int value), Is.True);
            return value;
        }

        [TestCase("capture-ball", 100)]
        [TestCase("bow", 100)]
        [TestCase("sniper", 599)]
        [TestCase("bomb", 599)]
        public void EachProductChargesExactlyItsServerPriceAndGrantsOne(string item, int price)
        {
            money.AddMoney("A", price);
            Assert.That(shop.Purchase("A", item, 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(Balance(), Is.Zero);
            foreach (var product in Products)
                Assert.That(Quantity(product.Id), Is.EqualTo(product.Id == item ? 1 : 0));
        }

        [Test]
        public void InsufficientPurchaseChangesNeitherBalanceNorInventoryAndDoesNotLaterReplay()
        {
            money.AddMoney("A", 99);
            balanceEvents = 0;
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.InsufficientFunds));
            Assert.That(Balance(), Is.EqualTo(99));
            Assert.That(Quantity(), Is.Zero);
            Assert.That(balanceEvents, Is.Zero);
            money.AddMoney("A", 1);
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.InsufficientFunds));
            Assert.That(Balance(), Is.EqualTo(100));
            Assert.That(Quantity(), Is.Zero);
            Assert.That(shop.Purchase("A", "capture-ball", 2).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(Balance(), Is.Zero);
            Assert.That(Quantity(), Is.EqualTo(1));
        }

        [Test]
        public void DuplicateAndOldRequestsNeverChargeOrGrantTwice()
        {
            money.AddMoney("A", 500);
            shop.Purchase("A", "capture-ball", 1);
            balanceEvents = 0;
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(shop.Purchase("A", "bow", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.InvalidRequest));
            Assert.That(Balance(), Is.EqualTo(400));
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity("bow"), Is.Zero);
            Assert.That(balanceEvents, Is.Zero);
            shop.Purchase("A", "bow", 2);
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.StaleRequest));
            Assert.That(Balance(), Is.EqualTo(300));
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity("bow"), Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("missing")]
        public void UnknownProductCannotDebitOrGrant(string item)
        {
            money.AddMoney("A", 1000);
            balanceEvents = 0;
            Assert.That(shop.Purchase("A", item, 1).Status, Is.EqualTo(ShopState.PurchaseStatus.InvalidItem));
            Assert.That(Balance(), Is.EqualTo(1000));
            foreach (var product in Products) Assert.That(Quantity(product.Id), Is.Zero);
            Assert.That(balanceEvents, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("unknown")]
        public void InvalidOrUnknownPlayersCannotPurchaseOrReadAnotherInventory(string id)
        {
            Assert.That(shop.Purchase(id, "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.UnknownPlayer));
            Assert.That(shop.TryGetQuantity(id, "capture-ball", out int value), Is.False);
            Assert.That(value, Is.Zero);
            Assert.That(shop.TryGetLastRequestId(id, out int request), Is.False);
            Assert.That(request, Is.Zero);
            Assert.That(shop.RemovePlayer(id), Is.False);
            Assert.That(Balance(), Is.Zero);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonpositiveRequestDoesNotReserveTheNextValidPurchase(int request)
        {
            money.AddMoney("A", 100);
            Assert.That(shop.Purchase("A", "capture-ball", request).Status, Is.EqualTo(ShopState.PurchaseStatus.InvalidRequest));
            Assert.That(Balance(), Is.EqualTo(100));
            Assert.That(Quantity(), Is.Zero);
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
        }

        [Test]
        public void DuplicateReadyPreservesStockAndPlayersRemainIndependent()
        {
            Register("B");
            money.AddMoney("A", 100);
            money.AddMoney("B", 599);
            shop.Purchase("A", "capture-ball", 1);
            Assert.That(shop.RegisterPlayer("A"), Is.False);
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity(id: "B"), Is.Zero);
            Assert.That(Balance("B"), Is.EqualTo(599));
            shop.Purchase("B", "bomb", 1);
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity("bomb", "B"), Is.EqualTo(1));
            Assert.That(Balance("B"), Is.Zero);
        }

        [Test]
        public void RemovalAndClearDiscardStockAndReplayHistory()
        {
            money.AddMoney("A", 200);
            shop.Purchase("A", "capture-ball", 1);
            Assert.That(shop.RemovePlayer("A"), Is.True);
            Assert.That(shop.RemovePlayer("A"), Is.False);
            shop.RegisterPlayer("A");
            Assert.That(Quantity(), Is.Zero);
            Assert.That(shop.Purchase("A", "bow", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(Quantity("bow"), Is.EqualTo(1));
            shop.Clear();
            Assert.That(shop.TryGetQuantity("A", "bow", out _), Is.False);
        }

        [Test]
        public void FullInventoryRejectsBeforeAnyMoneyIsSpent()
        {
            money.AddMoney("A", 100);
            // Reach the int boundary without billions of purchases; assertions use public behavior only.
            var inventories = (IDictionary)typeof(ShopState).GetField("inventories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shop);
            var inventory = inventories["A"];
            var counts = (int[])inventory.GetType().GetField("quantities").GetValue(inventory);
            counts[0] = int.MaxValue;
            balanceEvents = 0;
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.InventoryFull));
            Assert.That(Balance(), Is.EqualTo(100));
            Assert.That(Quantity(), Is.EqualTo(int.MaxValue));
            Assert.That(balanceEvents, Is.Zero);
        }

        [Test]
        public void SuccessfulBalanceObserversSeeGrantedStockAndCannotReenterPurchase()
        {
            money.AddMoney("A", 200);
            int observed = -1;
            ShopState.PurchaseStatus nested = ShopState.PurchaseStatus.Success;
            money.BalanceChanged += (id, balance) => {
                observed = Quantity();
                nested = shop.Purchase("A", "bow", 2).Status;
            };
            shop.Purchase("A", "capture-ball", 1);
            Assert.That(observed, Is.EqualTo(1));
            Assert.That(nested, Is.EqualTo(ShopState.PurchaseStatus.Unavailable));
            Assert.That(Balance(), Is.EqualTo(100));
            Assert.That(Quantity("bow"), Is.Zero);
            Assert.That(shop.Purchase("A", "bow", 2).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
        }

        [Test]
        public void InvalidCatalogIsRejectedRatherThanSellingForZeroOrAmbiguousIds()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopState.Product("x", "X", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShopState.Product("x", "X", -1));
            Assert.Throws<ArgumentException>(() => new ShopState.Product(" ", "X", 1));
            Assert.Throws<ArgumentException>(() => new ShopState.Product("x", " ", 1));
            Assert.Throws<ArgumentException>(() => new ShopState(new ShopState.Product[0], money.TrySpendMoney));
            Assert.Throws<ArgumentException>(() => new ShopState(new[] { Products[0], Products[0] }, money.TrySpendMoney));
        }

        [TestCase("capture-ball", 100)]
        [TestCase("bow", 100)]
        [TestCase("sniper", 599)]
        [TestCase("bomb", 599)]
        public void ConsumeDecrementsOneWithoutChargingOrChangingPurchaseReplay(string item, int price)
        {
            money.AddMoney("A", price * 2 + 17);
            shop.Purchase("A", item, 1);
            shop.Purchase("A", item, 2);
            balanceEvents = 0;
            Assert.That(shop.TryConsumeItem("A", item), Is.True);
            Assert.That(Quantity(item), Is.EqualTo(1));
            Assert.That(shop.TryConsumeItem("A", item), Is.True);
            Assert.That(Quantity(item), Is.Zero);
            Assert.That(shop.TryConsumeItem("A", item), Is.False);
            Assert.That(Quantity(item), Is.Zero);
            Assert.That(Balance(), Is.EqualTo(17));
            Assert.That(balanceEvents, Is.Zero);
            Assert.That(shop.TryGetLastRequestId("A", out int request), Is.True);
            Assert.That(request, Is.EqualTo(2));
            Assert.That(shop.Purchase("A", item, 2).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(Quantity(item), Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("unknown")]
        public void ConsumeRejectsUnknownPlayersWithoutTouchingOtherStock(string player)
        {
            money.AddMoney("A", 100);
            shop.Purchase("A", "capture-ball", 1);
            Assert.That(shop.TryConsumeItem(player, "capture-ball"), Is.False);
            Assert.That(Quantity(), Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("missing")]
        [TestCase("CAPTURE-BALL")]
        public void ConsumeRejectsInvalidItemsWithoutTouchingKnownStock(string item)
        {
            money.AddMoney("A", 100);
            shop.Purchase("A", "capture-ball", 1);
            Assert.That(shop.TryConsumeItem("A", item), Is.False);
            Assert.That(Quantity(), Is.EqualTo(1));
        }

        [Test]
        public void BalanceCallbackCannotConsumeStagedOrPreviouslyCommittedStock()
        {
            money.AddMoney("A", 300);
            shop.Purchase("A", "bow", 1);
            bool staged = true, committed = true;
            money.BalanceChanged += (id, balance) => {
                staged = shop.TryConsumeItem("A", "capture-ball");
                committed = shop.TryConsumeItem("A", "bow");
            };
            Assert.That(shop.Purchase("A", "capture-ball", 2).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(staged, Is.False);
            Assert.That(committed, Is.False);
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity("bow"), Is.EqualTo(1));
            Assert.That(shop.TryConsumeItem("A", "capture-ball"), Is.True);
        }

        [Test]
        public void RejectedSpendCallbackCannotConsumeStockThatWillBeRolledBack()
        {
            ShopState pending = null;
            bool consumed = true;
            pending = new ShopState(Products, (id, price) => {
                consumed = pending.TryConsumeItem(id, "capture-ball");
                return false;
            });
            pending.RegisterPlayer("A");
            Assert.That(pending.Purchase("A", "capture-ball", 1).Status,
                Is.EqualTo(ShopState.PurchaseStatus.InsufficientFunds));
            Assert.That(consumed, Is.False);
            Assert.That(pending.TryGetQuantity("A", "capture-ball", out int quantity), Is.True);
            Assert.That(quantity, Is.Zero);
            Assert.That(pending.TryConsumeItem("A", "capture-ball"), Is.False);
        }

        [Test]
        public void ConsumeAtIntegerMaximumMakesRoomForAnotherNormalPurchase()
        {
            var inventories = (IDictionary)typeof(ShopState).GetField("inventories", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shop);
            var inventory = inventories["A"];
            var counts = (int[])inventory.GetType().GetField("quantities").GetValue(inventory);
            counts[0] = int.MaxValue;
            Assert.That(shop.TryConsumeItem("A", "capture-ball"), Is.True);
            Assert.That(Quantity(), Is.EqualTo(int.MaxValue - 1));
            money.AddMoney("A", 100);
            Assert.That(shop.Purchase("A", "capture-ball", 1).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
            Assert.That(Quantity(), Is.EqualTo(int.MaxValue));
        }
    }
}
