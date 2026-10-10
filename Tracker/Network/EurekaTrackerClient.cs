using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EurekaAggro.Tracker.Model;
using EurekaAggro.Tracker.Zones;
using Newtonsoft.Json.Linq;

namespace EurekaAggro.Tracker.Network;

/// <summary>
/// Asynchronous Phoenix WebSocket and REST API client for ffxiv-eureka.com.
/// Synchronizes NM kill/pop timers, instance information, password protection, and live viewer counts.
/// </summary>
public class EurekaTrackerClient : IDisposable
{
    private const string TrackerWebSocketUrl = "wss://ffxiv-eureka.com/socket/websocket?vsn=2.0.0";
    private const string TrackerApiUrl = "https://ffxiv-eureka.com/api/instances";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private ClientWebSocket? webSocket;
    private CancellationTokenSource? cts;
    private int messageId;
    private int lastHeartbeatId = -1;
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

    public bool CanModify => !string.IsNullOrWhiteSpace(TrackerPassword);

    /// <summary>
    /// Fetches all active public trackers for a given data center from ffxiv-eureka.com.
    /// Connects via Phoenix WebSocket to datacenter:{dataCenterId} and collects the initial_payload.
    /// </summary>
    public static async Task<List<PublicTrackerInfo>> FetchPublicTrackersAsync(int dataCenterId, CancellationToken cancellationToken = default)
    {
        var results = new List<PublicTrackerInfo>();
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

                            results.Add(new PublicTrackerInfo
                            {
                                TrackerId = id,
                                ZoneId = zoneId,
                                InstanceId = instanceId,
                                CreatedAt = createdAt,
                                UpdatedAt = updatedAt,
                            });
                        }

                        break;
                    }
                }
            }

            if (clientWs.State == WebSocketState.Open)
            {
                await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EurekaAggroPlugin.PluginLog.Debug($"FetchPublicTrackersAsync for DC {dataCenterId}: {ex.Message}");
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
            EurekaAggroPlugin.PluginLog.Error(ex, "Failed to create new tracker via REST API.");
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
            EurekaAggroPlugin.PluginLog.Error(ex, "Failed to export tracker via REST API.");
            return (string.Empty, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// Connects to a tracker by ID and optional password.
    /// </summary>
    public async Task<bool> JoinTrackerAsync(string trackerId, string password = "")
    {
        if (IsConnected)
        {
            await DisconnectAsync();
        }

        ErrorMessage = null;
        IsInvalid = false;
        TrackerId = trackerId.Trim();
        TrackerPassword = password.Trim();

        try
        {
            cts = new CancellationTokenSource();
            webSocket = new ClientWebSocket();
            joinTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await webSocket.ConnectAsync(new Uri(TrackerWebSocketUrl), cts.Token);
            _ = ReceiveLoop(cts.Token);

            // Send phx_join
            var joinPayload = string.IsNullOrWhiteSpace(TrackerPassword)
                ? new JObject()
                : new JObject { ["password"] = TrackerPassword };

            EurekaTrackerMessage joinMsg = new(
                setJoinRef: true,
                messageId: ++messageId,
                channel: $"instance:{TrackerId}",
                @event: "phx_join",
                payload: joinPayload);

            await SendRawAsync(joinMsg.ToMessage(), cts.Token);

            // Start 30s heartbeat loop
            _ = HeartbeatLoop(cts.Token);

            // Wait for initial_payload or rejection from ffxiv-eureka.com (up to 8s timeout)
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using (timeoutCts.Token.Register(() => joinTcs?.TrySetResult(false)))
            {
                bool joined = await joinTcs.Task;
                return joined && IsConnected;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection error: {ex.Message}";
            EurekaAggroPlugin.PluginLog.Error(ex, $"Failed to connect to tracker {trackerId}");
            await DisconnectAsync();
            return false;
        }
    }

    private async Task HeartbeatLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                if (!IsConnected && webSocket?.State != WebSocketState.Open)
                    break;

                EurekaTrackerMessage heartbeat = new(
                    setJoinRef: false,
                    messageId: ++messageId,
                    channel: "phoenix",
                    @event: "heartbeat",
                    payload: new JObject());

                lastHeartbeatId = messageId;
                await SendRawAsync(heartbeat.ToMessage(), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EurekaAggroPlugin.PluginLog.Debug($"Heartbeat loop terminated: {ex.Message}");
        }
    }

    private async Task ReceiveLoop(CancellationToken token)
    {
        var buffer = new byte[4096];
        var segment = new ArraySegment<byte>(buffer);

        try
        {
            while (!token.IsCancellationRequested && webSocket?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(segment, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await DisconnectAsync();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                string jsonText = await reader.ReadToEndAsync(token);

                ProcessMessage(jsonText);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EurekaAggroPlugin.PluginLog.Debug($"ReceiveLoop disconnected: {ex.Message}");
            if (IsConnected)
            {
                ErrorMessage = "Connection closed unexpectedly.";
                await DisconnectAsync();
            }
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
            EurekaAggroPlugin.PluginLog.Error(ex, "Error processing incoming Phoenix message.");
        }
    }

    private void HandleReply(int msgRef, JObject? payload)
    {
        if (payload == null) return;

        string status = (string?)payload["status"] ?? string.Empty;
        if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            var response = payload["response"];
            string reason = response?.Type == JTokenType.String ? (string)response : (string?)response?["reason"] ?? "Unknown rejection";
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
            _ = DisconnectAsync();
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
            EurekaAggroPlugin.PluginLog.Error(ex, "Failed to parse notorious monsters JSON payload.");
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

        EurekaTrackerMessage msg = new(
            setJoinRef: true,
            messageId: ++messageId,
            channel: $"instance:{TrackerId}",
            @event: "set_kill_time",
            payload: payload);

        await SendRawAsync(msg.ToMessage());
    }

    public async Task ResetPopAsync(ushort trackerId)
    {
        if (!IsConnected) return;

        var payload = new JObject
        {
            ["id"] = trackerId,
        };

        EurekaTrackerMessage msg = new(
            setJoinRef: true,
            messageId: ++messageId,
            channel: $"instance:{TrackerId}",
            @event: "reset_kill",
            payload: payload);

        await SendRawAsync(msg.ToMessage());
    }

    public async Task ResetAllAsync()
    {
        if (!IsConnected) return;

        EurekaTrackerMessage msg = new(
            setJoinRef: true,
            messageId: ++messageId,
            channel: $"instance:{TrackerId}",
            @event: "reset_all",
            payload: new JObject());

        await SendRawAsync(msg.ToMessage());
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

        EurekaTrackerMessage msg = new(
            setJoinRef: true,
            messageId: ++messageId,
            channel: $"instance:{TrackerId}",
            @event: "set_instance_information",
            payload: payload);

        await SendRawAsync(msg.ToMessage());
        OnTrackerUpdated?.Invoke();
    }

    public async Task SetPasswordAsync(string password)
    {
        if (!IsConnected) return;

        var payload = new JObject
        {
            ["password"] = password.Trim(),
        };

        EurekaTrackerMessage msg = new(
            setJoinRef: true,
            messageId: ++messageId,
            channel: $"instance:{TrackerId}",
            @event: "set_password",
            payload: payload);

        await SendRawAsync(msg.ToMessage());
    }

    private async Task SendRawAsync(string message, CancellationToken token = default)
    {
        if (webSocket?.State != WebSocketState.Open) return;

        var bytes = Encoding.UTF8.GetBytes(message);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        joinTcs?.TrySetResult(false);
        cts?.Cancel();

        try
        {
            if (webSocket != null && webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnecting", CancellationToken.None);
            }
        }
        catch { }
        finally
        {
            webSocket?.Dispose();
            webSocket = null;
            cts?.Dispose();
            cts = null;
        }

        ActiveTracker = null;
        IsPublic = false;
        Viewers = 0;
        OnConnectionStatusChanged?.Invoke();
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
}
