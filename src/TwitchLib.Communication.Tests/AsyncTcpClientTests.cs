using System;
using System.Threading.Tasks;

using TwitchLib.Communication.Clients;

using Xunit;

namespace TwitchLib.Communication.Tests
{
    public class AsyncTcpClientTests : IDisposable
    {
        [Fact]
        public async Task Client_OpenAsync_ConnectionIsOpen()
        {
            var client = new AsyncTcpClient();

            await client.OpenAsync();
            Assert.True(client.IsConnected);

            //await Task.Delay(5000);

            await client.CloseAsync();
            client.Dispose();

            Assert.False(client.IsConnected);
        }

        [Fact]
        public async Task Client_OpenAsync_OnConnecedEventIsReceived()
        {
            var client = new AsyncTcpClient();
            string message = null;

            client.OnConnected += (a, b) =>
            {
                message = "connected";
                return Task.CompletedTask;
            };

            await client.OpenAsync();

            await client.CloseAsync();
            client.Dispose();

            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("connected", message);
        }

        [Fact]
        public async Task Client_OpenAsync_OnDisconnecedEventIsReceived()
        {
            var client = new AsyncTcpClient();
            string message = null;

            client.OnDisconnected += (a, b) =>
            {
                message = "disconnected";
                return Task.CompletedTask;
            };

            await client.OpenAsync();

            await client.CloseAsync();
            client.Dispose();

            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("disconnected", message);
        }

        [Fact]
        public async Task Client_OpenAsync_OnReconnecedEventIsReceived()
        {
            var client = new AsyncTcpClient();
            string message = null;

            client.OnReconnected += (a, b) =>
            {
                message = "reconnected";
                return Task.CompletedTask;
            };

            await client.OpenAsync();
            await client.ReconnectAsync();

            await client.CloseAsync();
            client.Dispose();

            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("reconnected", message);
        }

        [Fact]
        public async Task Client_OpenAsync_OnStateChangedEventIsReceived()
        {
            var client = new AsyncTcpClient();
            string message = null;
            int count = 0;

            client.OnStateChanged += (a, b) =>
            {
                message = "state changed";
                count += 1;
                return Task.CompletedTask;
            };

            await client.OpenAsync();

            await client.CloseAsync();
            client.Dispose();

            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("state changed", message);
            Assert.Equal(2, count);
        }

        [Fact]
        public async Task Client_OpenAsyncWithReconnectAsync_OnStateChangedEventIsReceived()
        {
            var client = new AsyncTcpClient();
            string message = null;
            int count = 0;

            client.OnStateChanged += (a, b) =>
            {
                message = "state changed";
                count += 1;
                return Task.CompletedTask;
            };

            await client.OpenAsync();
            await client.ReconnectAsync();
            Assert.Equal(3, count);

            await client.CloseAsync();
            client.Dispose();

            Assert.False(string.IsNullOrEmpty(message));
            Assert.Equal("state changed", message);
            Assert.Equal(4, count);
        }

        public void Dispose()
        {
        }
    }
}
