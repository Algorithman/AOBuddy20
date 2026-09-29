using AOSharp.Common.GameData;
using Newtonsoft.Json;

namespace AOSharp.Clientless;

public class NanoItem : ItemBase
{
    [JsonProperty] internal int AttackDelayInTicks;

    public int Cost;
    public int NCU;
    public NanoLine NanoLine;
    public NanoSchool NanoSchool;
    public int Range;

    [JsonProperty] internal int RechargeDelayInTicks;

    public int StackingOrder;

    [JsonProperty] internal int TotalTimeInTicks;

    public NanoItem(int id, int ql) : base(id, ql)
    {
    }

    [JsonIgnore] public double TotalTime => TotalTimeInTicks / 100f;

    [JsonIgnore] public double AttackDelay => AttackDelayInTicks / 100f;

    [JsonIgnore] public double RechargeDelay => RechargeDelayInTicks / 100f;
}