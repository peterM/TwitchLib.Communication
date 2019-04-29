using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Communication.Events;
using TwitchLib.Communication.Interfaces;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Services;

namespace TwitchLib.Communication.Clients
{
    public class AsyncTcpClient : IDisposable
    {
        private const string _server = "irc.chat.twitch.tv";

        private System.Net.Sockets.TcpClient _tcpClient;
        private ITwitchStreamReader _reader;
        private ITwitchStreamWriter _writer;

        protected CancellationTokenSource TokenSource { get; set; }

        protected List<Task> NetworkServices { get; }

        protected int Port => GetPort();

        public IClientOptions Options { get; }

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public event Func<object, OnErrorEventArgs, Task> OnErrorInternal;
        public event Func<object, OnErrorEventArgs, Task> OnError
        {
            add
            {
                if (_reader != null)
                    _reader.OnError += value;

                if (_writer != null)
                    _writer.OnError += value;

                OnErrorInternal += value;
            }

            remove
            {
                if (_reader != null)
                    _reader.OnError -= value;

                if (_writer != null)
                    _writer.OnError -= value;

                OnErrorInternal -= value;
            }
        }

        public event Func<object, OnMessageEventArgs, Task> OnMessage
        {
            add
            {
                if (_reader != null)
                    _reader.OnMessage += value;
            }

            remove
            {
                if (_reader != null)
                    _reader.OnMessage -= value;
            }
        }

        // Others
        public event Func<object, OnConnectedEventArgs, Task> OnConnected;
        public event Func<object, OnDisconnectedEventArgs, Task> OnDisconnected;
        public event Func<object, OnFatalErrorEventArgs, Task> OnFatality;
        public event Func<object, OnStateChangedEventArgs, Task> OnStateChanged;
        public event Func<object, OnReconnectedEventArgs, Task> OnReconnected;

        public AsyncTcpClient(IClientOptions clientOptions)
        {
            NetworkServices = new List<Task>();
            TokenSource = new CancellationTokenSource();

            _reader = new TwitchStreamReader(_server);
            _writer = new TwitchStreamWriter(_server);

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

        public virtual async Task<bool> OpenAsync()
        {
            try
            {
                if (IsConnected)
                {
                    return true;
                }

                NetworkServices.Add(StartMonitorTaskAsync(TokenSource.Token));

                await ConnectAsync()
                    .ConfigureAwait(false);

                await SetupReadersWritersAsync()
                     .ConfigureAwait(false);

                if (!IsConnected)
                {
                    return await OpenAsync()
                        .ConfigureAwait(false);
                }

                await StartNetworkServicesAsync(TokenSource.Token)
                    .ConfigureAwait(false);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public virtual async Task CloseAsync()
        {
            await DisposeAsync(true)
                .ConfigureAwait(false);

            OnDisconnected?.Invoke(this, new OnDisconnectedEventArgs());

            await Task.CompletedTask;
        }

        public virtual async Task ReconnectAsync()
        {
            await CloseAsync()
                .ConfigureAwait(false);

            TokenSource = new CancellationTokenSource();

            CreateClient();

            await OpenAsync()
                .ConfigureAwait(false);

            OnReconnected?.Invoke(this, new OnReconnectedEventArgs());
        }

        public virtual async Task<bool> SendAsync(string message)
        {
            return await _writer.WriteAsync(message)
                .ConfigureAwait(false);
        }

        public void Dispose()
        {
            DisposeAsync(true).GetAwaiter()
               .GetResult();
        }

        protected virtual async Task DisposeAsync(bool disposing)
        {
            if (disposing)
            {
                TokenSource?.Cancel();
                TokenSource?.Dispose();
                TokenSource = null;

                _tcpClient?.Close();
                _reader?.Dispose();
                _writer?.Dispose();
                await CleanupServicesAsync()
                    .ConfigureAwait(false);

                _tcpClient = null;
                _reader = null;
                _writer = null;
                NetworkServices.Clear();
            }
        }

        private async Task SetupReadersWritersAsync()
        {
            await _reader.SetupFromClientAsync(_tcpClient, Options)
                .ConfigureAwait(false);

            await _writer.SetupFromClientAsync(_tcpClient, Options)
                .ConfigureAwait(false);
        }

        private async Task ConnectAsync()
        {
            int portNumber = GetPort();

            await _tcpClient.ConnectAsync(_server, portNumber)
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
                        await Task.Delay(100)
                            .ConfigureAwait(false);
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
                OnErrorInternal?.Invoke(this, new OnErrorEventArgs { Exception = ex });
            }

            if (needsReconnect && !cancellationToken.IsCancellationRequested)
            {
                await ReconnectAsync()
                    .ConfigureAwait(false);
            }
        }

        private async Task CleanupServicesAsync()
        {
            if (NetworkServices.Count == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(NetworkServices).ConfigureAwait(false);
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

        protected virtual Task StartNetworkServicesAsync(CancellationToken cancellationToken)
        {
            NetworkServices.Add(_reader.StartListen(cancellationToken));

            return Task.CompletedTask;
        }

        private int GetPort()
        {
            if (Options == null)
                return 0;

            return Options.UseSsl ? 443 : 80;
        }
    }
}