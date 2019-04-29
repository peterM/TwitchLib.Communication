using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Communication.Enums;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Interfaces;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Services;

namespace TwitchLib.Communication.Clients
{
    public class WebSocketClient : IClient
    {
        public TimeSpan DefaultKeepAliveInterval { get; set; }
        public int SendQueueLength => _throttlers.SendQueue.Count;
        public int WhisperQueueLength => _throttlers.WhisperQueue.Count;
        public bool IsConnected => Client?.State == WebSocketState.Open;
        public IClientOptions Options { get; }
        public ClientWebSocket Client { get; private set; }

        public event EventHandler<OnConnectedEventArgs> OnConnected;
        public event EventHandler<OnDataEventArgs> OnData;
        public event EventHandler<OnDisconnectedEventArgs> OnDisconnected;
        public event EventHandler<OnErrorEventArgs> OnError;
        public event EventHandler<OnFatalErrorEventArgs> OnFatality;
        public event EventHandler<OnMessageEventArgs> OnMessage;
        public event EventHandler<OnMessageThrottledEventArgs> OnMessageThrottled;
        public event EventHandler<OnWhisperThrottledEventArgs> OnWhisperThrottled;
        public event EventHandler<OnSendFailedEventArgs> OnSendFailed;
        public event EventHandler<OnStateChangedEventArgs> OnStateChanged;
        public event EventHandler<OnReconnectedEventArgs> OnReconnected;

        private string Url { get; }
        private readonly Throttlers _throttlers;
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private bool _stopServices;
        private bool _networkServicesRunning;
        private Task[] _networkTasks;
        private Task _monitorTask;

        public WebSocketClient(IClientOptions options = null)
        {
            Options = options ?? new ClientOptions();

            switch (Options.ClientType)
            {
                case ClientType.Chat:
                    Url = Options.UseSsl ? "wss://irc-ws.chat.twitch.tv:443" : "ws://irc-ws.chat.twitch.tv:80";
                    break;
                case ClientType.PubSub:
                    Url = Options.UseSsl ? "wss://pubsub-edge.twitch.tv:443" : "ws://pubsub-edge.twitch.tv:80";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _throttlers = new Throttlers(this, Options.ThrottlingPeriod, Options.WhisperThrottlingPeriod) { TokenSource = _tokenSource };
        }

        private async Task InitializeClientAsync(CancellationToken cancellationToken)
        {
            Client?.Abort();
            Client = new ClientWebSocket();

            if (_monitorTask == null)
            {
                _monitorTask = StartMonitorTaskAsync(cancellationToken);
                return;
            }

            if (_monitorTask.IsCompleted)
            {
                _monitorTask = StartMonitorTaskAsync(cancellationToken);
                await _monitorTask;
            }
        }

        public async Task<bool> OpenAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (IsConnected) return true;

                await InitializeClientAsync(cancellationToken).ConfigureAwait(false);
                Client.ConnectAsync(new Uri(Url), _tokenSource.Token).Wait(10000);
                if (!IsConnected)
                    return await OpenAsync(cancellationToken).ConfigureAwait(false);

                await StartNetworkServicesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (WebSocketException)
            {
                await InitializeClientAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        public async Task CloseAsync(CancellationToken cancellationToken, bool callDisconnect = true)
        {
            Client?.Abort();
            _stopServices = callDisconnect;
            CleanupServices();
            //await InitializeClientAsync(cancellationToken).ConfigureAwait(false);
            OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs());
        }

        public async Task ReconnectAsync(CancellationToken cancellationToken)
        {
            await CloseAsync(cancellationToken).ConfigureAwait(false);
            await InitializeClientAsync(cancellationToken).ConfigureAwait(false);
            await OpenAsync(cancellationToken).ConfigureAwait(false);
            OnReconnected?.Invoke(this, new OnReconnectedEventArgs());
        }

        public Task<bool> SendAsync(string message, CancellationToken cancellationToken)
        {
            try
            {
                if (!IsConnected || SendQueueLength >= Options.SendQueueCapacity)
                {
                    return Task.FromResult(false);
                }

                _throttlers.SendQueue.Add(new Tuple<DateTime, string>(DateTime.UtcNow, message));

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
                throw;
            }
        }

        public bool SendWhisper(string message)
        {
            try
            {
                if (!IsConnected || WhisperQueueLength >= Options.WhisperQueueCapacity)
                {
                    return false;
                }

                _throttlers.WhisperQueue.Add(new Tuple<DateTime, string>(DateTime.UtcNow, message));

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
                throw;
            }
        }

        private Task StartNetworkServicesAsync(CancellationToken cancellationToken)
        {
            _networkServicesRunning = true;
            _networkTasks = new[]
            {
                StartListenerTaskAsync(cancellationToken),
                _throttlers.StartSenderTaskAsync(cancellationToken),
                _throttlers.StartWhisperSenderTaskAsync(cancellationToken)
            }.ToArray();

            if (!_networkTasks.Any(c => c.IsFaulted))
                return Task.CompletedTask;
            _networkServicesRunning = false;
            CleanupServices();

            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] message, CancellationToken cancellationToken)
        {
            return Client.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, cancellationToken);
        }

        private async Task StartListenerTaskAsync(CancellationToken cancellationToken)
        {
            var message = "";

            while (IsConnected
                && _networkServicesRunning
                && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                var buffer = new byte[1024];

                try
                {
                    result = await Client.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await InitializeClientAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }

                if (result == null) continue;

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Close:
                        await CloseAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case WebSocketMessageType.Text when !result.EndOfMessage:
                        message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                        continue;
                    case WebSocketMessageType.Text:
                        message += Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                        OnMessage?.Invoke(this, new OnMessageEventArgs() { Message = message });
                        break;
                    case WebSocketMessageType.Binary:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                message = "";
            }
        }

        private async Task StartMonitorTaskAsync(CancellationToken cancellationToken)
        {
            var needsReconnect = false;
            try
            {
                var lastState = IsConnected;
                while (!_tokenSource.IsCancellationRequested)
                {
                    if (lastState == IsConnected)
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                        continue;
                    }
                    OnStateChanged?.Invoke(this, new OnStateChangedEventArgs { IsConnected = Client.State == WebSocketState.Open, WasConnected = lastState });

                    if (IsConnected)
                        OnConnected?.Invoke(this, new OnConnectedEventArgs());

                    if (!IsConnected && !_stopServices)
                    {
                        if (lastState && Options.ReconnectionPolicy?.AreAttemptsComplete() == false)
                        {
                            needsReconnect = true;
                            break;
                        }

                        OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs());
                        if (Client.CloseStatus != null && Client.CloseStatus != WebSocketCloseStatus.NormalClosure)
                            OnError?.Invoke(this, new OnErrorEventArgs { Exception = new Exception(Client.CloseStatus + " " + Client.CloseStatusDescription) });
                    }

                    lastState = IsConnected;
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
            }

            if (needsReconnect && !_stopServices)
                await ReconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        private void CleanupServices()
        {
            _tokenSource.Cancel();
            _tokenSource = new CancellationTokenSource();
            _throttlers.TokenSource = _tokenSource;

            if (!_stopServices) return;
            if (!(_networkTasks?.Length > 0)) return;
            if (Task.WaitAll(_networkTasks, 15000)) return;

            OnFatality?.Invoke(this,
                new OnFatalErrorEventArgs
                {
                    Reason = "Fatal network error. Network services fail to shut down."
                });
            _stopServices = false;
            _throttlers.Reconnecting = false;
            _networkServicesRunning = false;
        }

        public void WhisperThrottled(OnWhisperThrottledEventArgs eventArgs)
        {
            OnWhisperThrottled?.Invoke(this, eventArgs);
        }

        public void MessageThrottled(OnMessageThrottledEventArgs eventArgs)
        {
            OnMessageThrottled?.Invoke(this, eventArgs);
        }

        public void SendFailed(OnSendFailedEventArgs eventArgs)
        {
            OnSendFailed?.Invoke(this, eventArgs);
        }

        public void Error(OnErrorEventArgs eventArgs)
        {
            OnError?.Invoke(this, eventArgs);
        }

        public void Dispose()
        {
            CloseAsync(CancellationToken.None).GetAwaiter().GetResult();
            _throttlers.ShouldDispose = true;
            _tokenSource.Cancel();
            Thread.Sleep(500);
            _tokenSource.Dispose();
            Client?.Dispose();
            GC.Collect();
        }
    }
}
