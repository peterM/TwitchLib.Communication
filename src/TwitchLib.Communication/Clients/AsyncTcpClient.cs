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
    public class AsyncTcpClient : IAsyncClient
    {
        private System.Net.Sockets.TcpClient _tcpClient;
        private readonly ITwitchStreamReader _twitchStreamReader;
        private readonly ITwitchStreamWritter _twitchStreamWritter;
        private readonly IrcServerProvider _ircServerProvider;

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
                if (_twitchStreamReader != null)
                    _twitchStreamReader.OnError += value;

                if (_twitchStreamWritter != null)
                    _twitchStreamWritter.OnError += value;

                OnErrorInternal += value;
            }

            remove
            {
                if (_twitchStreamReader != null)
                    _twitchStreamReader.OnError -= value;

                if (_twitchStreamWritter != null)
                    _twitchStreamWritter.OnError -= value;

                OnErrorInternal -= value;
            }
        }

        public event Func<object, OnMessageEventArgs, Task> OnMessage
        {
            add
            {
                if (_twitchStreamReader != null)
                    _twitchStreamReader.OnMessage += value;
            }

            remove
            {
                if (_twitchStreamReader != null)
                    _twitchStreamReader.OnMessage -= value;
            }
        }

        // Others
        public event Func<object, OnConnectedEventArgs, Task> OnConnected;
        public event Func<object, OnDisconnectedEventArgs, Task> OnDisconnected;
        public event Func<object, OnFatalErrorEventArgs, Task> OnFatality;
        public event Func<object, OnStateChangedEventArgs, Task> OnStateChanged;
        public event Func<object, OnReconnectedEventArgs, Task> OnReconnected;

        internal AsyncTcpClient(
            IClientOptions clientOptions,
            IrcServerProvider ircServerProvider,
            ITwitchStreamReader twitchStreamReader,
            ITwitchStreamWritter twitchStreamWritter)
        {
            NetworkServices = new List<Task>();
            TokenSource = new CancellationTokenSource();

            _twitchStreamReader = twitchStreamReader;
            _twitchStreamWritter = twitchStreamWritter;
            _ircServerProvider = ircServerProvider;

            Options = clientOptions;

            CreateClient();
        }

        public AsyncTcpClient()
            : this(new ClientOptions(),
                  new IrcServerProvider(),
                  new TwitchStreamReader(new IrcServerProvider().Server),
                  new TwitchStreamWriter(new IrcServerProvider().Server))
        {
        }

        public AsyncTcpClient(IClientOptions clientOptions)
           : this(clientOptions,
                 new IrcServerProvider(),
                 new TwitchStreamReader(new IrcServerProvider().Server),
                 new TwitchStreamWriter(new IrcServerProvider().Server))
        {
        }

        private void CreateClient()
        {
            if (_tcpClient != null)
                return;

            _tcpClient = new System.Net.Sockets.TcpClient();
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
            return await _twitchStreamWritter.WriteAsync(message)
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
                _twitchStreamReader?.Dispose();
                _twitchStreamWritter?.Dispose();
                await CleanupServicesAsync()
                    .ConfigureAwait(false);

                _tcpClient = null;
                NetworkServices.Clear();
            }
        }

        private async Task SetupReadersWritersAsync()
        {
            await _twitchStreamReader.SetupFromClientAsync(_tcpClient, Options)
                .ConfigureAwait(false);

            await _twitchStreamWritter.SetupFromClientAsync(_tcpClient, Options)
                .ConfigureAwait(false);
        }

        private async Task ConnectAsync()
        {
            int portNumber = GetPort();

            await _tcpClient.ConnectAsync(_ircServerProvider.Server, portNumber)
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

                if (cancellationToken.IsCancellationRequested && lastState)
                {
                    OnStateChanged?.Invoke(this, new OnStateChangedEventArgs { IsConnected = IsConnected, WasConnected = lastState });
                }
            }
            catch (Exception ex)
            {
                RaiseOnErrorInternal(ex);
            }

            if (needsReconnect && !cancellationToken.IsCancellationRequested)
            {
                await ReconnectAsync()
                    .ConfigureAwait(false);
            }
        }

        protected void RaiseOnErrorInternal(Exception ex)
        {
            OnErrorInternal?.Invoke(this, new OnErrorEventArgs { Exception = ex });
        }

        private async Task CleanupServicesAsync()
        {
            if (NetworkServices.Count == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(NetworkServices)
                    .ConfigureAwait(false);
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
            NetworkServices.Add(_twitchStreamReader.StartListenAsync(cancellationToken));

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