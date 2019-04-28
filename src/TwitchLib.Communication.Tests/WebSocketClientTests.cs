using System.Threading;
using System.Threading.Tasks;

using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Events;
using TwitchLib.Communication.Models;

using Xunit;

namespace TwitchLib.Communication.Tests
{
    public class WebSocketClientTests
    {
        [Fact]
        public void Client_Raises_OnConnected_EventArgs()
        {
            var client = new WebSocketClient();
            var pauseConnected = new ManualResetEvent(false);

            Assert.Raises<OnConnectedEventArgs>(
                h => client.OnConnected += h,
                h => client.OnConnected -= h,
               async () =>
                {
                    client.OnConnected += (sender, e) => { pauseConnected.Set(); };
                    await client.OpenAsync(CancellationToken.None);
                    Assert.True(pauseConnected.WaitOne(5000));
                });
        }

        [Fact]
        public void Client_Raises_OnDisconnected_EventArgs()
        {
            var client = new WebSocketClient(new ClientOptions() { DisconnectWait = 5000 });
            var pauseDisconnected = new ManualResetEvent(false);

            Assert.Raises<OnDisconnectedEventArgs>(
                h => client.OnDisconnected += h,
                h => client.OnDisconnected -= h,
                async () =>
                {
                    client.OnConnected += async (sender, e) =>
                    {
                        await Task.Delay(3000);
                        await client.CloseAsync(CancellationToken.None);
                    };
                    client.OnDisconnected += (sender, e) =>
                    {
                        pauseDisconnected.Set();
                    };
                    await client.OpenAsync(CancellationToken.None);
                    Assert.True(pauseDisconnected.WaitOne(200000));
                });
        }

        [Fact]
        public void Client_Raises_OnReconnected_EventArgs()
        {
            var client = new WebSocketClient(new ClientOptions() { ReconnectionPolicy = null });
            var pauseReconnected = new ManualResetEvent(false);

            Assert.Raises<OnReconnectedEventArgs>(
                h => client.OnReconnected += h,
                h => client.OnReconnected -= h,
               async () =>
                {
                    client.OnConnected += async (s, e) =>
                    {
                        await client.ReconnectAsync(CancellationToken.None);
                    };

                    client.OnReconnected += (s, e) => { pauseReconnected.Set(); };
                    await client.OpenAsync(CancellationToken.None);

                    Assert.True(pauseReconnected.WaitOne(20000));
                });
        }

        [Fact]
        public void Dispose_Client_Before_Connecting_IsOK()
        {
            var client = new WebSocketClient();
            client.Dispose();
        }


        [Fact]
        public void Client_Can_SendAndReceive_Messages()
        {
            var client = new WebSocketClient();
            var pauseConnected = new ManualResetEvent(false);
            var pauseReadMessage = new ManualResetEvent(false);

            Assert.Raises<OnMessageEventArgs>(
                h => client.OnMessage += h,
                h => client.OnMessage -= h,
              async () =>
                {
                    client.OnConnected += (sender, e) => { pauseConnected.Set(); };

                    client.OnMessage += (sender, e) =>
                    {
                        pauseReadMessage.Set();
                        Assert.Equal("PONG :tmi.twitch.tv", e.Message);
                    };

                    await client.OpenAsync(CancellationToken.None);
                    await client.SendAsync("PING", CancellationToken.None);
                    Assert.True(pauseConnected.WaitOne(5000));
                    Assert.True(pauseReadMessage.WaitOne(5000));
                });
        }
    }
}
