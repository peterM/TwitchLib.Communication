using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
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
        private IEnumerable<IStreamReceiver> StreamReceivers =>
            new IStreamReceiver[]
            {
                new HttpStreamReceiver(),
                new HttpsStreamReceiver(_server)
            };

        private const string _server = "irc.chat.twitch.tv";

        private System.Net.Sockets.TcpClient _tcpClient;
        private StreamReader _reader;
        private StreamWriter _writer;

        protected CancellationTokenSource TokenSource { get; set; }

        protected List<Task> NetworkServices { get; }

        protected int Port => GetPort();

        public IClientOptions Options { get; }

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public event Func<object, OnConnectedEventArgs, Task> OnConnected;
        public event Func<object, OnDisconnectedEventArgs, Task> OnDisconnected;
        public event Func<object, OnErrorEventArgs, Task> OnError;
        public event Func<object, OnFatalErrorEventArgs, Task> OnFatality;
        public event Func<object, OnMessageEventArgs, Task> OnMessage;
        public event Func<object, OnStateChangedEventArgs, Task> OnStateChanged;
        public event Func<object, OnReconnectedEventArgs, Task> OnReconnected;

        public AsyncTcpClient(IClientOptions clientOptions)
        {
            NetworkServices = new List<Task>();
            TokenSource = new CancellationTokenSource();

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
                await ConnectAsync().ConfigureAwait(false);

                await SetupAsNonSslAsync()
                     .ConfigureAwait(false);

                await SetupStreamsAsSslAsync()
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
            await CloseAsync().ConfigureAwait(false);
            TokenSource = new CancellationTokenSource();
            CreateClient();
            await OpenAsync().ConfigureAwait(false);
            OnReconnected?.Invoke(this, new OnReconnectedEventArgs());
        }

        public virtual async Task<bool> SendAsync(string message)
        {
            var result = true;
            try
            {
                await _writer.WriteLineAsync(message).ConfigureAwait(false);
                await _writer.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = false;
                RaiseOnErrorAsync(ex);
            }

            return result;
        }

        protected Task RaiseOnErrorAsync(Exception ex)
        {
            return AwaitTaskAsync(OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex }));
        }

        public void Dispose()
        {
            DisposeAsync(true).GetAwaiter().GetResult();
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
                await CleanupServicesAsync();

                _tcpClient = null;
                _reader = null;
                _writer = null;
                NetworkServices.Clear();
            }
        }

        private async Task SetupAsNonSslAsync()
        {
            var stream = await StreamReceivers.SingleOrDefault(d => d.IsHttps == Options.UseSsl)
                .GetStreamAsync(_tcpClient).ConfigureAwait(false);

            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream);
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
            NetworkServices.Add(StartListenerTaskAsync(cancellationToken));

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