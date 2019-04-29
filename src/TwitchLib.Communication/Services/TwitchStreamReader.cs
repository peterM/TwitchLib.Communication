using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Communication.Events;

namespace TwitchLib.Communication.Services
{
    internal class TwitchStreamReader : AbstractTwitchStreamOperator, ITwitchStreamReader
    {
        protected StreamReader Reader;

        public event Func<object, OnErrorEventArgs, Task> OnError;
        public event Func<object, OnMessageEventArgs, Task> OnMessage;

        public TwitchStreamReader(string server) : base(server)
        {
        }

        protected override Task DisposeAsync(bool disposing)
        {
            Reader?.Dispose();

            return Task.CompletedTask;
        }

        protected override Task FromStreamAsync(Stream stream)
        {
            Reader = new StreamReader(stream);

            return Task.CompletedTask;
        }

        public async Task StartListenAsync(CancellationToken cancellationToken)
        {
            if (Reader == null)
                return;

            while (TcpClient.Connected
                   && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    string message = await Reader.ReadLineAsync().ConfigureAwait(false);
                    OnMessage?.Invoke(this, new OnMessageEventArgs { Message = message });
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
                }
            }
        }
    }
}
