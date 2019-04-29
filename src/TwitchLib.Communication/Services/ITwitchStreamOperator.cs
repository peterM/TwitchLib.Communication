using System;
using System.Threading.Tasks;
using TwitchLib.Communication.Interfaces;

namespace TwitchLib.Communication.Services
{
    internal interface ITwitchStreamOperator : IDisposable
    {
        Task SetupFromClientAsync(System.Net.Sockets.TcpClient tcpClient, IClientOptions clientOptions);
    }
}
