namespace AOSharp.Clientless;

public class Utils
{
    public static long SetExpireTimeInTicks(float timeInSeconds)
    {
        return DateTime.Now.AddSeconds(timeInSeconds).Ticks;
    }

    public static double GetRemainingTimeInSeconds(long expireTimeTicks)
    {
        return TimeSpan.FromTicks(expireTimeTicks - DateTime.Now.Ticks).TotalSeconds;
    }
}