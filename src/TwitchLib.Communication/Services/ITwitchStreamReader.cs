using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Communication.Events;

namespace TwitchLib.Communication.Services
{
    internal interface ITwitchStreamReader : ITwitchStreamOperator
    {
        event Func<object, OnErrorEventArgs, Task> OnError;
        event Func<object, OnMessageEventArgs, Task> OnMessage;

        Task StartListen(CancellationToken cancellationToken);
    }
}
