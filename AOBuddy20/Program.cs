using AOBuddy20.Controlling;
using Microsoft.Extensions.DependencyInjection;

namespace AOBuddy20;

class Program
{
    public static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ControlArbiter>();
        services.AddSingleton<MissionController>();
        
        
        var provider = services.BuildServiceProvider();
        WirePackets(provider);
    }

    private static void WirePackets(ServiceProvider provider)
    {
        
    }
}