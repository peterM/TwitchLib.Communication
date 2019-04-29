using System;
using System.Threading.Tasks;

using TwitchLib.Communication.Events;

namespace TwitchLib.Communication.Interfaces
{
    public interface IAsyncClient : IDisposable
    {
        IClientOptions Options { get; }
        bool IsConnected { get; }

        event Func<object, OnErrorEventArgs, Task> OnError;
        event Func<object, OnMessageEventArgs, Task> OnMessage;

        event Func<object, OnConnectedEventArgs, Task> OnConnected;
        event Func<object, OnDisconnectedEventArgs, Task> OnDisconnected;
        event Func<object, OnFatalErrorEventArgs, Task> OnFatality;
        event Func<object, OnStateChangedEventArgs, Task> OnStateChanged;
        event Func<object, OnReconnectedEventArgs, Task> OnReconnected;

        Task<bool> OpenAsync();

        Task CloseAsync();

        Task ReconnectAsync();

        Task<bool> SendAsync(string message);
    }
}