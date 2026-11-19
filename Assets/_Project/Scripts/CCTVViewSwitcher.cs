using System;
using System.Collections.Generic;
using System.Text;
using Unity.RenderStreaming;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public sealed class CCTVViewSwitcher : SignalingHandlerBase,
    IOfferHandler, IAddChannelHandler, IDisconnectHandler, IDeletedConnectionHandler
{
    [Serializable]
    public sealed class CCTVView
    {
        public Camera streamCamera;
        public Camera previewCamera;
        public string displayName;
    }

    [Tooltip("The existing Video Stream Sender provides settings for each connection's own sender.")]
    [SerializeField] private VideoStreamSender videoSender;

    [SerializeField] private LiveStock.Economy.MoneyManager moneyManager;
    [SerializeField] private LiveStock.Economy.ShopManager shopManager;
    [SerializeField] private LiveStock.Items.ItemUseManager itemUseManager;

    [Tooltip("For each CCTV group, assign its CCTV camera and local preview camera.")]
    [SerializeField] private CCTVView[] views = Array.Empty<CCTVView>();

    [Header("Monitor rules")]
    [SerializeField, Min(1)] private float heatLimit = 100;
    [SerializeField, Min(0.1f)] private float heatPerViewer = 5;
    [SerializeField, Min(0)] private float coolingRate = 10;
    [SerializeField, Min(0.1f)] private float recoveryDuration = 10;
    [SerializeField, Min(0)] private float switchCooldown = 3;

    [Header("Test projectile")]
    [SerializeField] private CCTVTestProjectile projectilePrefab;

    private int previewIndex;
    private CCTVMonitorState state;
    private SignalingManager signalingManager;
    private Broadcast sourceBroadcast;
    private InputReceiver inputTemplate;
    private InputActionAsset inputActions;
    private InputAction switchAction;
    private bool senderWasEnabled;
    private bool inputWasEnabled;
    private bool registered;
    private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
    private readonly Dictionary<Camera, CameraOutput> cameraOutputs = new Dictionary<Camera, CameraOutput>();
    private readonly HashSet<Camera> blackedOutCameras = new HashSet<Camera>();
    private float nextStatusTime;

    public SignalingManager ManagedSignalingManager => signalingManager != null
        ? signalingManager
        : videoSender != null ? videoSender.GetComponent<SignalingManager>() : null;

    /// <summary>讓多人場景只保留 CCTV 的本機預覽 Camera，不要把串流來源誤當一般場景 Camera 關閉。</summary>
    public void CollectPreviewCameras(ICollection<Camera> target)
    {
        if (target == null || views == null)
            return;

        foreach (var view in views)
            if (view != null && view.previewCamera != null)
                target.Add(view.previewCamera);
    }

    private sealed class Session
    {
        public GameObject output;
        // DEMO TEMPORARY: 網頁目前只展示一個主 CCTV。只建立一個 sender，避免隱藏的
        // 第二串流繼續渲染及累積過熱；正式恢復雙畫面時將長度改回 2。
        public ViewOutput[] views = new ViewOutput[1];
        public InputReceiver input;
        public InputActionAsset actions;
        public RTCDataChannel channel;
        public DelegateOnMessage onMessage;
        public DelegateOnClose onClose;
        public int kills;
    }

    private sealed class ViewOutput
    {
        public Camera camera;
        public VideoStreamSender sender;
        public int appliedIndex = -1;
    }

    private struct CameraOutput
    {
        public int cullingMask;
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
    }

    [Serializable]
    private sealed class StatusMessage
    {
        public string type = "cctv-state";
        public int selectedIndex;
        public int[] selectedIndices;
        public string[] videoMids;
        public float cooldownRemaining;
        public float cooldownDuration;
        public float heatLimit;
        public MonitorMessage[] monitors;
    }

    [Serializable]
    private sealed class MoneyStatusMessage
    {
        public string type = "money-state";
        public int balance;
    }

    [Serializable]
    private sealed class ShopStatusMessage
    {
        public string type = "shop-state";
        public int lastRequestId;
        public ShopProductMessage[] items;
    }

    [Serializable]
    private sealed class ShopProductMessage
    {
        public string id;
        public string name;
        public int price;
        public int quantity;
    }

    [Serializable]
    private sealed class ShopResultMessage
    {
        public string type = "shop-result";
        public int requestId;
        public string itemId;
        public string status;
    }

    [Serializable]
    private sealed class ItemStatusMessage
    {
        public string type = "item-state";
        public int lastRequestId;
        public string mode;
        public string gunItemId = LiveStock.Items.ItemUseManager.CrazyGunItemId;
        public int shotCost = LiveStock.Items.ItemUseManager.CrazyShotCost;
        public float fireInterval;
    }

    [Serializable]
    private sealed class ItemResultMessage
    {
        public string type = "item-result";
        public int requestId;
        public string itemId;
        public int slot;
        public int index;
        public string status;
        public string objectName;
        public int kills;
    }

    [Serializable]
    private sealed class MonitorMessage
    {
        public string name;
        public string status;
        public float heat;
        public float recoveryRemaining;
        public int viewers;
    }

    [Serializable]
    private sealed class Command
    {
        public string type;
        public int index;
        public int slot;
        public float x;
        public float y;
        public string itemId;
        public int requestId;
    }

    [Serializable]
    private sealed class FireResult
    {
        public string type = "cctv-fire-result";
        public int slot;
        public int index;
        public string status;
        public string objectName;
        public int kills;
    }

    private void Awake()
    {
        if (videoSender == null || views.Length == 0)
        {
            Debug.LogWarning("[CCTVViewSwitcher] Assign Video Sender and at least one view.", this);
            enabled = false;
            return;
        }

        if (videoSender.source != VideoStreamSource.Camera)
        {
            Debug.LogWarning("[CCTVViewSwitcher] Set Video Stream Sender Source to Camera.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < views.Length; i++)
        {
            var view = views[i];
            if (view == null || view.streamCamera == null || view.previewCamera == null ||
                view.streamCamera == view.previewCamera)
            {
                Debug.LogWarning($"[CCTVViewSwitcher] View {i + 1} needs separate stream and preview cameras.", this);
                enabled = false;
                return;
            }

            for (int j = 0; j < i; j++)
            {
                var previous = views[j];
                if (view.streamCamera == previous.streamCamera ||
                    view.streamCamera == previous.previewCamera ||
                    view.previewCamera == previous.streamCamera ||
                    view.previewCamera == previous.previewCamera)
                {
                    Debug.LogWarning("[CCTVViewSwitcher] Each view must use its own cameras.", this);
                    enabled = false;
                    return;
                }
            }
        }

        signalingManager = videoSender.GetComponent<SignalingManager>();
        sourceBroadcast = videoSender.GetComponent<Broadcast>();
        inputTemplate = videoSender.GetComponent<InputReceiver>();
        if (signalingManager == null || sourceBroadcast == null || inputTemplate == null)
        {
            Debug.LogWarning("[CCTVViewSwitcher] Keep Signaling Manager, Broadcast and Input Receiver on the template Video Sender object.", this);
            enabled = false;
            return;
        }
        inputActions = inputTemplate.actions;
        if (inputActions == null)
        {
            enabled = false;
            return;
        }
        foreach (var actionEvent in inputTemplate.actionEvents)
            for (int i = 0; i < actionEvent.GetPersistentEventCount(); i++)
                if (actionEvent.GetPersistentTarget(i) == this && actionEvent.GetPersistentMethodName(i) == nameof(OnSwitch))
                    switchAction = inputActions.FindAction(actionEvent.actionId);
        if (switchAction == null) switchAction = inputActions.FindAction("Player/CCTV Switch");

        state = new CCTVMonitorState(views.Length, heatLimit, heatPerViewer,
            coolingRate, recoveryDuration, switchCooldown);

        // Register video creation before Broadcast answers the offer. Broadcast
        // continues to handle audio; it must not also send the shared video/input.
        sourceBroadcast.RemoveComponent(videoSender);
        sourceBroadcast.RemoveComponent(inputTemplate);
        senderWasEnabled = videoSender.enabled;
        inputWasEnabled = inputTemplate.enabled;
        videoSender.enabled = false;
        inputTemplate.enabled = false;
        signalingManager.RemoveSignalingHandler(sourceBroadcast);
        signalingManager.AddSignalingHandler(this);
        signalingManager.AddSignalingHandler(sourceBroadcast);
        registered = true;
        if (moneyManager != null) moneyManager.BalanceChanged += OnMoneyChanged;
        if (itemUseManager != null) itemUseManager.Impact += OnItemImpact;
        if (itemUseManager != null) itemUseManager.ModeChanged += OnItemModeChanged;
        foreach (var view in views) view.streamCamera.enabled = false;
        ApplyPreview();
    }

    // Identify the template's old click-to-switch action so remote receivers
    // can exclude it. Inspector calls here still control the local preview.
    public void OnSwitch(InputAction.CallbackContext context)
    {
        if (context.performed)
            SwitchNext();
    }

    public void SwitchNext()
    {
        if (isActiveAndEnabled) SwitchToView((previewIndex + 1) % views.Length);
    }

    public void SwitchToView(int index)
    {
        if (!isActiveAndEnabled || state == null || index < 0 || index >= views.Length) return;
        previewIndex = index;
        ApplyPreview();
    }

    [ContextMenu("測試/進入瘋狂模式")]
    public void EnterCrazyMode()
    {
        if (!Application.isPlaying) return;
        if (!isActiveAndEnabled || shopManager == null || !shopManager.IsConfiguredFor(moneyManager) ||
            itemUseManager == null || !itemUseManager.IsConfiguredFor(shopManager) ||
            !itemUseManager.EnterCrazyMode())
            Debug.LogWarning("[CCTVViewSwitcher] Crazy mode needs the scene's configured MoneyManager, ShopManager and ItemUseManager.", this);
    }

    [ContextMenu("測試/結束本局")]
    public void EndRound()
    {
        if (Application.isPlaying && itemUseManager != null) itemUseManager.EndRound();
    }

    private void Update()
    {
        if (state == null) return;
        state.Tick(Time.unscaledDeltaTime, ConnectedViewers());
        foreach (var entry in sessions)
        {
            ApplySessionViews(entry.Key, entry.Value);
        }
        ApplyPreview();
        if (Time.unscaledTime >= nextStatusTime)
        {
            SendStatus();
            nextStatusTime = Time.unscaledTime + 0.2f;
        }
    }

    private IEnumerable<string> ConnectedViewers()
    {
        foreach (var entry in sessions)
            if (entry.Value.views[0].sender.Transceivers.ContainsKey(entry.Key) && IsConnected(entry.Key))
                yield return entry.Key;
    }

    private void SwitchNextFor(string connectionId, int slot = 0)
    {
        if (!state.Viewers.TryGetValue(connectionId, out var viewer) || slot < 0 || slot > 1 || viewer.IndexAt(slot) < 0) return;
        for (int offset = 1; offset < views.Length; offset++)
        {
            int index = (viewer.IndexAt(slot) + offset) % views.Length;
            if (!state.Monitors[index].Overheated)
            {
                SwitchFor(connectionId, index, slot);
                return;
            }
        }
    }

    private void SwitchFor(string connectionId, int index, int slot = 0)
    {
        if (!isActiveAndEnabled || !sessions.TryGetValue(connectionId, out var session) ||
            !IsConnected(connectionId) || !state.TrySwitch(connectionId, index, slot)) return;
        state.Tick(0, ConnectedViewers());
        ApplySessionViews(connectionId, session);
        previewIndex = index;
        ApplyPreview();
        SendStatus();
    }

    private void ApplyPreview()
    {
        for (int i = 0; i < views.Length; i++)
        {
            views[i].previewCamera.enabled = i == previewIndex;
            SetBlackout(views[i].previewCamera, state.Monitors[i].Overheated);
        }
    }

    private void ApplySessionViews(string connectionId, Session session)
    {
        var viewer = state.Viewers[connectionId];
        for (int slot = 0; slot < session.views.Length; slot++)
            ApplySessionView(session.views[slot], viewer.IndexAt(slot));
    }

    private void ApplySessionView(ViewOutput session, int index)
    {
        bool active = index >= 0;
        session.sender.enabled = active;
        if (session.sender.Track != null && session.sender.Track.Enabled != active)
            session.sender.Track.Enabled = active;
        if (!active)
        {
            SetBlackout(session.camera, false);
            session.camera.enabled = false;
            return;
        }
        if (session.appliedIndex != index)
        {
            SetBlackout(session.camera, false);
            // Retain this session's render texture and video track when changing
            // viewpoint; never replace another browser's sender source.
            var texture = session.camera.targetTexture;
            session.camera.CopyFrom(views[index].streamCamera);
            session.camera.targetTexture = texture;
            var sourceData = views[index].streamCamera.GetComponent<UniversalAdditionalCameraData>();
            var outputData = session.camera.GetComponent<UniversalAdditionalCameraData>();
            if (sourceData != null)
            {
                if (outputData == null) outputData = session.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(sourceData), outputData);
            }
            else if (outputData != null) outputData.enabled = false;
            session.appliedIndex = index;
        }
        session.camera.enabled = true;
        session.camera.transform.SetPositionAndRotation(views[index].streamCamera.transform.position,
            views[index].streamCamera.transform.rotation);
        SetBlackout(session.camera, state.Monitors[index].Overheated);
    }

    private void LateUpdate()
    {
        // Follow moving physical monitors after their gameplay scripts update.
        foreach (var session in sessions.Values)
            foreach (var output in session.views)
            if (output.camera.enabled && output.appliedIndex >= 0)
            {
                var source = views[output.appliedIndex].streamCamera.transform;
                output.camera.transform.SetPositionAndRotation(source.position, source.rotation);
            }
    }

    public void OnOffer(SignalingEventData data)
    {
        if (!isActiveAndEnabled || sessions.ContainsKey(data.connectionId)) return;
        state.AddViewer(data.connectionId);
        var output = new GameObject("CCTV Viewer " + data.connectionId);
        output.SetActive(false);
        output.transform.SetParent(transform, false);
        var session = new Session { output = output, input = output.AddComponent<InputReceiver>() };
        // Copy component settings only. Instantiating the template GameObject
        // would duplicate its Signaling Manager, Broadcast and AudioListener.
        for (int slot = 0; slot < session.views.Length; slot++)
        {
            var viewObject = new GameObject("View " + (slot + 1));
            viewObject.transform.SetParent(output.transform, false);
            var view = new ViewOutput { camera = viewObject.AddComponent<Camera>(),
                sender = viewObject.AddComponent<VideoStreamSender>() };
            session.views[slot] = view;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(videoSender), view.sender);
            view.sender.source = VideoStreamSource.Camera;
            view.sender.sourceCamera = view.camera;
            // Even an initially hidden slot needs a valid capture source.
            ApplySessionView(view, state.Viewers[data.connectionId].SelectedIndex);
        }
        session.input.actions = inputActions;
        session.input.defaultActionMap = inputTemplate.defaultActionMap;
        var events = new List<PlayerInput.ActionEvent>();
        foreach (var actionEvent in inputTemplate.actionEvents)
            if (switchAction == null || actionEvent.actionId != switchAction.id.ToString()) events.Add(actionEvent);
        session.input.actionEvents = new ReadOnlyArray<PlayerInput.ActionEvent>(events.ToArray());
        sessions.Add(data.connectionId, session);
        ApplySessionViews(data.connectionId, session);
        output.SetActive(true);
        session.actions = session.input.actions;
        // Both views are available in the initial answer without another
        // connection or an extra UI switch. Audio remains a single shared track.
        foreach (var view in session.views) AddSender(data.connectionId, view.sender);
        ApplySessionViews(data.connectionId, session);
    }

    private void RememberOutput(Camera camera)
    {
        cameraOutputs[camera] = new CameraOutput {
            cullingMask = camera.cullingMask, clearFlags = camera.clearFlags,
            backgroundColor = camera.backgroundColor
        };
    }

    private void SetBlackout(Camera camera, bool blackout)
    {
        if (blackout)
        {
            if (!blackedOutCameras.Add(camera)) return;
            RememberOutput(camera);
        }
        else if (!blackedOutCameras.Remove(camera)) return;
        var output = cameraOutputs[camera];
        camera.cullingMask = blackout ? 0 : output.cullingMask;
        camera.clearFlags = blackout ? CameraClearFlags.SolidColor : output.clearFlags;
        camera.backgroundColor = blackout ? Color.black : output.backgroundColor;
    }

    // A separate JSON channel; keep the official binary "input" channel unchanged.
    public void OnAddChannel(SignalingEventData data)
    {
        if (!isActiveAndEnabled || !sessions.TryGetValue(data.connectionId, out var session)) return;
        if (data.channel.Label == "input")
        {
            session.input.SetChannel(data.connectionId, data.channel);
            return;
        }
        if (data.channel.Label != "cctv") return;
        RemoveStatusChannel(data.connectionId);
        session.channel = data.channel;
        session.onMessage = bytes =>
        {
            if (session.channel == data.channel) ReceiveCommand(data.connectionId, bytes);
        };
        session.onClose = () =>
        {
            if (session.channel == data.channel) RemoveStatusChannel(data.connectionId);
        };
        session.channel.OnMessage += session.onMessage;
        session.channel.OnClose += session.onClose;
    }

    private void ReceiveCommand(string connectionId, byte[] bytes)
    {
        if (!isActiveAndEnabled || state == null || bytes.Length > 1024 ||
            !sessions.TryGetValue(connectionId, out var session) || session.channel == null ||
            session.channel.ReadyState != RTCDataChannelState.Open) return;
        Command command;
        try { command = JsonUtility.FromJson<Command>(Encoding.UTF8.GetString(bytes)); }
        catch (ArgumentException) { return; }
        if (command == null) return;
        if (command.type == "cctv-ready")
        {
            // Data-channel open can precede the peer's Connected notification.
            // The open, session-owned channel is sufficient for this handshake.
            if (moneyManager != null && moneyManager.isActiveAndEnabled)
            {
                moneyManager.RegisterPlayer(connectionId);
                moneyManager.StartIncome(connectionId);
                SendMoneyStatus(connectionId);
            }
            if (shopManager != null && shopManager.IsConfiguredFor(moneyManager))
            {
                shopManager.RegisterPlayer(connectionId);
                SendShopStatus(connectionId, session, session.channel);
            }
            if (itemUseManager != null && itemUseManager.IsConfiguredFor(shopManager))
            {
                itemUseManager.RegisterPlayer(connectionId);
                SendItemStatus(connectionId, session, session.channel);
            }
        }
        else if (!IsConnected(connectionId)) return;
        else if (command.type == "cctv-select") SwitchFor(connectionId, command.index, command.slot);
        else if (command.type == "cctv-next") SwitchNextFor(connectionId, command.slot);
        else if (command.type == "cctv-fire") Fire(connectionId, command);
        else if (command.type == "shop-buy") Purchase(connectionId, session, command);
        else if (command.type == "item-use") UseItem(connectionId, session, command);
        SendStatus();
    }

    private void Fire(string connectionId, Command command)
    {
        var session = sessions[connectionId];
        if (session.channel == null || session.channel.ReadyState != RTCDataChannelState.Open) return;
        var result = new FireResult { slot = command.slot, index = command.index, status = "unavailable" };
        // An assigned item manager opts the scene in, including when its setup is invalid.
        // Never restore free test ammunition as a fallback for a broken item configuration.
        if (itemUseManager != null)
        {
            result.status = "item-required";
            session.channel.Send(JsonUtility.ToJson(result));
            return;
        }
        if (projectilePrefab != null && command.slot >= 0 && command.slot < session.views.Length &&
            command.x >= 0 && command.x <= 1 && command.y >= 0 && command.y <= 1 &&
            state.Viewers[connectionId].IndexAt(command.slot) == command.index &&
            command.index >= 0 && !state.Monitors[command.index].Overheated)
        {
            var output = session.views[command.slot];
            ApplySessionView(output, command.index);
            Ray ray = output.camera.ViewportPointToRay(new Vector3(command.x, command.y, 0));
            var projectile = Instantiate(projectilePrefab, ray.GetPoint(0.12f), Quaternion.LookRotation(ray.direction));
            projectile.Launch(ray.direction, (status, objectName) =>
            {
                if (this == null || !isActiveAndEnabled ||
                    !sessions.TryGetValue(connectionId, out var current) || current != session ||
                    session.channel == null || session.channel.ReadyState != RTCDataChannelState.Open) return;
                if (status == "kill") session.kills++;
                var impact = new FireResult {
                    slot = command.slot, index = command.index,
                    status = status, objectName = objectName ?? "", kills = session.kills
                };
                session.channel.Send(JsonUtility.ToJson(impact));
            });
            result.status = "launched";
        }
        session.channel.Send(JsonUtility.ToJson(result));
    }

    private void OnMoneyChanged(string connectionId, int balance)
    {
        SendMoneyStatus(connectionId);
    }

    private void SendMoneyStatus(string connectionId)
    {
        if (!isActiveAndEnabled || moneyManager == null || !moneyManager.isActiveAndEnabled ||
            !sessions.TryGetValue(connectionId, out var session) ||
            session.channel == null || session.channel.ReadyState != RTCDataChannelState.Open ||
            !moneyManager.TryGetBalance(connectionId, out int balance)) return;
        session.channel.Send(JsonUtility.ToJson(new MoneyStatusMessage { balance = balance }));
    }

    private bool IsCurrentChannel(string connectionId, Session session, RTCDataChannel channel)
    {
        return isActiveAndEnabled && sessions.TryGetValue(connectionId, out var current) &&
            current == session && channel != null && session.channel == channel &&
            channel.ReadyState == RTCDataChannelState.Open;
    }

    private void SendShopStatus(string connectionId, Session session, RTCDataChannel channel)
    {
        if (!IsCurrentChannel(connectionId, session, channel) || shopManager == null ||
            !shopManager.IsConfiguredFor(moneyManager) ||
            !shopManager.TryGetLastRequestId(connectionId, out int lastRequestId)) return;
        var message = new ShopStatusMessage {
            lastRequestId = lastRequestId,
            items = new ShopProductMessage[shopManager.ProductCount]
        };
        for (int i = 0; i < message.items.Length; i++)
        {
            var product = shopManager.GetProduct(i);
            if (!shopManager.TryGetQuantity(connectionId, product.Id, out int quantity)) return;
            message.items[i] = new ShopProductMessage {
                id = product.Id, name = product.Name, price = product.Price, quantity = quantity
            };
        }
        channel.Send(JsonUtility.ToJson(message));
    }

    private void OnItemModeChanged()
    {
        foreach (var entry in sessions)
            SendItemStatus(entry.Key, entry.Value, entry.Value.channel);
    }

    private void SendItemStatus(string connectionId, Session session, RTCDataChannel channel)
    {
        if (!IsCurrentChannel(connectionId, session, channel) || itemUseManager == null ||
            !itemUseManager.IsConfiguredFor(shopManager) ||
            !itemUseManager.TryGetLastRequestId(connectionId, out int lastRequestId)) return;
        string mode = itemUseManager.Mode == LiveStock.Economy.ItemUseState.UseMode.Crazy ? "crazy"
            : itemUseManager.Mode == LiveStock.Economy.ItemUseState.UseMode.Ended ? "ended" : "normal";
        channel.Send(JsonUtility.ToJson(new ItemStatusMessage {
            lastRequestId = lastRequestId, mode = mode, fireInterval = itemUseManager.CrazyFireInterval
        }));
    }

    private void UseItem(string connectionId, Session session, Command command)
    {
        var channel = session.channel;
        var status = LiveStock.Economy.ItemUseState.UseStatus.Unavailable;
        if (itemUseManager != null && shopManager != null &&
            shopManager.IsConfiguredFor(moneyManager) && itemUseManager.IsConfiguredFor(shopManager))
        {
            Ray ray = default;
            bool allowed = command.slot >= 0 && command.slot < session.views.Length &&
                command.index >= 0 && command.index < views.Length &&
                command.x >= 0 && command.x <= 1 && command.y >= 0 && command.y <= 1 &&
                state.Viewers.TryGetValue(connectionId, out var viewer) &&
                viewer.IndexAt(command.slot) == command.index && !state.Monitors[command.index].Overheated;
            if (allowed)
            {
                var output = session.views[command.slot];
                allowed = output != null && output.camera != null;
                if (allowed)
                {
                    ApplySessionView(output, command.index);
                    ray = output.camera.ViewportPointToRay(new Vector3(command.x, command.y, 0));
                }
            }
            status = itemUseManager.Use(connectionId, command.itemId, command.requestId,
                ray, command.slot, command.index, allowed).Status;
        }
        SendShopStatus(connectionId, session, channel);
        SendItemStatus(connectionId, session, channel);
        if (!IsCurrentChannel(connectionId, session, channel)) return;
        channel.Send(JsonUtility.ToJson(new ItemResultMessage {
            requestId = command.requestId, itemId = command.itemId,
            slot = command.slot, index = command.index,
            status = ItemStatusName(status), objectName = "", kills = session.kills
        }));
    }

    private void OnItemImpact(string connectionId, LiveStock.Items.ItemUseManager.ItemImpact impact)
    {
        if (!isActiveAndEnabled || !sessions.TryGetValue(connectionId, out var session) ||
            !IsCurrentChannel(connectionId, session, session.channel)) return;
        if (impact.Status == "kill") session.kills++;
        session.channel.Send(JsonUtility.ToJson(new ItemResultMessage {
            requestId = impact.RequestId, itemId = impact.ItemId,
            slot = impact.Slot, index = impact.Index, status = impact.Status,
            objectName = impact.ObjectName ?? "", kills = session.kills
        }));
    }

    private static string ItemStatusName(LiveStock.Economy.ItemUseState.UseStatus status)
    {
        switch (status)
        {
            case LiveStock.Economy.ItemUseState.UseStatus.Launched: return "launched";
            case LiveStock.Economy.ItemUseState.UseStatus.NoStock: return "no-stock";
            case LiveStock.Economy.ItemUseState.UseStatus.InvalidItem: return "invalid-item";
            case LiveStock.Economy.ItemUseState.UseStatus.InvalidRequest: return "invalid-request";
            case LiveStock.Economy.ItemUseState.UseStatus.UnknownPlayer: return "unknown-player";
            case LiveStock.Economy.ItemUseState.UseStatus.Cooldown: return "cooldown";
            case LiveStock.Economy.ItemUseState.UseStatus.StaleRequest: return "stale-request";
            case LiveStock.Economy.ItemUseState.UseStatus.InsufficientFunds: return "insufficient-funds";
            case LiveStock.Economy.ItemUseState.UseStatus.RoundEnded: return "round-ended";
            default: return "unavailable";
        }
    }

    private void Purchase(string connectionId, Session session, Command command)
    {
        var channel = session.channel;
        bool purchasingAllowed = itemUseManager == null ||
            itemUseManager.Mode == LiveStock.Economy.ItemUseState.UseMode.Normal;
        var result = purchasingAllowed && shopManager != null && shopManager.IsConfiguredFor(moneyManager)
            ? shopManager.Purchase(connectionId, command.itemId, command.requestId)
            : new LiveStock.Economy.ShopState.PurchaseResult(command.requestId, command.itemId,
                LiveStock.Economy.ShopState.PurchaseStatus.Unavailable);
        SendShopStatus(connectionId, session, channel);
        if (!IsCurrentChannel(connectionId, session, channel)) return;
        channel.Send(JsonUtility.ToJson(new ShopResultMessage {
            requestId = result.RequestId, itemId = result.ItemId, status = ShopStatusName(result.Status)
        }));
    }

    private static string ShopStatusName(LiveStock.Economy.ShopState.PurchaseStatus status)
    {
        switch (status)
        {
            case LiveStock.Economy.ShopState.PurchaseStatus.Success: return "success";
            case LiveStock.Economy.ShopState.PurchaseStatus.InsufficientFunds: return "insufficient-funds";
            case LiveStock.Economy.ShopState.PurchaseStatus.InvalidItem: return "invalid-item";
            case LiveStock.Economy.ShopState.PurchaseStatus.InvalidRequest: return "invalid-request";
            case LiveStock.Economy.ShopState.PurchaseStatus.UnknownPlayer: return "unknown-player";
            case LiveStock.Economy.ShopState.PurchaseStatus.InventoryFull: return "inventory-full";
            case LiveStock.Economy.ShopState.PurchaseStatus.StaleRequest: return "stale-request";
            default: return "unavailable";
        }
    }

    private void SendStatus()
    {
        if (state == null) return;
        var message = new StatusMessage {
            heatLimit = state.HeatLimit, cooldownDuration = switchCooldown,
            monitors = new MonitorMessage[views.Length]
        };
        for (int i = 0; i < views.Length; i++)
        {
            var monitor = state.Monitors[i];
            message.monitors[i] = new MonitorMessage {
                name = string.IsNullOrWhiteSpace(views[i].displayName) ? $"CCTV {i + 1:00}" : views[i].displayName,
                status = monitor.Overheated ? "overheated" : monitor.Viewers > 0 ? "watching" : "ready",
                heat = monitor.Heat, recoveryRemaining = monitor.RecoveryRemaining, viewers = monitor.Viewers
            };
        }
        foreach (var entry in sessions)
        {
            if (entry.Value.channel == null || entry.Value.channel.ReadyState != RTCDataChannelState.Open) continue;
            var viewer = state.Viewers[entry.Key];
            message.selectedIndex = viewer.SelectedIndex;
            // DEMO TEMPORARY: 保留陣列格式相容前端，但第二槽固定為 -1。
            message.selectedIndices = new[] { viewer.SelectedIndex, -1 };
            message.videoMids = new string[entry.Value.views.Length];
            for (int slot = 0; slot < message.videoMids.Length; slot++)
                if (entry.Value.views[slot].sender.Transceivers.TryGetValue(entry.Key, out var transceiver))
                    message.videoMids[slot] = transceiver.Mid;
            message.cooldownRemaining = viewer.CooldownRemaining;
            entry.Value.channel.Send(JsonUtility.ToJson(message));
        }
    }

    public void OnDisconnect(SignalingEventData data) => RemoveSession(data.connectionId);
    public void OnDeletedConnection(SignalingEventData data) => RemoveSession(data.connectionId);

    private void RemoveStatusChannel(string connectionId)
    {
        if (itemUseManager != null) itemUseManager.RemovePlayer(connectionId);
        if (shopManager != null) shopManager.RemovePlayer(connectionId);
        if (moneyManager != null) moneyManager.RemovePlayer(connectionId);
        if (!sessions.TryGetValue(connectionId, out var entry) || entry.channel == null) return;
        entry.channel.OnMessage -= entry.onMessage;
        entry.channel.OnClose -= entry.onClose;
        entry.channel = null;
    }

    private void RemoveSession(string connectionId, bool removeSender = true)
    {
        RemoveStatusChannel(connectionId);
        if (!sessions.TryGetValue(connectionId, out var session)) return;
        sessions.Remove(connectionId);
        state.RemoveViewer(connectionId);
        foreach (var view in session.views)
        {
            if (removeSender && view.sender != null && view.sender.Transceivers.ContainsKey(connectionId))
                RemoveSender(connectionId, view.sender);
            if (view.camera != null)
            {
                SetBlackout(view.camera, false);
                cameraOutputs.Remove(view.camera);
            }
        }
        if (session.input != null) session.input.SetChannel(connectionId, null);
        // Restore/discard this private output without touching another viewer's camera.
        if (session.output != null)
        {
            session.output.SetActive(false);
            Destroy(session.output);
        }
        if (session.actions != null) Destroy(session.actions);
    }

    private void OnDestroy()
    {
        if (moneyManager != null) moneyManager.BalanceChanged -= OnMoneyChanged;
        if (itemUseManager != null) itemUseManager.Impact -= OnItemImpact;
        if (itemUseManager != null) itemUseManager.ModeChanged -= OnItemModeChanged;
        foreach (var connectionId in new List<string>(sessions.Keys)) RemoveSession(connectionId, false);
        if (registered && signalingManager != null) signalingManager.RemoveSignalingHandler(this);
        if (registered && sourceBroadcast != null)
        {
            sourceBroadcast.AddComponent(videoSender);
            sourceBroadcast.AddComponent(inputTemplate);
        }
        if (registered && videoSender != null) videoSender.enabled = senderWasEnabled;
        if (registered && inputTemplate != null) inputTemplate.enabled = inputWasEnabled;
        foreach (var camera in cameraOutputs.Keys)
            if (camera != null) SetBlackout(camera, false);
    }
}
