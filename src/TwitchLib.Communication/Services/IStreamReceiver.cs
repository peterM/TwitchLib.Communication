using System.IO;
using System.Threading.Tasks;

namespace TwitchLib.Communication.Services
{
    internal interface IStreamReceiver
    {
        bool IsHttps { get; }

        Task<Stream> GetStreamAsync(System.Net.Sockets.TcpClient client);
    }
}
