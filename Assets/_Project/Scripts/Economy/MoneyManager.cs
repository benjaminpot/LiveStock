using System;
using UnityEngine;

namespace LiveStock.Economy
{
    [DisallowMultipleComponent]
    public sealed class MoneyManager : MonoBehaviour
    {
        [SerializeField, Min(0)] private int initialBalance = 0;
        [SerializeField, Min(0)] private int incomePerSecond = 10000;

        private MoneyState state;
        public event Action<string, int> BalanceChanged;

        private void Awake()
        {
            state = new MoneyState(initialBalance, incomePerSecond);
            state.BalanceChanged += OnBalanceChanged;
        }

        public bool RegisterPlayer(string playerId) => state.RegisterPlayer(playerId);
        public bool RemovePlayer(string playerId) => state.RemovePlayer(playerId);
        public bool TryGetBalance(string playerId, out int balance) => state.TryGetBalance(playerId, out balance);
        public bool AddMoney(string playerId, int amount) => state.AddMoney(playerId, amount);
        public bool TrySpendMoney(string playerId, int amount) => state.TrySpendMoney(playerId, amount);
        public bool StartIncome(string playerId) => state.StartIncome(playerId);
        public bool StopIncome(string playerId) => state.StopIncome(playerId);
        public void Clear() => state.Clear();

        private void Update() => state.Tick(Time.unscaledDeltaTime);

        private void OnBalanceChanged(string playerId, int balance) => BalanceChanged?.Invoke(playerId, balance);

        private void OnDestroy()
        {
            if (state == null) return;
            state.BalanceChanged -= OnBalanceChanged;
            state.Clear();
        }
    }
}
