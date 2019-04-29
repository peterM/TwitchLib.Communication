using System.Threading.Tasks;

using TwitchLib.Communication.Clients;

using Xunit;

namespace TwitchLib.Communication.Tests
{
    public class AsyncTcpClientTests
    {
        [Fact]
        public async Task Client_OpenAsync_ConnectionIsOpen()
        {
            var client = new AsyncTcpClient();

            await client.OpenAsync();
            Assert.True(client.IsConnected);

            //await Task.Delay(5000);

            await client.CloseAsync();
            Assert.False(client.IsConnected);
        }
    }
}
