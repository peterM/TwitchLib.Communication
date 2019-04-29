using System;
using System.IO;
using System.Threading.Tasks;

using TwitchLib.Communication.Events;

namespace TwitchLib.Communication.Services
{
    internal class TwitchStreamWriter : AbstractTwitchStreamOperator, ITwitchStreamWritter
    {
        protected StreamWriter Writter;

        public event Func<object, OnErrorEventArgs, Task> OnError;

        public TwitchStreamWriter(string server) : base(server)
        {
        }

        public virtual async Task<bool> WriteAsync(string message)
        {
            if (Writter == null)
            {
                return false;
            }

            bool result = true;
            try
            {
                await Writter.WriteLineAsync(message).ConfigureAwait(false);
                await Writter.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = false;
                OnError?.Invoke(this, new OnErrorEventArgs { Exception = ex });
            }

            return result;
        }

        protected override Task DisposeAsync(bool disposing)
        {
            Writter?.Dispose();

            return Task.CompletedTask;
        }

        protected override Task FromStreamAsync(Stream stream)
        {
            Writter = new StreamWriter(stream);

            return Task.CompletedTask;
        }
    }
}
