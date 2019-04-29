using System;
using System.Threading.Tasks;

using TwitchLib.Communication.Events;

namespace TwitchLib.Communication.Services
{
    internal interface ITwitchStreamWritter : ITwitchStreamOperator
    {
        event Func<object, OnErrorEventArgs, Task> OnError;

        Task<bool> WriteAsync(string message);
    }
}
