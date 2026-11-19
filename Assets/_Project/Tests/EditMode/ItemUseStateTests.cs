using System;
using NUnit.Framework;
using Status = LiveStock.Economy.ItemUseState.UseStatus;

namespace LiveStock.Economy.Tests
{
    public sealed class ItemUseStateTests
    {
        private static readonly string[] ItemIds = { "capture-ball", "bow", "sniper", "bomb" };
        private static readonly ShopState.Product[] Products = {
            new ShopState.Product("capture-ball", "Ball", 100),
            new ShopState.Product("bow", "Bow", 100),
            new ShopState.Product("sniper", "Sniper", 599),
            new ShopState.Product("bomb", "Bomb", 599)
        };
        private MoneyState money;
        private ShopState shop;
        private ItemUseState uses;
        private int consumeCalls;

        [SetUp]
        public void SetUp()
        {
            consumeCalls = 0;
            money = new MoneyState(0, 10000);
            shop = new ShopState(Products, money.TrySpendMoney);
            uses = new ItemUseState(ItemIds, (player, item) => {
                consumeCalls++;
                return shop.TryConsumeItem(player, item);
            }, trySpendMoney: money.TrySpendMoney);
            Register("A");
        }

        private void Register(string player)
        {
            money.RegisterPlayer(player);
            shop.RegisterPlayer(player);
            uses.RegisterPlayer(player);
        }

        private void Buy(string item = "capture-ball", int count = 1, string player = "A")
        {
            int price = Array.Find(Products, product => product.Id == item).Price;
            money.AddMoney(player, price * count);
            shop.TryGetLastRequestId(player, out int request);
            for (int i = 0; i < count; i++)
                Assert.That(shop.Purchase(player, item, ++request).Status, Is.EqualTo(ShopState.PurchaseStatus.Success));
        }

        private int Quantity(string item = "capture-ball", string player = "A")
        {
            Assert.That(shop.TryGetQuantity(player, item, out int quantity), Is.True);
            return quantity;
        }

        private int Last(string player = "A")
        {
            Assert.That(uses.TryGetLastRequestId(player, out int request), Is.True);
            return request;
        }

        private static void Expect(ItemUseState.UseResult result, Status status, bool launch = false)
        {
            Assert.That(result.Status, Is.EqualTo(status));
            Assert.That(result.ShouldLaunch, Is.EqualTo(launch));
        }

        [TestCase("capture-ball")]
        [TestCase("bow")]
        [TestCase("sniper")]
        [TestCase("bomb")]
        public void FreshLaunchConsumesExactlyOneWithoutSpendingMoney(string item)
        {
            Buy(item, 2);
            money.AddMoney("A", 17);
            int balanceEvents = 0;
            money.BalanceChanged += (id, value) => balanceEvents++;
            Expect(uses.Use("A", item, 1, true, 0), Status.Launched, true);
            Assert.That(Quantity(item), Is.EqualTo(1));
            Assert.That(money.TryGetBalance("A", out int balance), Is.True);
            Assert.That(balance, Is.EqualTo(17));
            Assert.That(balanceEvents, Is.Zero);
            Assert.That(consumeCalls, Is.EqualTo(1));
            Assert.That(shop.TryGetLastRequestId("A", out int purchaseRequest), Is.True);
            Assert.That(purchaseRequest, Is.EqualTo(2));
        }

        [Test]
        public void LaunchWithoutAnyImpactStillConsumesAndCannotBeReplayedOrRefunded()
        {
            Buy();
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
            // No collision/impact is reported: inventory was already committed at launch.
            Expect(uses.Use("A", "capture-ball", 1, true, 100), Status.Launched);
            Assert.That(Quantity(), Is.Zero);
            Assert.That(consumeCalls, Is.EqualTo(1));
            Expect(uses.Use("A", "capture-ball", 2, true, 100), Status.NoStock);
            Assert.That(Quantity(), Is.Zero);
        }

        [Test]
        public void NoStockIsCachedButDoesNotStartCooldown()
        {
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.NoStock);
            Buy();
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.NoStock);
            Assert.That(consumeCalls, Is.EqualTo(1));
            Expect(uses.Use("A", "capture-ball", 2, true, 0), Status.Launched, true);
            Assert.That(Quantity(), Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("missing")]
        [TestCase("CAPTURE-BALL")]
        public void InvalidItemRecordsRejectionWithoutConsumingOrStartingCooldown(string item)
        {
            Buy();
            Expect(uses.Use("A", item, 1, true, 0), Status.InvalidItem);
            Expect(uses.Use("A", item, 1, true, 100), Status.InvalidItem);
            Assert.That(Last(), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.Zero);
            Assert.That(Quantity(), Is.EqualTo(1));
            Expect(uses.Use("A", "capture-ball", 2, true, 0), Status.Launched, true);
        }

        [Test]
        public void InvalidTargetIsFinalForItsIdButDoesNotConsumeOrStartCooldown()
        {
            Buy();
            Expect(uses.Use("A", "capture-ball", 1, false, 0), Status.InvalidRequest);
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.InvalidRequest);
            Assert.That(Last(), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.Zero);
            Assert.That(Quantity(), Is.EqualTo(1));
            Expect(uses.Use("A", "capture-ball", 2, true, 0), Status.Launched, true);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("unknown")]
        public void UnknownPlayersCannotConsumeOrCreateRequestHistory(string player)
        {
            Buy();
            Expect(uses.Use(player, "capture-ball", 100, true, 0), Status.UnknownPlayer);
            Assert.That(uses.TryGetLastRequestId(player, out int request), Is.False);
            Assert.That(request, Is.Zero);
            Assert.That(consumeCalls, Is.Zero);
            Assert.That(Last(), Is.Zero);
            Assert.That(Quantity(), Is.EqualTo(1));
            if (player == "unknown")
            {
                Register(player);
                Buy(player: player);
                Expect(uses.Use(player, "capture-ball", 1, true, 0), Status.Launched, true);
            }
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void NonpositiveIdsNeverAdvanceHistory(int request)
        {
            Buy();
            Expect(uses.Use("A", "capture-ball", request, true, 0), Status.InvalidRequest);
            Assert.That(Last(), Is.Zero);
            Assert.That(consumeCalls, Is.Zero);
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
        }

        [Test]
        public void CooldownIsSharedAcrossItemsAndExpiresAtExactBoundary()
        {
            Buy();
            Buy("bow");
            Expect(uses.Use("A", "capture-ball", 1, true, 1), Status.Launched, true);
            Expect(uses.Use("A", "bow", 2, true, 1.1), Status.Cooldown);
            Assert.That(Quantity("bow"), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.EqualTo(1));
            Expect(uses.Use("A", "bow", 2, true, 1.2), Status.Cooldown);
            Expect(uses.Use("A", "bow", 3, true, 1.2), Status.Launched, true);
            Assert.That(Quantity("bow"), Is.Zero);
        }

        [Test]
        public void ReplayConflictAndOlderIdsNeverConsumeOrOverwriteLatestResult()
        {
            Buy(count: 3);
            Buy("bow");
            Expect(uses.Use("A", "capture-ball", 10, true, 0), Status.Launched, true);
            Expect(uses.Use("A", "capture-ball", 10, false, 1), Status.Launched);
            Expect(uses.Use("A", "bow", 10, true, 1), Status.InvalidRequest);
            Expect(uses.Use("A", "capture-ball", 9, true, 1), Status.StaleRequest);
            Expect(uses.Use("A", "capture-ball", 10, true, 1), Status.Launched);
            Expect(uses.Use("A", "capture-ball", 11, true, 1), Status.Launched, true);
            Expect(uses.Use("A", "capture-ball", 10, true, 2), Status.StaleRequest);
            Assert.That(Last(), Is.EqualTo(11));
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity("bow"), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.EqualTo(2));
        }

        [Test]
        public void FreshRejectedRequestReplacesTheSingleReplayRecord()
        {
            Buy(count: 2);
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
            Expect(uses.Use("A", "missing", 100, true, 1), Status.InvalidItem);
            Expect(uses.Use("A", "capture-ball", 1, true, 1), Status.StaleRequest);
            Expect(uses.Use("A", "capture-ball", 99, true, 1), Status.StaleRequest);
            Expect(uses.Use("A", "missing", 100, true, 1), Status.InvalidItem);
            Assert.That(Last(), Is.EqualTo(100));
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.EqualTo(1));
        }

        [Test]
        public void PlayersHaveIndependentStockCooldownAndReplayAndRepeatReadyPreservesThem()
        {
            Register("B");
            Buy(count: 2);
            Buy(player: "B");
            Expect(uses.Use("A", "capture-ball", 5, true, 0), Status.Launched, true);
            Assert.That(uses.RegisterPlayer("A"), Is.False);
            Assert.That(shop.RegisterPlayer("A"), Is.False);
            Assert.That(Last(), Is.EqualTo(5));
            Expect(uses.Use("A", "capture-ball", 5, true, 0), Status.Launched);
            Expect(uses.Use("A", "capture-ball", 6, true, 0), Status.Cooldown);
            Expect(uses.Use("B", "capture-ball", 1, true, 0), Status.Launched, true);
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(Quantity(player: "B"), Is.Zero);
            Assert.That(Last("B"), Is.EqualTo(1));
        }

        [Test]
        public void DisconnectAndReconnectResetStockRequestSequenceAndCooldown()
        {
            Buy(count: 2);
            Expect(uses.Use("A", "capture-ball", 100, true, 10), Status.Launched, true);
            Assert.That(uses.RemovePlayer("A"), Is.True);
            Assert.That(uses.RemovePlayer("A"), Is.False);
            shop.RemovePlayer("A");
            money.RemovePlayer("A");
            Expect(uses.Use("A", "capture-ball", 101, true, 10), Status.UnknownPlayer);
            Register("A");
            Assert.That(Last(), Is.Zero);
            Assert.That(Quantity(), Is.Zero);
            Buy();
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
        }

        [Test]
        public void ClearDiscardsAllPlayersAndTheirCooldownAndHistory()
        {
            Register("B");
            Buy(count: 2);
            Buy(player: "B");
            uses.Use("A", "capture-ball", 10, true, 100);
            uses.Use("B", "capture-ball", 20, true, 100);
            uses.Clear();
            Assert.That(uses.TryGetLastRequestId("A", out _), Is.False);
            Assert.That(uses.TryGetLastRequestId("B", out _), Is.False);
            Expect(uses.Use("B", "capture-ball", 21, true, 100), Status.UnknownPlayer);
            Assert.That(uses.RegisterPlayer("A"), Is.True);
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
        }

        [Test]
        public void RecursiveCallbackCannotConsumeOrReplaceThePendingRequest()
        {
            Buy(count: 3);
            ItemUseState guarded = null;
            int calls = 0;
            guarded = new ItemUseState(ItemIds, (player, item) => {
                calls++;
                Assert.That(guarded.TryGetLastRequestId(player, out int pending), Is.True);
                Expect(guarded.Use(player, item, 1, true, 0), Status.Unavailable);
                Expect(guarded.Use(player, "bow", 2, true, 100), Status.Unavailable);
                Assert.That(guarded.TryGetLastRequestId(player, out int after), Is.True);
                Assert.That(after, Is.EqualTo(pending));
                return shop.TryConsumeItem(player, item);
            });
            guarded.RegisterPlayer("A");
            Expect(guarded.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
            Expect(guarded.Use("A", "capture-ball", 1, true, 100), Status.Launched);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(Quantity(), Is.EqualTo(2));
            // The recursive higher ID was transiently unavailable, not committed over its caller.
            Expect(guarded.Use("A", "bow", 2, true, 100), Status.NoStock);
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void ThrowingCallbackReleasesGuardWithoutAuthorizingAReplay()
        {
            Buy();
            int calls = 0;
            var guarded = new ItemUseState(ItemIds, (player, item) => {
                if (++calls == 1) throw new InvalidOperationException("Consumer failed before consuming.");
                return shop.TryConsumeItem(player, item);
            });
            guarded.RegisterPlayer("A");
            Assert.Throws<InvalidOperationException>(() => guarded.Use("A", "capture-ball", 1, true, 0));
            Expect(guarded.Use("A", "capture-ball", 1, true, 0), Status.Unavailable);
            Assert.That(Quantity(), Is.EqualTo(1));
            Expect(guarded.Use("A", "capture-ball", 2, true, 0), Status.Launched, true);
        }

        [Test]
        public void MaximumRequestIdCanLaunchOnlyOnceAndNeverWraps()
        {
            Buy(count: 2);
            Expect(uses.Use("A", "capture-ball", int.MaxValue, true, 0), Status.Launched, true);
            Expect(uses.Use("A", "capture-ball", int.MaxValue, true, 100), Status.Launched);
            Expect(uses.Use("A", "capture-ball", int.MaxValue - 1, true, 100), Status.StaleRequest);
            Expect(uses.Use("A", "capture-ball", int.MinValue, true, 100), Status.InvalidRequest);
            Expect(uses.Use("A", "capture-ball", 1, true, 100), Status.StaleRequest);
            Assert.That(Last(), Is.EqualTo(int.MaxValue));
            Assert.That(Quantity(), Is.EqualTo(1));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-0.001)]
        public void InvalidTimeNeverConsumesOrMutatesEvenOnDuplicateRequests(double now)
        {
            Buy(count: 2);
            Assert.Throws<ArgumentOutOfRangeException>(() => uses.Use("A", "capture-ball", 100, true, now));
            Assert.That(Last(), Is.Zero);
            Assert.That(consumeCalls, Is.Zero);
            Expect(uses.Use("A", "capture-ball", 1, true, 1), Status.Launched, true);
            Assert.Throws<ArgumentOutOfRangeException>(() => uses.Use("A", "capture-ball", 1, true, now));
            Assert.Throws<ArgumentOutOfRangeException>(() => uses.Use("A", "capture-ball", 100, true, now));
            Assert.That(Last(), Is.EqualTo(1));
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.EqualTo(1));
            Expect(uses.Use("A", "capture-ball", 2, true, 1.2), Status.Launched, true);
        }

        [Test]
        public void TimeRegressionCannotBypassServerCooldown()
        {
            Buy(count: 2);
            Expect(uses.Use("A", "capture-ball", 1, true, 10), Status.Launched, true);
            Expect(uses.Use("A", "capture-ball", 2, true, 9), Status.Cooldown);
            Assert.That(Quantity(), Is.EqualTo(1));
            Expect(uses.Use("A", "capture-ball", 3, true, 10.2), Status.Launched, true);
        }

        [Test]
        public void LargeFiniteTimeCannotRoundPositiveCooldownIntoImmediateRepeatedLaunches()
        {
            Buy(count: 2);
            Expect(uses.Use("A", "capture-ball", 1, true, double.MaxValue), Status.Launched, true);
            Expect(uses.Use("A", "capture-ball", 2, true, double.MaxValue), Status.Cooldown);
            Assert.That(Quantity(), Is.EqualTo(1));
        }

        [Test]
        public void OverflowingFutureCooldownDoesNotBecomeReadyAtFiniteTime()
        {
            Buy(count: 2);
            var slow = new ItemUseState(ItemIds, shop.TryConsumeItem, double.MaxValue);
            slow.RegisterPlayer("A");
            Expect(slow.Use("A", "capture-ball", 1, true, double.MaxValue / 2), Status.Launched, true);
            Expect(slow.Use("A", "capture-ball", 2, true, double.MaxValue), Status.Cooldown);
            Assert.That(Quantity(), Is.EqualTo(1));
        }

        [Test]
        public void ExplicitZeroCooldownAllowsDistinctRequestsAtTheSameFiniteTime()
        {
            Buy(count: 2);
            var immediate = new ItemUseState(ItemIds, shop.TryConsumeItem, 0);
            immediate.RegisterPlayer("A");
            Expect(immediate.Use("A", "capture-ball", 1, true, double.MaxValue), Status.Launched, true);
            Expect(immediate.Use("A", "capture-ball", 2, true, double.MaxValue), Status.Launched, true);
            Assert.That(Quantity(), Is.Zero);
        }

        [TestCase(99, Status.InsufficientFunds, 99, 0)]
        [TestCase(100, Status.Launched, 0, 1)]
        public void CrazyGunUsesWalletWithoutOwningAnyItems(int funds, Status status, int remaining, int launches)
        {
            money.AddMoney("A", funds);
            Assert.That(uses.EnterCrazyMode(), Is.True);
            int launched = 0;
            money.BalanceChanged += (player, balance) => Assert.That(launched, Is.EqualTo(1));
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 1, true, 0, () => launched++),
                status, launches == 1);
            Assert.That(launched, Is.EqualTo(launches));
            Assert.That(money.TryGetBalance("A", out int balance), Is.True);
            Assert.That(balance, Is.EqualTo(remaining));
            Assert.That(consumeCalls, Is.Zero);
            foreach (string item in ItemIds) Assert.That(Quantity(item), Is.Zero);
        }

        [Test]
        public void EnteringCrazyPreservesInventoryAndSharesNormalHistoryAndCooldown()
        {
            Buy(count: 2);
            money.AddMoney("A", 300);
            Expect(uses.Use("A", "capture-ball", 7, true, 0), Status.Launched, true);
            Assert.That(uses.EnterCrazyMode(), Is.True);
            Assert.That(uses.EnterCrazyMode(), Is.True);
            Assert.That(uses.Mode, Is.EqualTo(ItemUseState.UseMode.Crazy));
            Assert.That(Last(), Is.EqualTo(7));
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 7, true, 0.2), Status.InvalidRequest);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 8, true, 0.1), Status.Cooldown);
            int launched = 0;
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 9, true, 0.2, () => launched++),
                Status.Launched, true);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 9, true, 100, () => launched++),
                Status.Launched);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 8, true, 100), Status.StaleRequest);
            Expect(uses.Use("A", "capture-ball", 10, true, 100), Status.InvalidItem);
            Assert.That(launched, Is.EqualTo(1));
            Assert.That(Quantity(), Is.EqualTo(1));
            Assert.That(consumeCalls, Is.EqualTo(1));
            money.TryGetBalance("A", out int balance);
            Assert.That(balance, Is.EqualTo(200));
        }

        [Test]
        public void RejectedCrazyRequestsNeverChargeAndInsufficientFundsDoesNotStartCooldown()
        {
            money.AddMoney("A", 99);
            uses.EnterCrazyMode();
            int launches = 0;
            Action launch = () => launches++;
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 0, true, 0, launch), Status.InvalidRequest);
            Expect(uses.Use("unknown", ItemUseState.CrazyGunItemId, 1, true, 0, launch), Status.UnknownPlayer);
            Expect(uses.Use("A", "bow", 1, true, 0, launch), Status.InvalidItem);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 2, false, 0, launch), Status.InvalidRequest);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 3, true, 0, launch), Status.InsufficientFunds);
            money.TryGetBalance("A", out int unchanged);
            Assert.That(unchanged, Is.EqualTo(99));
            money.AddMoney("A", 101);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 3, true, 0, launch), Status.InsufficientFunds);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 4, true, 0, launch), Status.Launched, true);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 5, true, 0.1, launch), Status.Cooldown);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 5, true, 1, launch), Status.Cooldown);
            money.TryGetBalance("A", out int balance);
            Assert.That(balance, Is.EqualTo(100));
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(consumeCalls, Is.Zero);
        }

        [Test]
        public void CrazyIntervalIsIndependentButCannotResetThePreviousLaunchTime()
        {
            Buy();
            var paced = new ItemUseState(ItemIds, shop.TryConsumeItem, 0,
                money.TrySpendMoney, 0.5);
            paced.RegisterPlayer("A");
            Expect(paced.Use("A", "capture-ball", 1, true, 1), Status.Launched, true);
            money.AddMoney("A", 200);
            paced.EnterCrazyMode();
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 2, true, 1.49), Status.Cooldown);
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 3, true, 1.5), Status.Launched, true);
            paced.EnterCrazyMode();
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 4, true, 1.99), Status.Cooldown);
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 5, true, 2), Status.Launched, true);
        }

        [Test]
        public void EnteringCrazyCannotShortenAnOutstandingNormalCooldown()
        {
            Buy();
            var paced = new ItemUseState(ItemIds, shop.TryConsumeItem, 1,
                money.TrySpendMoney, 0.2);
            paced.RegisterPlayer("A");
            Expect(paced.Use("A", "capture-ball", 1, true, 0), Status.Launched, true);
            money.AddMoney("A", 200);
            paced.EnterCrazyMode();
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 2, true, 0.5), Status.Cooldown);
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 3, true, 1), Status.Launched, true);
            Expect(paced.Use("A", ItemUseState.CrazyGunItemId, 4, true, 1.2), Status.Launched, true);
        }

        [Test]
        public void BalanceCallbackSeesLaunchAndCannotReplayChargeBeforeEndingRound()
        {
            money.AddMoney("A", 300);
            uses.EnterCrazyMode();
            int launches = 0;
            money.BalanceChanged += (player, balance) => {
                Assert.That(launches, Is.EqualTo(1));
                Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 1, true, 0), Status.Unavailable);
                Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 2, true, 1), Status.Unavailable);
                Assert.That(Last(), Is.EqualTo(1));
                uses.EndRound();
            };
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 1, true, 0, () => launches++),
                Status.Launched, true);
            Assert.That(uses.Mode, Is.EqualTo(ItemUseState.UseMode.Ended));
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 1, true, 1), Status.RoundEnded);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 2, true, 1), Status.RoundEnded);
            money.TryGetBalance("A", out int balance);
            Assert.That(balance, Is.EqualTo(200));
            Assert.That(launches, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EndingIsTerminalAcrossClearAndLateRegistration(bool crazy)
        {
            Buy(count: 2);
            money.AddMoney("A", 200);
            if (crazy) uses.EnterCrazyMode();
            uses.EndRound();
            uses.EndRound();
            Assert.That(uses.EnterCrazyMode(), Is.False);
            Assert.That(uses.Mode, Is.EqualTo(ItemUseState.UseMode.Ended));
            Expect(uses.Use("A", "capture-ball", 1, true, 0), Status.RoundEnded);
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 2, true, 0), Status.RoundEnded);
            Assert.That(Quantity(), Is.EqualTo(2));
            money.TryGetBalance("A", out int balance);
            Assert.That(balance, Is.EqualTo(200));
            uses.Clear();
            uses.RegisterPlayer("B");
            Expect(uses.Use("B", ItemUseState.CrazyGunItemId, 1, true, 0), Status.RoundEnded);
            Assert.That(consumeCalls, Is.Zero);
        }

        [Test]
        public void MissingWalletCannotEnterCrazyAndGunCannotBeANormalProduct()
        {
            var normal = new ItemUseState(ItemIds, shop.TryConsumeItem);
            Assert.That(normal.EnterCrazyMode(), Is.False);
            Assert.That(normal.Mode, Is.EqualTo(ItemUseState.UseMode.Normal));
            Assert.Throws<ArgumentException>(() => new ItemUseState(
                new[] { ItemUseState.CrazyGunItemId }, shop.TryConsumeItem));
            Expect(uses.Use("A", ItemUseState.CrazyGunItemId, 1, true, 0), Status.InvalidItem);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(0)]
        [TestCase(-0.01)]
        public void CrazyIntervalMustBeFiniteAndPositive(double interval)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemUseState(
                ItemIds, shop.TryConsumeItem, trySpendMoney: money.TrySpendMoney, crazyFireInterval: interval));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-0.01)]
        public void InvalidCooldownCannotConstructAnAuthorizer(double cooldown)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemUseState(ItemIds, shop.TryConsumeItem, cooldown));
        }

        [Test]
        public void InvalidCatalogAndMissingConsumerAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new ItemUseState(null, shop.TryConsumeItem));
            Assert.Throws<ArgumentException>(() => new ItemUseState(Array.Empty<string>(), shop.TryConsumeItem));
            Assert.Throws<ArgumentException>(() => new ItemUseState(new[] { "bow", "bow" }, shop.TryConsumeItem));
            Assert.Throws<ArgumentException>(() => new ItemUseState(new[] { "bow", " " }, shop.TryConsumeItem));
            Assert.Throws<ArgumentException>(() => new ItemUseState(new[] { "bow", null }, shop.TryConsumeItem));
            Assert.Throws<ArgumentNullException>(() => new ItemUseState(ItemIds, null));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void InvalidPlayerIdsCannotRegisterOrRemoveOtherPlayers(string player)
        {
            Assert.That(uses.RegisterPlayer(player), Is.False);
            Assert.That(uses.RemovePlayer(player), Is.False);
            Assert.That(Last(), Is.Zero);
        }
    }
}
