using AOSharp.Common.GameData;

namespace AOSharp.Clientless;

public class TempItem : UniqueItem
{
    private readonly Cooldown _remainingTime;

    public TempItem(Identity slot, SimpleItem simpleItem) : base(slot, simpleItem)
    {
        _remainingTime = new Cooldown();
        _remainingTime.SetExpireTime(simpleItem.Stats.FirstOrDefault(x => x.Key == Stat.TimeExist).Value / 100f);
    }

    public double RemainingTime => _remainingTime != null ? _remainingTime.RemainingTime : 0;
}