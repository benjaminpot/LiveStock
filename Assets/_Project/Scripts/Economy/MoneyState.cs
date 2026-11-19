using System;
using System.Collections.Generic;

namespace LiveStock.Economy
{
    public sealed class MoneyState
    {
        private sealed class Wallet
        {
            public int balance;
            public double elapsedSeconds;
            public bool earning;
        }

        private readonly Dictionary<string, Wallet> wallets = new Dictionary<string, Wallet>();
        private readonly int initialBalance;
        private readonly int incomePerSecond;
        public bool IncomeSuspended { get; private set; }

        public event Action<string, int> BalanceChanged;

        public MoneyState(int initialBalance, int incomePerSecond)
        {
            if (initialBalance < 0) throw new ArgumentOutOfRangeException(nameof(initialBalance));
            if (incomePerSecond < 0) throw new ArgumentOutOfRangeException(nameof(incomePerSecond));
            this.initialBalance = initialBalance;
            this.incomePerSecond = incomePerSecond;
        }

        public bool RegisterPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || wallets.ContainsKey(playerId)) return false;
            wallets.Add(playerId, new Wallet { balance = initialBalance });
            return true;
        }

        public bool RemovePlayer(string playerId) =>
            !string.IsNullOrWhiteSpace(playerId) && wallets.Remove(playerId);

        public bool TryGetBalance(string playerId, out int balance)
        {
            balance = 0;
            if (!TryGetWallet(playerId, out var wallet)) return false;
            balance = wallet.balance;
            return true;
        }

        public bool AddMoney(string playerId, int amount)
        {
            if (amount <= 0 || !TryGetWallet(playerId, out var wallet)) return false;
            long total = (long)wallet.balance + amount;
            if (total > int.MaxValue) return false;
            wallet.balance = (int)total;
            BalanceChanged?.Invoke(playerId, wallet.balance);
            return true;
        }

        public bool TrySpendMoney(string playerId, int amount) => TrySpendMoney(playerId, amount, null);

        // Complete the accepted action before synchronous balance observers can end the round.
        public bool TrySpendMoney(string playerId, int amount, Action onSpent)
        {
            if (amount <= 0 || !TryGetWallet(playerId, out var wallet) || wallet.balance < amount)
                return false;
            wallet.balance -= amount;
            onSpent?.Invoke();
            BalanceChanged?.Invoke(playerId, wallet.balance);
            return true;
        }

        public bool StartIncome(string playerId)
        {
            if (IncomeSuspended || !TryGetWallet(playerId, out var wallet)) return false;
            wallet.earning = true;
            return true;
        }

        public bool StopIncome(string playerId)
        {
            if (!TryGetWallet(playerId, out var wallet)) return false;
            wallet.earning = false;
            wallet.elapsedSeconds = 0;
            return true;
        }

        public void SuspendIncome()
        {
            IncomeSuspended = true;
            foreach (var wallet in wallets.Values)
            {
                wallet.earning = false;
                wallet.elapsedSeconds = 0;
            }
        }

        public void Tick(double deltaSeconds)
        {
            if (deltaSeconds < 0 || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (IncomeSuspended) return;

            foreach (var entry in wallets)
            {
                var wallet = entry.Value;
                if (!wallet.earning) continue;
                double elapsed = wallet.elapsedSeconds + deltaSeconds;
                double wholeSeconds = Math.Floor(elapsed);
                wallet.elapsedSeconds = elapsed - wholeSeconds;
                // Decimal frame intervals can land a few binary ulps below a whole second.
                if (1 - wallet.elapsedSeconds <= 4 * 2.2204460492503131e-16)
                {
                    wholeSeconds++;
                    wallet.elapsedSeconds = 0;
                }
                if (wholeSeconds < 1 || incomePerSecond == 0) continue;

                // Reject the entire batch before converting a potentially enormous delta.
                // Consumed whole seconds never become deferred income after an overflow.
                if (wholeSeconds > (int.MaxValue - wallet.balance) / incomePerSecond) continue;
                wallet.balance = (int)(wallet.balance + (long)wholeSeconds * incomePerSecond);
                BalanceChanged?.Invoke(entry.Key, wallet.balance);
                if (IncomeSuspended) return;
            }
        }

        public void Clear() => wallets.Clear();

        private bool TryGetWallet(string playerId, out Wallet wallet)
        {
            wallet = null;
            return !string.IsNullOrWhiteSpace(playerId) && wallets.TryGetValue(playerId, out wallet);
        }
    }
}
