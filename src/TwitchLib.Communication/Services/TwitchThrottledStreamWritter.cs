namespace TwitchLib.Communication.Services
{
    internal class TwitchThrottledStreamWritter : TwitchStreamWriter, ITwitchThrottledStreamWritter
    {
        public TwitchThrottledStreamWritter(string server)
            : base(server)
        {
        }
    }
}
