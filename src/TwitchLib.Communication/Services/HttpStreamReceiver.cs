using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TwitchLib.Communication.Services
{
    public class HttpStreamReceiver : IHttpStreamReceiver
    {
        private static Dictionary<TcpClient, Stream> _cache = new Dictionary<TcpClient, Stream>();

        public bool IsHttps => false;

        public Task<Stream> GetStreamAsync(System.Net.Sockets.TcpClient client)
        {
            if (_cache.ContainsKey(client))
                return Task.FromResult<Stream>(_cache[client]);

            NetworkStream networkStream = client.GetStream();
            _cache.Add(client, networkStream);

            return Task.FromResult<Stream>(networkStream);
        }
    }
}
