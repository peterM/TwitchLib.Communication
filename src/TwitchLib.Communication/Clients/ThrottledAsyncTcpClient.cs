using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Communication.Interfaces;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Services;

namespace TwitchLib.Communication.Clients
{
    public class ThrottledAsyncTcpClient : AsyncTcpClient
    {
        private Throttlers _throttlers;

        public int SendQueueLength => _throttlers.SendQueue.Count;

        public int WhisperQueueLength => _throttlers.WhisperQueue.Count;

        public ThrottledAsyncTcpClient(IClientOptions clientOptions)
            : base(clientOptions)
        {
            CreateThrottlers();
        }

        private void CreateThrottlers()
        {
            // TODO: fix cast
            _throttlers = new Throttlers(this as IClient, Options.ThrottlingPeriod, Options.WhisperThrottlingPeriod)
            {
                TokenSource = TokenSource
            };
        }

        public ThrottledAsyncTcpClient()
            : this(new ClientOptions())
        {
        }

        //public event Func<object, OnMessageThrottledEventArgs, Task> OnMessageThrottled;
        //public event Func<object, OnWhisperThrottledEventArgs, Task> OnWhisperThrottled;
        //public event Func<object, OnDataEventArgs, Task> OnData;
        //public event Func<object, OnSendFailedEventArgs, Task> OnSendFailed;

        public override Task<bool> SendAsync(string message)
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
                RaiseOnErrorAsync(ex);
                return Task.FromResult(false);
            }
        }

        protected override Task StartNetworkServicesAsync(CancellationToken cancellationToken)
        {
            base.StartNetworkServicesAsync(cancellationToken);
            NetworkServices.Add(_throttlers.StartSenderTaskAsync(cancellationToken));
            NetworkServices.Add(_throttlers.StartWhisperSenderTaskAsync(cancellationToken));

            return Task.CompletedTask;
        }

        protected override async Task DisposeAsync(bool disposing)
        {
            _throttlers.ShouldDispose = true;
            await base.DisposeAsync(disposing);
        }

        public override Task ReconnectAsync()
        {
            CreateThrottlers();
            return base.ReconnectAsync();
        }
    }
}