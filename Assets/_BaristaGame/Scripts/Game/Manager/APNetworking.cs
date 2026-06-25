using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class APNetworking : MonoBehaviour
{
    public static APNetworking Instance { get; private set; }

    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _receiveTask;
    private Queue<string> _messageQueue = new Queue<string>();
    private bool _isConnecting = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public async Task<bool> ConnectAsync(string serverUrl)
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            Debug.LogWarning("[AP Network] Already connected");
            return true;
        }

        _isConnecting = true;

        try
        {
            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            // Public servers (e.g. archipelago.gg) require WSS; use plain WS only for localhost.
            if (!serverUrl.StartsWith("ws://") && !serverUrl.StartsWith("wss://"))
            {
                string host = serverUrl.Split(':')[0];
                bool isLocal = host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0";
                serverUrl = (isLocal ? "ws://" : "wss://") + serverUrl;
            }

            Uri serverUri = new Uri(serverUrl);
            await _webSocket.ConnectAsync(serverUri, _cancellationTokenSource.Token);

            Debug.Log("[AP Network] Connected to Archipelago server");
            _isConnecting = false;

            // Start receiving messages
            _receiveTask = ReceiveMessagesAsync();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AP Network] Connection failed: {e.Message}");
            _isConnecting = false;
            _webSocket?.Dispose();
            _webSocket = null;
            return false;
        }
    }

    public void Disconnect()
    {
        if (_webSocket == null) return;

        try
        {
            _cancellationTokenSource?.Cancel();
            _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait(1000);
            _webSocket?.Dispose();
            _webSocket = null;
            Debug.Log("[AP Network] Disconnected");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AP Network] Error disconnecting: {e.Message}");
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        byte[] buffer = new byte[1024 * 16];
        var messageBuilder = new System.Text.StringBuilder();

        try
        {
            while (_webSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    _cancellationTokenSource.Token
                );

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    Debug.Log("[AP Network] WebSocket closed");
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        _messageQueue.Enqueue(messageBuilder.ToString());
                        messageBuilder.Clear();
                    }
                }
            }
        }
        catch (Exception e)
        {
            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Debug.LogError($"[AP Network] Receive error: {e.Message}");
            }
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            Debug.LogError("[AP Network] WebSocket not connected");
            return;
        }

        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );
        }
        catch (Exception e)
        {
            Debug.LogError($"[AP Network] Send error: {e.Message}");
        }
    }

    public bool HasMessages()
    {
        return _messageQueue.Count > 0;
    }

    public string DequeueMessage()
    {
        if (_messageQueue.Count > 0)
            return _messageQueue.Dequeue();
        return null;
    }

    public bool IsConnected()
    {
        return _webSocket != null && _webSocket.State == WebSocketState.Open;
    }

    private void OnDestroy()
    {
        Disconnect();
    }
}
