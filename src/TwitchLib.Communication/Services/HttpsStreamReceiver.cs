using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TwitchLib.Communication.Services
{
    public class HttpsStreamReceiver : IHttpStreamReceiver
    {
        private readonly string _server;

        private static Dictionary<TcpClient, Stream> _cache = new Dictionary<TcpClient, Stream>();

        public HttpsStreamReceiver(string server)
        {
            _server = server;
        }

        public bool IsHttps => true;

        public async Task<Stream> GetStreamAsync(System.Net.Sockets.TcpClient client)
        {
            if (_cache.ContainsKey(client))
                return _cache[client];

            NetworkStream networkStream = client.GetStream();
            SslStream sslStream = new SslStream(networkStream, false);
            await sslStream.AuthenticateAsClientAsync(_server).ConfigureAwait(false);

            _cache.Add(client, sslStream);

            return sslStream;
        }
    }
}
