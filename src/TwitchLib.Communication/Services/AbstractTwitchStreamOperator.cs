using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

using TwitchLib.Communication.Interfaces;

namespace TwitchLib.Communication.Services
{
    internal abstract class AbstractTwitchStreamOperator : ITwitchStreamOperator
    {
        protected TcpClient TcpClient { get; private set; }

        protected AbstractTwitchStreamOperator(string server)
        {
            _server = server;
        }

        private IEnumerable<IStreamReceiver> StreamReceivers =>
           new IStreamReceiver[]
           {
                new HttpStreamReceiver(),
                new HttpsStreamReceiver(_server)
           };

        private readonly string _server;

        public async Task SetupFromClientAsync(TcpClient tcpClient, IClientOptions clientOptions)
        {
            TcpClient = tcpClient;
            var stream = await StreamReceivers.SingleOrDefault(d => d.IsHttps == clientOptions.UseSsl)
                .GetStreamAsync(tcpClient).ConfigureAwait(false);

            await FromStreamAsync(stream).ConfigureAwait(false);
        }

        protected abstract Task FromStreamAsync(Stream stream);

        protected abstract Task DisposeAsync(bool disposing);

        public void Dispose()
        {
            DisposeAsync(true).GetAwaiter().GetResult();
        }
    }
}
