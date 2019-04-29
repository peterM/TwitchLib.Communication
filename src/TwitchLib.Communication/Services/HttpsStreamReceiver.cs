using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TwitchLib.Communication.Services
{
    public class HttpsStreamReceiver : IHttpStreamReceiver
    {
        private readonly string _server;

        public HttpsStreamReceiver(string server)
        {
            _server = server;
        }

        public bool IsHttps => true;

        public async Task<Stream> GetStreamAsync(System.Net.Sockets.TcpClient client)
        {
            NetworkStream networkStream = client.GetStream();
            SslStream sslStream = new SslStream(networkStream, false);
            await sslStream.AuthenticateAsClientAsync(_server).ConfigureAwait(false);

            return networkStream;
        }
    }
}
