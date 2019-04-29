using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Communication.Events;
using TwitchLib.Communication.Interfaces;
using TwitchLib.Communication.Models;

namespace TwitchLib.Communication.Clients
{
    public sealed class AsyncTcpClient : IDisposable
    {
        private const string _server = "irc.chat.twitch.tv";

        private CancellationTokenSource _cancellationTokenSource;
        private readonly List<Task> _networkServices;
        private System.Net.Sockets.TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;

        private int Port => GetPort();

        public IClientOptions Options { get; }

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public event Func<object, OnConnectedEventArgs, Task> OnConnected;
        public event Func<object, OnDataEventArgs, Task> OnData;
        public event Func<object, OnDisconnectedEventArgs, Task> OnDisconnected;
        public event Func<object, OnErrorEventArgs, Task> OnError;
        public event Func<object, OnFatalErrorEventArgs, Task> OnFatality;
        public event Func<object, OnMessageEventArgs, Task> OnMessage;
        public event Func<object, OnMessageThrottledEventArgs, Task> OnMessageThrottled;
        public event Func<object, OnWhisperThrottledEventArgs, Task> OnWhisperThrottled;
        public event Func<object, OnSendFailedEventArgs, Task> OnSendFailed;
        public event Func<object, OnStateChangedEventArgs, Task> OnStateChanged;
        public event Func<object, OnReconnectedEventArgs, Task> OnReconnected;

        public AsyncTcpClient(IClientOptions clientOptions)
        {
            _networkServices = new List<Task>();
            _cancellationTokenSource = new CancellationTokenSource();

            Options = clientOptions;

            CreateClient();
        }

        private void CreateClient()
        {
            if (_tcpClient != null)
                return;

            _tcpClient = new System.Net.Sockets.TcpClient();
        }

        public AsyncTcpClient()
            : this(new ClientOptions())
        {
        }

        public async Task<bool> OpenAsync()
        {
            try
            {
                if (IsConnected)
                {
                    return true;
                }

                _networkServices.Add(StartMonitorTaskAsync(_cancellationTokenSource.Token));
                await ConnectAsync().ConfigureAwait(false);

                if (Options.UseSsl)
                {
                    await SetupStreamsAsSslAsync()
                        .ConfigureAwait(false);
                }
                else
                {
                    await SetupAsNonSslAsync()
                        .ConfigureAwait(false);
                }

                if (!IsConnected)
                {
                    return await OpenAsync()
                        .ConfigureAwait(false);
                }

                await StartNetworkServicesAsync(_cancellationTokenSource.Token)
                    .ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task CloseAsync()
        {
            await DisposeAsync(true)
                .ConfigureAwait(false);

            OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs());

            await Task.CompletedTask;
        }

        public async Task ReconnectAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            _cancellationTokenSource = new CancellationTokenSource();
            CreateClient();
            await OpenAsync().ConfigureAwait(false);
            OnReconnected?.Invoke(this, new OnReconnectedEventArgs());
        }

        public async Task SendAsync(string message)
        {
            await _writer.WriteLineAsync(message).ConfigureAwait(false);
            await _writer.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            DisposeAsync(true).GetAwaiter().GetResult();
        }

        private async Task DisposeAsync(bool disposing)
        {
            if (disposing)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                _tcpClient?.Close();
                _reader?.Dispose();
                _writer?.Dispose();
                await CleanupServicesAsync();

                _tcpClient = null;
                _reader = null;
                _writer = null;
                _networkServices.Clear();
            }
        }

        private Task SetupAsNonSslAsync()
        {
            _reader = new StreamReader(_tcpClient.GetStream());
            _writer = new StreamWriter(_tcpClient.GetStream());

            return Task.CompletedTask;
        }

        private void SetupStreams(Stream stream)
        {
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream);
        }

        private async Task SetupStreamsAsSslAsync()
        {
            SslStream sslStream = new SslStream(_tcpClient.GetStream(), false);
            await sslStream.AuthenticateAsClientAsync(_server).ConfigureAwait(false);
            SetupStreams(sslStream);
        }

        private async Task ConnectAsync()
        {
            await _tcpClient.ConnectAsync(_server, Port)
                .ConfigureAwait(false);
        }

        private async Task StartMonitorTaskAsync(CancellationToken cancellationToken)
        {
            var needsReconnect = false;
            try
            {
                bool lastState = IsConnected;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (lastState == IsConnected)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                        continue;
                    }

                    OnStateChanged?.Invoke(this, new OnStateChangedEventArgs { IsConnected = IsConnected, WasConnected = lastState });
                    if (IsConnected)
                    {
                        OnConnected?.Invoke(this, new OnConnectedEventArgs());
                    }

                    if (!IsConnected
                        && !cancellationToken.IsCancellationRequested)
                    {
                        if (lastState
                            && Options.ReconnectionPolicy != null
                            && !Options.ReconnectionPolicy.AreAttemptsComplete())
                        {
                            needsReconnect = true;
                            break;
                        }

                        OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs());
                    }

                    lastState = IsConnected;
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
            }

            if (needsReconnect && !cancellationToken.IsCancellationRequested)
            {
                await ReconnectAsync().ConfigureAwait(false);
            }
        }

        private async Task AwaitTaskAsync(Task task)
        {
            if (task != null)
            {
                await task;
            }
        }

        private async Task CleanupServicesAsync()
        {
            if (_networkServices.Count == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(_networkServices).ConfigureAwait(false);
            }
            catch (Exception)
            {
                OnFatality?.Invoke(
                   this,
                   new OnFatalErrorEventArgs
                   {
                       Reason = "Fatal network error. Network services fail to shut down."
                   });
            }
        }

        private Task StartNetworkServicesAsync(CancellationToken cancellationToken)
        {
            Task listenerTask = StartListenerTaskAsync(cancellationToken);
            _networkServices.Add(listenerTask);

            return Task.CompletedTask;
        }

        private async Task StartListenerTaskAsync(CancellationToken cancellationToken)
        {
            while (IsConnected
                   && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    string message = await _reader.ReadLineAsync().ConfigureAwait(false);
                    OnMessage?.Invoke(this, new OnMessageEventArgs { Message = message });
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
                }
            }
        }

        private int GetPort()
        {
            if (Options == null)
                return 0;

            return Options.UseSsl ? 443 : 80;
        }
    }
}