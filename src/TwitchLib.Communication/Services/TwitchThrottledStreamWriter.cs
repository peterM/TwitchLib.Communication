namespace TwitchLib.Communication.Services
{
    internal class TwitchThrottledStreamWriter : TwitchStreamWriter, ITwitchThrottledStreamWriter
    {
        public TwitchThrottledStreamWriter(string server)
            : base(server)
        {
        }
    }
}
