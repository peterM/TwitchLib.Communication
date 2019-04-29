using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TwitchLib.Communication.Services
{
    public class HttpStreamReceiver : IHttpStreamReceiver
    {
        public bool IsHttps => false;

        public Task<Stream> GetStreamAsync(System.Net.Sockets.TcpClient client)
        {
            NetworkStream networkStream = client.GetStream();
            return Task.FromResult<Stream>(networkStream);
        }
    }
}
