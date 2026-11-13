using System;
using System.Collections.Generic;

namespace LiveStock.Economy
{
    // Launch authorization only: impacts never consume or refund inventory.
    public sealed class ItemUseState
    {
        public enum UseStatus
        {
            Launched, NoStock, InvalidItem, InvalidRequest, UnknownPlayer,
            Cooldown, StaleRequest, Unavailable
        }

        public readonly struct UseResult
        {
            public readonly int RequestId;
            public readonly string ItemId;
            public readonly UseStatus Status;
            public readonly bool ShouldLaunch;

            public UseResult(int requestId, string itemId, UseStatus status, bool shouldLaunch = false)
            {
                RequestId = requestId;
                ItemId = itemId;
                Status = status;
                ShouldLaunch = shouldLaunch;
            }
        }

        private sealed class Player
        {
            public int lastRequestId;
            public string lastItemId;
            public UseStatus lastStatus;
            public bool consuming;
            public bool hasLaunched;
            public double lastUseSeconds;
        }

        private readonly HashSet<string> items = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Player> players = new Dictionary<string, Player>(StringComparer.Ordinal);
        private readonly Func<string, string, bool> tryConsumeItem;
        private readonly double cooldownSeconds;

        public ItemUseState(IReadOnlyList<string> itemIds, Func<string, string, bool> tryConsumeItem,
            double cooldownSeconds = 0.2)
        {
            if (itemIds == null || itemIds.Count == 0) throw new ArgumentException(nameof(itemIds));
            this.tryConsumeItem = tryConsumeItem ?? throw new ArgumentNullException(nameof(tryConsumeItem));
            if (!IsFinite(cooldownSeconds) || cooldownSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(cooldownSeconds));
            this.cooldownSeconds = cooldownSeconds;
            for (int i = 0; i < itemIds.Count; i++)
            {
                string itemId = itemIds[i];
                if (string.IsNullOrWhiteSpace(itemId) || !items.Add(itemId))
                    throw new ArgumentException("Item IDs must be nonempty and unique.", nameof(itemIds));
            }
        }

        public bool RegisterPlayer(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) || players.ContainsKey(playerId)) return false;
            players.Add(playerId, new Player());
            return true;
        }

        public bool RemovePlayer(string playerId) =>
            !string.IsNullOrWhiteSpace(playerId) && players.Remove(playerId);

        public bool TryGetLastRequestId(string playerId, out int requestId)
        {
            requestId = 0;
            if (!TryGetPlayer(playerId, out var player)) return false;
            requestId = player.lastRequestId;
            return true;
        }

        public void Clear() => players.Clear();

        public UseResult Use(string playerId, string itemId, int requestId, bool allowed, double nowSeconds)
        {
            if (!IsFinite(nowSeconds) || nowSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            if (!TryGetPlayer(playerId, out var player))
                return new UseResult(requestId, itemId, UseStatus.UnknownPlayer);
            if (requestId <= 0)
                return new UseResult(requestId, itemId, UseStatus.InvalidRequest);
            // A synchronous consumption callback cannot overwrite its pending request.
            if (player.consuming)
                return new UseResult(requestId, itemId, UseStatus.Unavailable);
            if (requestId == player.lastRequestId)
                return new UseResult(requestId, itemId, itemId == player.lastItemId
                    ? player.lastStatus : UseStatus.InvalidRequest);
            if (requestId < player.lastRequestId)
                return new UseResult(requestId, itemId, UseStatus.StaleRequest);

            // Only the latest request is retained; even rejected fresh requests are final.
            player.lastRequestId = requestId;
            player.lastItemId = itemId;
            player.lastStatus = UseStatus.Unavailable;
            if (itemId == null || !items.Contains(itemId))
                player.lastStatus = UseStatus.InvalidItem;
            else if (!allowed)
                player.lastStatus = UseStatus.InvalidRequest;
            // Equality also guards positive cooldowns too small to change a large timestamp.
            else if (player.hasLaunched && (nowSeconds < player.lastUseSeconds + cooldownSeconds ||
                (cooldownSeconds > 0 && nowSeconds == player.lastUseSeconds)))
                player.lastStatus = UseStatus.Cooldown;
            else
            {
                player.consuming = true;
                try
                {
                    if (tryConsumeItem(playerId, itemId))
                    {
                        player.lastUseSeconds = nowSeconds;
                        player.hasLaunched = true;
                        player.lastStatus = UseStatus.Launched;
                        return new UseResult(requestId, itemId, UseStatus.Launched, true);
                    }
                    player.lastStatus = UseStatus.NoStock;
                }
                finally { player.consuming = false; }
            }
            return new UseResult(requestId, itemId, player.lastStatus);
        }

        private bool TryGetPlayer(string playerId, out Player player)
        {
            player = null;
            return !string.IsNullOrWhiteSpace(playerId) && players.TryGetValue(playerId, out player);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
