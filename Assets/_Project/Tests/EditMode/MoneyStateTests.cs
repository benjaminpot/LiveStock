using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LiveStock.Economy.Tests
{
    public sealed class MoneyStateTests
    {
        private MoneyState state;
        private List<string> changes;

        [SetUp]
        public void SetUp()
        {
            state = new MoneyState(0, 10000);
            changes = new List<string>();
            state.BalanceChanged += (id, balance) => changes.Add(id + ":" + balance);
        }

        private int Balance(string id)
        {
            Assert.That(state.TryGetBalance(id, out int balance), Is.True);
            return balance;
        }

        [Test]
        public void IncomeWaitsForWholeSecondsAndRetainsRemainder()
        {
            Assert.That(state.RegisterPlayer("A"), Is.True);
            Assert.That(Balance("A"), Is.Zero);
            state.Tick(5);
            Assert.That(Balance("A"), Is.Zero);
            Assert.That(changes, Is.Empty);
            state.StartIncome("A");
            state.Tick(0.9);
            Assert.That(Balance("A"), Is.Zero);
            state.Tick(0.1);
            Assert.That(Balance("A"), Is.EqualTo(10000));
            state.Tick(2.4);
            Assert.That(Balance("A"), Is.EqualTo(30000));
            state.Tick(0.6);
            Assert.That(Balance("A"), Is.EqualTo(40000));
            Assert.That(changes, Is.EqualTo(new[] { "A:10000", "A:30000", "A:40000" }));
        }

        [Test]
        public void DuplicateReadyPreservesBalanceAndFractionalTime()
        {
            state.RegisterPlayer("A");
            state.StartIncome("A");
            state.AddMoney("A", 7);
            state.Tick(0.9);
            Assert.That(state.RegisterPlayer("A"), Is.False);
            Assert.That(state.StartIncome("A"), Is.True);
            Assert.That(Balance("A"), Is.EqualTo(7));
            state.Tick(0.1);
            Assert.That(Balance("A"), Is.EqualTo(10007));
            Assert.That(changes, Is.EqualTo(new[] { "A:7", "A:10007" }));
        }

        [Test]
        public void WalletsHaveIndependentClocksAndTransactions()
        {
            state.RegisterPlayer("A");
            state.StartIncome("A");
            state.Tick(0.75);
            state.RegisterPlayer("B");
            state.StartIncome("B");
            state.Tick(0.25);
            Assert.That(Balance("A"), Is.EqualTo(10000));
            Assert.That(Balance("B"), Is.Zero);
            state.AddMoney("A", 42);
            state.TrySpendMoney("A", 2);
            Assert.That(Balance("B"), Is.Zero);
            state.Tick(0.75);
            Assert.That(Balance("A"), Is.EqualTo(10040));
            Assert.That(Balance("B"), Is.EqualTo(10000));
        }

        [Test]
        public void SpendingRejectsInsufficientBalanceAndCanReachExactZero()
        {
            state.RegisterPlayer("A");
            state.AddMoney("A", 10000);
            changes.Clear();
            Assert.That(state.TrySpendMoney("A", 10001), Is.False);
            Assert.That(Balance("A"), Is.EqualTo(10000));
            Assert.That(changes, Is.Empty);
            Assert.That(state.TrySpendMoney("A", 10000), Is.True);
            Assert.That(Balance("A"), Is.Zero);
            Assert.That(changes, Is.EqualTo(new[] { "A:0" }));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void InvalidAmountsCannotChangeBalance(int amount)
        {
            state.RegisterPlayer("A");
            Assert.That(state.AddMoney("A", amount), Is.False);
            Assert.That(state.TrySpendMoney("A", amount), Is.False);
            Assert.That(Balance("A"), Is.Zero);
            Assert.That(changes, Is.Empty);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void InvalidIdsAreRejectedByEveryPlayerOperation(string id)
        {
            Assert.That(state.RegisterPlayer(id), Is.False);
            Assert.That(state.RemovePlayer(id), Is.False);
            Assert.That(state.TryGetBalance(id, out int balance), Is.False);
            Assert.That(balance, Is.Zero);
            Assert.That(state.AddMoney(id, 1), Is.False);
            Assert.That(state.TrySpendMoney(id, 1), Is.False);
            Assert.That(state.StartIncome(id), Is.False);
            Assert.That(state.StopIncome(id), Is.False);
            Assert.That(changes, Is.Empty);
        }

        [Test]
        public void UnknownPlayersAreNotCreatedByTransactionsOrIncome()
        {
            Assert.That(state.AddMoney("unknown", 1), Is.False);
            Assert.That(state.TrySpendMoney("unknown", 1), Is.False);
            Assert.That(state.StartIncome("unknown"), Is.False);
            Assert.That(state.StopIncome("unknown"), Is.False);
            Assert.That(state.RemovePlayer("unknown"), Is.False);
            Assert.That(state.TryGetBalance("unknown", out int balance), Is.False);
            Assert.That(balance, Is.Zero);
        }

        [Test]
        public void StopDropsFractionAndRemovalStartsANewWallet()
        {
            state.RegisterPlayer("A");
            state.StartIncome("A");
            state.Tick(1.9);
            Assert.That(state.StopIncome("A"), Is.True);
            Assert.That(state.StopIncome("A"), Is.True);
            state.Tick(10);
            Assert.That(Balance("A"), Is.EqualTo(10000));
            state.StartIncome("A");
            state.Tick(0.9);
            Assert.That(Balance("A"), Is.EqualTo(10000));
            state.Tick(0.1);
            Assert.That(Balance("A"), Is.EqualTo(20000));
            Assert.That(state.RemovePlayer("A"), Is.True);
            Assert.That(state.RemovePlayer("A"), Is.False);
            changes.Clear();
            state.Tick(10);
            Assert.That(changes, Is.Empty);
            state.RegisterPlayer("A");
            Assert.That(Balance("A"), Is.Zero);
            state.Clear();
            Assert.That(state.TryGetBalance("A", out _), Is.False);
        }

        [Test]
        public void OverflowRejectsWholeBatchWithoutDeferringOrAffectingOthers()
        {
            state.RegisterPlayer("A");
            state.RegisterPlayer("B");
            state.AddMoney("A", int.MaxValue - 10000);
            Assert.That(state.AddMoney("A", 10001), Is.False);
            state.StartIncome("A");
            state.StartIncome("B");
            changes.Clear();
            state.Tick(2.5);
            Assert.That(Balance("A"), Is.EqualTo(int.MaxValue - 10000));
            Assert.That(Balance("B"), Is.EqualTo(20000));
            Assert.That(changes, Is.EqualTo(new[] { "B:20000" }));
            state.Tick(0.5);
            Assert.That(Balance("A"), Is.EqualTo(int.MaxValue));
            Assert.That(Balance("B"), Is.EqualTo(30000));
            state.Tick(double.MaxValue);
            Assert.That(Balance("A"), Is.EqualTo(int.MaxValue));
            Assert.That(Balance("B"), Is.EqualTo(30000));
            state.TrySpendMoney("A", int.MaxValue);
            state.Tick(1);
            Assert.That(Balance("A"), Is.EqualTo(10000));
        }

        [Test]
        public void IncomeProductCannotOverflowAndZeroIncomeDoesNotNotify()
        {
            state = new MoneyState(0, int.MaxValue);
            state.RegisterPlayer("A");
            state.StartIncome("A");
            state.Tick(4294967296d);
            Assert.That(Balance("A"), Is.Zero);
            state.Tick(1);
            Assert.That(Balance("A"), Is.EqualTo(int.MaxValue));
            state = new MoneyState(5, 0);
            state.BalanceChanged += (id, balance) => changes.Add(id);
            state.RegisterPlayer("A");
            state.StartIncome("A");
            state.Tick(double.MaxValue);
            Assert.That(Balance("A"), Is.EqualTo(5));
            Assert.That(changes, Is.Empty);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidTimeIsRejectedWithoutChangingWallets(double delta)
        {
            state.RegisterPlayer("A");
            state.StartIncome("A");
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Tick(delta));
            Assert.That(Balance("A"), Is.Zero);
            state.Tick(1);
            Assert.That(Balance("A"), Is.EqualTo(10000));
        }

        [Test]
        public void SuspensionPreservesBalancesAndPermanentlyStopsExistingAndLateWallets()
        {
            state.RegisterPlayer("A");
            state.RegisterPlayer("B");
            state.AddMoney("A", 100);
            state.AddMoney("B", 99);
            state.StartIncome("A");
            state.StartIncome("B");
            state.Tick(0.9);
            changes.Clear();
            state.SuspendIncome();
            state.SuspendIncome();
            Assert.That(state.IncomeSuspended, Is.True);
            Assert.That(state.RegisterPlayer("A"), Is.False);
            Assert.That(state.StartIncome("A"), Is.False);
            Assert.That(state.StartIncome("B"), Is.False);
            Assert.That(state.RegisterPlayer("late"), Is.True);
            Assert.That(state.StartIncome("late"), Is.False);
            state.Tick(1000);
            Assert.That(Balance("A"), Is.EqualTo(100));
            Assert.That(Balance("B"), Is.EqualTo(99));
            Assert.That(Balance("late"), Is.Zero);
            Assert.That(changes, Is.Empty);
            Assert.That(state.TrySpendMoney("A", 100), Is.True);
            Assert.That(Balance("A"), Is.Zero);
            state.RemovePlayer("A");
            state.RegisterPlayer("A");
            Assert.That(state.StartIncome("A"), Is.False);
            state.Clear();
            state.RegisterPlayer("next");
            Assert.That(state.StartIncome("next"), Is.False);
            state.Tick(1);
            Assert.That(Balance("next"), Is.Zero);
        }

        [Test]
        public void SuspendingFromAnIncomeCallbackStopsTheBatchAndAllowsLateRegistration()
        {
            state.RegisterPlayer("A");
            state.RegisterPlayer("B");
            state.StartIncome("A");
            state.StartIncome("B");
            int notifications = 0;
            state.BalanceChanged += (id, balance) => {
                notifications++;
                state.SuspendIncome();
                Assert.That(state.RegisterPlayer("late"), Is.True);
                Assert.That(state.StartIncome("late"), Is.False);
            };
            state.Tick(1);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(Balance("A") + Balance("B"), Is.EqualTo(10000));
            state.Tick(10);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(Balance("late"), Is.Zero);
        }

        [TestCase(99, false)]
        [TestCase(100, true)]
        public void AcceptedActionRunsExactlyOnceBeforeBalanceObservers(int funds, bool accepted)
        {
            state.RegisterPlayer("A");
            state.AddMoney("A", funds);
            int actions = 0;
            int notifications = 0;
            state.BalanceChanged += (id, balance) => {
                notifications++;
                Assert.That(actions, Is.EqualTo(1));
                Assert.That(balance, Is.Zero);
            };
            Assert.That(state.TrySpendMoney("A", 100, () => {
                Assert.That(Balance("A"), Is.Zero);
                actions++;
                state.SuspendIncome();
            }), Is.EqualTo(accepted));
            Assert.That(actions, Is.EqualTo(accepted ? 1 : 0));
            Assert.That(notifications, Is.EqualTo(actions));
            Assert.That(Balance("A"), Is.EqualTo(accepted ? 0 : funds));
        }

        [Test]
        public void NegativeConfigurationIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoneyState(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoneyState(0, -1));
        }
    }
}
