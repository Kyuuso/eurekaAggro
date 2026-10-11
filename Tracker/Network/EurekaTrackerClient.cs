using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EurekaSuite.Tracker.Model;
using EurekaSuite.Tracker.Zones;
using Newtonsoft.Json.Linq;

namespace EurekaSuite.Tracker.Network;

/// <summary>
/// Asynchronous Phoenix WebSocket and REST API client for ffxiv-eureka.com.
/// Synchronizes NM kill/pop timers, instance information, password protection, and live viewer counts.
/// </summary>
public class EurekaTrackerClient : IDisposable
{
    private const string TrackerWebSocketUrl = "wss://ffxiv-eureka.com/socket/websocket?vsn=2.0.0";
    private const string TrackerApiUrl = "https://ffxiv-eureka.com/api/instances";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Serializes connect/disconnect so only one session owns webSocket/cts at a time.
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    // ClientWebSocket does not allow concurrent SendAsync calls.
    private readonly SemaphoreSlim sendLock = new(1, 1);
    // Event names of in-flight channel requests keyed by message ref, used to label rejected replies.
    private readonly ConcurrentDictionary<int, string> pendingRequests = new();

    private ClientWebSocket? webSocket;
    private CancellationTokenSource? cts;
    private int messageId;
    private int lastHeartbeatId = -1;
    private int joinMessageId = -1;
    // Incremented on every connect and disconnect; loops bound to an older value are stale.
    private int sessionId;
    private TaskCompletionSource<bool>? joinTcs;

    public bool IsConnected { get; private set; }
    public bool IsInvalid { get; private set; }
    public string TrackerId { get; private set; } = string.Empty;
    public string TrackerPassword { get; private set; } = string.Empty;
    public string InstanceId { get; private set; } = string.Empty;
    public int? DataCenterId { get; private set; }
    public bool IsPublic { get; private set; }
    public int Viewers { get; private set; }
    public string? ErrorMessage { get; private set; }

    public IEurekaZoneTracker? ActiveTracker { get; private set; }

    public event Action? OnTrackerUpdated;
    public event Action? OnConnectionStatusChanged;

    /// <summary>
    /// Raised from the network thread when the server rejects a channel request other than the join
    /// (for example set_kill_time with a wrong password). Arguments: event name, rejection reason.
    /// </summary>
    public event Action<string, string>? OnRequestRejected;

    public bool CanModify => !string.IsNullOrWhiteSpace(TrackerPassword);

    /// <summary>
    /// Fetches all active public trackers for a given data center from ffxiv-eureka.com.
    /// Connects via Phoenix WebSocket to datacenter:{dataCenterId} and collects the initial_payload.
    /// Returns null if the directory could not be reached or did not answer in time, so callers can
    /// tell a failure apart from a data center that has no public trackers.
    /// </summary>
    public static async Task<List<PublicTrackerInfo>?> FetchPublicTrackersAsync(int dataCenterId, CancellationToken cancellationToken = default)
    {
        var results = new List<PublicTrackerInfo>();
        bool received = false;
        using var clientWs = new ClientWebSocket();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await clientWs.ConnectAsync(new Uri(TrackerWebSocketUrl), linkedCts.Token);

            var joinMsg = new EurekaTrackerMessage(
                setJoinRef: true,
                messageId: 1,
                channel: $"datacenter:{dataCenterId}",
                @event: "phx_join",
                payload: new JObject());

            var sendBytes = Encoding.UTF8.GetBytes(joinMsg.ToMessage());
            await clientWs.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, linkedCts.Token);

            var buffer = new byte[8192];
            var segment = new ArraySegment<byte>(buffer);

            while (!linkedCts.Token.IsCancellationRequested && clientWs.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult recvResult;
                do
                {
                    recvResult = await clientWs.ReceiveAsync(segment, linkedCts.Token);
                    if (recvResult.MessageType == WebSocketMessageType.Close) break;
                    ms.Write(buffer, 0, recvResult.Count);
                } while (!recvResult.EndOfMessage);

                if (recvResult.MessageType == WebSocketMessageType.Close) break;

                ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                string jsonText = await reader.ReadToEndAsync(linkedCts.Token);

                var messageArray = JArray.Parse(jsonText);
                if (messageArray.Count >= 5)
                {
                    string @event = (string?)messageArray[3] ?? string.Empty;
                    var payload = messageArray[4] as JObject;

                    if (@event == "initial_payload" && payload?["data"] is JArray dataArr)
                    {
                        foreach (var token in dataArr)
                        {
                            if (token is not JObject item) continue;

                            string id = (string?)item["id"] ?? string.Empty;
                            if (string.IsNullOrEmpty(id)) continue;

                            var attrs = item["attributes"] as JObject;
                            int zoneId = (int?)item["relationships"]?["zone"]?["data"]?["id"] ?? 0;

                            string? instanceId = null;
                            var instToken = attrs?["instance-id"];
                            if (instToken != null && instToken.Type != JTokenType.Null)
                            {
                                instanceId = instToken.ToString().Trim();
                            }

                            DateTimeOffset? createdAt = null;
                            if (DateTimeOffset.TryParse((string?)attrs?["created-at"], out var dtCreated))
                                createdAt = dtCreated;

                            DateTimeOffset? updatedAt = null;
                            if (DateTimeOffset.TryParse((string?)attrs?["updated-at"], out var dtUpdated))
                                updatedAt = dtUpdated;

                            int poppedCount = 0;
                            var nmToken = attrs?["notorious-monsters"];
                            if (nmToken != null)
                            {
                                try
                                {
                                    JObject? nmObj = nmToken.Type == JTokenType.String
                                        ? JObject.Parse((string)nmToken!)
                                        : nmToken as JObject;
                                    if (nmObj != null)
                                    {
                                        foreach (var prop in nmObj.Properties())
                                        {
                                            if (long.TryParse(prop.Value?.ToString(), out var time) && time > 0)
                                            {
                                                poppedCount++;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }

                            results.Add(new PublicTrackerInfo
                            {
                                TrackerId = id,
                                ZoneId = zoneId,
                                InstanceId = instanceId,
                                CreatedAt = createdAt,
                                UpdatedAt = updatedAt,
                                PoppedCount = poppedCount,
                            });
                        }

                        received = true;
                        break;
                    }
                }
            }

            if (clientWs.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", closeCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Debug($"FetchPublicTrackersAsync for DC {dataCenterId}: {ex.Message}");
        }

        if (!received)
        {
            EurekaSuitePlugin.PluginLog.Debug($"FetchPublicTrackersAsync for DC {dataCenterId}: no directory payload received.");
            return null;
        }

        return results;
    }

    /// <summary>
    /// Creates a brand new tracker via ffxiv-eureka.com REST API.
    /// Zone IDs: 1 = Anemos, 2 = Pagos, 3 = Pyros, 4 = Hydatos.
    /// </summary>
    public static async Task<(string trackerId, string password, string? error)> CreateTrackerAsync(int zoneId)
    {
        try
        {
            var payload = new JObject
            {
                ["data"] = new JObject
                {
                    ["type"] = "instances",
                    ["attributes"] = new JObject
                    {
                        ["zone-id"] = zoneId,
                    },
                },
            };

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(TrackerApiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                return (string.Empty, string.Empty, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var jsonStr = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonStr);

            string trackerId = (string?)json["data"]?["id"] ?? string.Empty;
            string password = (string?)json["data"]?["attributes"]?["password"] ?? string.Empty;

            return (trackerId, password, null);
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Failed to create new tracker via REST API.");
            return (string.Empty, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Exports and clones an existing tracker into a fresh instance.
    /// </summary>
    public static async Task<(string trackerId, string password, string? error)> ExportTrackerAsync(string oldTrackerId)
    {
        try
        {
            var payload = new JObject
            {
                ["data"] = new JObject
                {
                    ["type"] = "instances",
                    ["attributes"] = new JObject
                    {
                        ["copy-from"] = oldTrackerId,
                    },
                },
            };

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await HttpClient.PostAsync(TrackerApiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                return (string.Empty, string.Empty, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
            }

            var jsonStr = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonStr);

            string trackerId = (string?)json["data"]?["id"] ?? string.Empty;
            string password = (string?)json["data"]?["attributes"]?["password"] ?? string.Empty;

            return (trackerId, password, null);
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Failed to export tracker via REST API.");
            return (string.Empty, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Connects to a tracker by ID and optional password.
    /// </summary>
    public async Task<bool> JoinTrackerAsync(string trackerId, string password = "")
    {
        await connectionLock.WaitAsync();
        try
        {
            // Always tear down the previous session (connected, joining, or half-open) first.
            await DisconnectCoreAsync();

            ErrorMessage = null;
            IsInvalid = false;
            TrackerId = trackerId.Trim();
            TrackerPassword = password.Trim();

            var sessionCts = new CancellationTokenSource();
            var socket = new ClientWebSocket();
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int session = Interlocked.Increment(ref sessionId);

            cts = sessionCts;
            webSocket = socket;
            joinTcs = tcs;

            try
            {
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(new Uri(TrackerWebSocketUrl), connectCts.Token);
                }

                _ = ReceiveLoop(socket, session, sessionCts.Token);

                // Send phx_join
                var joinPayload = string.IsNullOrWhiteSpace(TrackerPassword)
                    ? new JObject()
                    : new JObject { ["password"] = TrackerPassword };

                int joinRef = Interlocked.Increment(ref messageId);
                joinMessageId = joinRef;

                EurekaTrackerMessage joinMsg = new(
                    setJoinRef: true,
                    messageId: joinRef,
                    channel: $"instance:{TrackerId}",
                    @event: "phx_join",
                    payload: joinPayload);

                await SendRawAsync(joinMsg.ToMessage(), socket, sessionCts.Token);

                // Start 30s heartbeat loop
                _ = HeartbeatLoop(socket, sessionCts.Token);

                // Wait for initial_payload or rejection from ffxiv-eureka.com (up to 8s timeout)
                bool joined;
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
                using (sessionCts.Token.Register(() => tcs.TrySetResult(false)))
                {
                    joined = await tcs.Task;
                }

                if (joined && IsConnected)
                    return true;

                if (string.IsNullOrEmpty(ErrorMessage))
                    ErrorMessage = "Timed out waiting for the tracker to respond.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Connection error: {ex.Message}";
                EurekaSuitePlugin.PluginLog.Error(ex, $"Failed to connect to tracker {trackerId}");
            }

            // A failed or timed-out join must not leave the socket and its loops running.
            await DisconnectCoreAsync();
            return false;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task HeartbeatLoop(ClientWebSocket socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                if (socket.State != WebSocketState.Open)
                    break;

                int heartbeatRef = Interlocked.Increment(ref messageId);
                EurekaTrackerMessage heartbeat = new(
                    setJoinRef: false,
                    messageId: heartbeatRef,
                    channel: "phoenix",
                    @event: "heartbeat",
                    payload: new JObject());

                lastHeartbeatId = heartbeatRef;
                await SendRawAsync(heartbeat.ToMessage(), socket, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Debug($"Heartbeat loop terminated: {ex.Message}");
        }
    }

    private async Task ReceiveLoop(ClientWebSocket socket, int session, CancellationToken token)
    {
        var buffer = new byte[4096];
        var segment = new ArraySegment<byte>(buffer);

        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(segment, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (IsCurrentSession(session))
                        {
                            ErrorMessage = "Tracker closed the connection.";
                            joinTcs?.TrySetResult(false);
                            await DisconnectSessionAsync(session);
                        }
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                string jsonText = await reader.ReadToEndAsync(token);

                // A message still in flight on a replaced socket must not touch the new session's state.
                if (!IsCurrentSession(session)) return;

                ProcessMessage(jsonText);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Debug($"ReceiveLoop disconnected: {ex.Message}");
            if (IsCurrentSession(session))
            {
                ErrorMessage = "Connection closed unexpectedly.";
                joinTcs?.TrySetResult(false);
                await DisconnectSessionAsync(session);
            }
        }
    }

    private bool IsCurrentSession(int session) => Volatile.Read(ref sessionId) == session;

    /// <summary>
    /// Disconnects only if the given session is still the active one, so a stale loop can never
    /// tear down a newer connection.
    /// </summary>
    private async Task DisconnectSessionAsync(int session)
    {
        await connectionLock.WaitAsync();
        try
        {
            if (IsCurrentSession(session))
            {
                await DisconnectCoreAsync();
            }
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private void ProcessMessage(string jsonText)
    {
        try
        {
            var messageArray = JArray.Parse(jsonText);
            if (messageArray.Count < 5) return;

            int msgRef = messageArray[1].Type == JTokenType.Null ? -1 : (int)messageArray[1];
            string channel = (string?)messageArray[2] ?? string.Empty;
            string @event = (string?)messageArray[3] ?? string.Empty;
            var payload = messageArray[4] as JObject;

            switch (@event)
            {
                case "phx_reply":
                    HandleReply(msgRef, payload);
                    break;

                case "presence_state":
                    if (payload != null)
                    {
                        Viewers = payload.Count;
                        OnTrackerUpdated?.Invoke();
                    }
                    break;

                case "presence_diff":
                    if (payload != null)
                    {
                        int joins = (payload["joins"] as JObject)?.Count ?? 0;
                        int leaves = (payload["leaves"] as JObject)?.Count ?? 0;
                        Viewers = Math.Max(1, Viewers + joins - leaves);
                        OnTrackerUpdated?.Invoke();
                    }
                    break;

                case "initial_payload":
                    HandleInitialPayload(payload);
                    break;

                case "payload":
                    HandlePayloadUpdate(payload);
                    break;

                case "password_set":
                    if (payload?["success"]?.Value<bool>() == true)
                    {
                        TrackerPassword = (string?)payload["password"] ?? string.Empty;
                    }
                    OnTrackerUpdated?.Invoke();
                    break;
            }
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Error processing incoming Phoenix message.");
        }
    }

    private void HandleReply(int msgRef, JObject? payload)
    {
        if (payload == null) return;

        pendingRequests.TryRemove(msgRef, out var requestEvent);

        string status = (string?)payload["status"] ?? string.Empty;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var response = payload["response"];
            string reason = response?.Type == JTokenType.String ? (string)response : (string?)response?["reason"] ?? "Unknown rejection";

            if (msgRef == joinMessageId)
            {
                // Only a rejected join ends the session; JoinTrackerAsync tears the socket down.
                if (reason.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                {
                    IsInvalid = true;
                    ErrorMessage = "Tracker does not exist.";
                }
                else
                {
                    ErrorMessage = $"Tracker error: {reason}";
                }

                joinTcs?.TrySetResult(false);
                return;
            }

            // Any other rejected request (wrong edit password, invalid NM id) keeps the connection open.
            string eventName = requestEvent ?? "request";
            EurekaSuitePlugin.PluginLog.Warning($"Tracker rejected {eventName}: {reason}");
            OnRequestRejected?.Invoke(eventName, reason);
            return;
        }

        if (msgRef == lastHeartbeatId)
        {
            lastHeartbeatId = -1;
        }
    }

    private void HandleInitialPayload(JObject? payload)
    {
        if (payload?["data"] == null) return;

        var data = payload["data"];
        if (data is JArray) return;

        int zoneId = (int?)data["relationships"]?["zone"]?["data"]?["id"] ?? 1;
        SetZone(zoneId);

        TrackerId = (string?)data["id"] ?? TrackerId;

        var attrs = data["attributes"];
        if (attrs != null)
        {
            var pwd = (string?)attrs["password"];
            if (!string.IsNullOrWhiteSpace(pwd))
            {
                TrackerPassword = pwd;
            }

            // Instance ID (e.g. "60")
            InstanceId = (string?)attrs["instance-id"] ?? string.Empty;

            // Data Center ID
            if (attrs["data-center-id"] != null && attrs["data-center-id"]!.Type != JTokenType.Null)
            {
                DataCenterId = (int?)attrs["data-center-id"];
                IsPublic = true;
            }
            else
            {
                DataCenterId = null;
                IsPublic = false;
            }

            // Pop / Kill times
            ApplyNotoriousMonsters(attrs["notorious-monsters"]);
        }

        IsConnected = true;
        IsInvalid = false;
        ErrorMessage = null;

        joinTcs?.TrySetResult(true);

        OnConnectionStatusChanged?.Invoke();
        OnTrackerUpdated?.Invoke();
    }

    private void HandlePayloadUpdate(JObject? payload)
    {
        if (payload?["data"]?["attributes"] == null) return;

        var attrs = payload["data"]!["attributes"]!;

        // Update NM Pop times
        if (attrs["notorious-monsters"] != null)
        {
            ApplyNotoriousMonsters(attrs["notorious-monsters"]);
        }

        // Update Instance ID
        if (attrs["instance-id"] != null)
        {
            InstanceId = (string?)attrs["instance-id"] ?? string.Empty;
        }

        // Update Visibility / Data Center
        if (attrs["data-center-id"] != null)
        {
            if (attrs["data-center-id"]!.Type != JTokenType.Null)
            {
                DataCenterId = (int?)attrs["data-center-id"];
                IsPublic = true;
            }
            else
            {
                DataCenterId = null;
                IsPublic = false;
            }
        }

        OnTrackerUpdated?.Invoke();
    }

    private void ApplyNotoriousMonsters(JToken? token)
    {
        if (token == null || ActiveTracker == null) return;

        try
        {
            JObject nmObj;
            if (token.Type == JTokenType.String)
                nmObj = JObject.Parse((string)token!);
            else if (token is JObject obj)
                nmObj = obj;
            else
                return;

            Dictionary<ushort, long> kills = new();
            foreach (var prop in nmObj.Properties())
            {
                if (ushort.TryParse(prop.Name, out var id) && long.TryParse(prop.Value.ToString(), out var time))
                {
                    kills[id] = time;
                }
            }

            ActiveTracker.SetPopTimes(kills);
        }
        catch (Exception ex)
        {
            EurekaSuitePlugin.PluginLog.Error(ex, "Failed to parse notorious monsters JSON payload.");
        }
    }

    private void SetZone(int zoneId)
    {
        ActiveTracker = zoneId switch
        {
            1 => new AnemosTracker(),
            2 => new PagosTracker(),
            3 => new PyrosTracker(),
            4 => new HydatosTracker(),
            _ => new HydatosTracker(),
        };
    }

    public async Task SetPopTimeAsync(ushort trackerId, long killTimeMs)
    {
        if (!IsConnected) return;

        var payload = new JObject
        {
            ["id"] = trackerId,
            ["time"] = killTimeMs,
        };

        await SendRequestAsync("set_kill_time", payload);
    }

    public async Task ResetPopAsync(ushort trackerId)
    {
        if (!IsConnected) return;

        var payload = new JObject
        {
            ["id"] = trackerId,
        };

        await SendRequestAsync("reset_kill", payload);
    }

    public async Task ResetAllAsync()
    {
        if (!IsConnected) return;

        await SendRequestAsync("reset_all", new JObject());
    }

    /// <summary>
    /// Updates the instance ID and datacenter visibility on the ffxiv-eureka.com tracker.
    /// If instanceId is empty or null, it clears the instance ID on the tracker.
    /// If dataCenterId is null or -1, tracker becomes private.
    /// </summary>
    public async Task SetInstanceInformationAsync(string? instanceId, int? dataCenterId)
    {
        if (!IsConnected) return;

        string? cleanId = string.IsNullOrWhiteSpace(instanceId) ? null : instanceId.Trim();
        InstanceId = cleanId ?? string.Empty;
        if (dataCenterId.HasValue && dataCenterId.Value > 0)
        {
            DataCenterId = dataCenterId.Value;
            IsPublic = true;
        }

        var payload = new JObject
        {
            ["instance_id"] = cleanId,
            ["data_center_id"] = dataCenterId.HasValue && dataCenterId.Value > 0 ? dataCenterId.Value : null,
        };

        await SendRequestAsync("set_instance_information", payload);
        OnTrackerUpdated?.Invoke();
    }

    public async Task SetPasswordAsync(string password)
    {
        if (!IsConnected) return;

        var payload = new JObject
        {
            ["password"] = password.Trim(),
        };

        await SendRequestAsync("set_password", payload);
    }

    /// <summary>
    /// Sends a channel request on the current session and records its ref so a rejection can be reported.
    /// </summary>
    private async Task SendRequestAsync(string @event, JObject payload)
    {
        var socket = webSocket;
        var token = cts?.Token ?? CancellationToken.None;
        if (socket == null) return;

        int requestRef = Interlocked.Increment(ref messageId);
        pendingRequests[requestRef] = @event;

        EurekaTrackerMessage msg = new(
            setJoinRef: true,
            messageId: requestRef,
            channel: $"instance:{TrackerId}",
            @event: @event,
            payload: payload);

        try
        {
            await SendRawAsync(msg.ToMessage(), socket, token);
        }
        catch (Exception ex)
        {
            pendingRequests.TryRemove(requestRef, out _);
            EurekaSuitePlugin.PluginLog.Warning($"Failed to send {@event} to tracker: {ex.Message}");
        }
    }

    private async Task SendRawAsync(string message, ClientWebSocket socket, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await sendLock.WaitAsync(token);
        try
        {
            if (socket.State != WebSocketState.Open) return;
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
        finally
        {
            sendLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await connectionLock.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
        }
        finally
        {
            connectionLock.Release();
        }
    }

    /// <summary>
    /// Tears down the active session. Callers must hold connectionLock.
    /// </summary>
    private async Task DisconnectCoreAsync()
    {
        // Invalidate the session first so its loops stop touching shared state
        Interlocked.Increment(ref sessionId);

        bool hadSession = webSocket != null || IsConnected;
        var socket = webSocket;
        var sessionCts = cts;
        webSocket = null;
        cts = null;

        IsConnected = false;
        joinTcs?.TrySetResult(false);
        joinTcs = null;
        joinMessageId = -1;
        pendingRequests.Clear();

        if (socket != null)
        {
            try
            {
                // CloseOutputAsync does not wait for a receive, so it cannot collide with the pending ReceiveAsync
                if (socket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "disconnecting", closeCts.Token);
                }
            }
            catch { }
        }

        try
        {
            sessionCts?.Cancel();
        }
        catch { }

        socket?.Dispose();
        sessionCts?.Dispose();

        ActiveTracker = null;
        IsPublic = false;
        Viewers = 0;

        if (hadSession)
        {
            OnConnectionStatusChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}

/// <summary>
/// Model representing a publicly listed tracker on a given data center.
/// </summary>
public class PublicTrackerInfo
{
    public string TrackerId { get; set; } = string.Empty;
    public int ZoneId { get; set; }
    public string? InstanceId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int PoppedCount { get; set; }

    public string GetAgeString()
    {
        var time = UpdatedAt ?? CreatedAt;
        if (!time.HasValue) return string.Empty;

        var elapsed = DateTimeOffset.UtcNow - time.Value.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return $"{(int)elapsed.TotalDays}d ago";
    }

    public string GetTimeActiveString()
    {
        if (!CreatedAt.HasValue) return "-";
        var elapsed = DateTimeOffset.UtcNow - CreatedAt.Value.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return "0m";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h";
        return $"{(int)elapsed.TotalDays}d";
    }
}
