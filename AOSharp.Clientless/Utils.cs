

namespace AOSharp.Clientless
{
    public class Utils
    {
        public static long SetExpireTimeInTicks(float timeInSeconds) =>  DateTime.Now.AddSeconds(timeInSeconds).Ticks;
        public static double GetRemainingTimeInSeconds(long expireTimeTicks) => TimeSpan.FromTicks(expireTimeTicks - DateTime.Now.Ticks).TotalSeconds;
    }
}
