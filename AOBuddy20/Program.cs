// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: Program.cs
// 
// Last modified: 2026-09-29 13:47
// Created:       2026-09-28 16:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using AOBuddy20.Configuration;
using AOBuddy20.Controlling;
using AOSharp.Clientless;
using AOSharp.Clientless.Common;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;

namespace AOBuddy20;

internal class Program
{
    private static readonly List<ClientDomain> _domains = new List<ClientDomain>();

    private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    public static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ControlArbiter>();
        services.AddSingleton<MissionController>();


        var provider = services.BuildServiceProvider();
        WirePackets(provider);

        string configFile;
        // --config <file> (owner, 2026-09-28): one config per character, e.g. --config dadbod.json. Relative to Build\.
        // The same name is handed to plugins (AOBUDDY_CONFIG) so Plugins\<name>\dadbod.json is read if it exists.
        var configName = "config.json";
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                configName = args[i + 1];
            }
        }

        Environment.SetEnvironmentVariable("AOBUDDY_CONFIG", configName);
        var configPath = Path.IsPathRooted(configName) ? configName : AppDomain.CurrentDomain.BaseDirectory + configName;

        try
        {
            configFile = File.ReadAllText(configPath);
        }
        catch
        {
            Console.WriteLine($"Config file not found at '{configPath}', read the instructions.");
            Console.ReadLine();
            return;
        }

        var config = JsonConvert.DeserializeObject<MainConfig>(configFile);

        if (config == null || config.Accounts == null || config.Accounts.Count == 0)
        {
            Console.WriteLine($"'{configPath}' has no Accounts. Copy config.example.json over it and fill it in.");
            Console.ReadLine();
            return;
        }


        // The window is named after the character(s) it runs (owner, 2026-09-26: "name the console the bots name,
        // we may run a few at a time").
        try
        {
            Console.Title = string.Join(", ", config.Accounts.Select(a => a.Character).Where(c => !string.IsNullOrEmpty(c))) + " - AOBuddy";
        }
        catch
        {
        }
        
        Client.SuppressItemDataLoad();

        foreach (var acc in config.Accounts)
        {
            _domains.Add(CreateBot(acc));
        }

        Console.ReadLine();

        foreach (var domain in _domains)
        {
            domain.Unload();
        }
    }

    private static void WirePackets(ServiceProvider provider)
    {
    }

    private static ClientDomain CreateBot(AccountInfo accInfo)
    {
        var logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();

        var dimension = ParseDimension(accInfo.Dimension);
        logger.Information($"Logging {accInfo.Character} into dimension {dimension}.");
        var instance = Client.CreateInstance(accInfo.Username, accInfo.Password, accInfo.Character, dimension, logger);

        Client.SuppressItemDataLoad(false);
        instance.Start();
        return instance;
    }


    // Map the per-account "Dimension" config string to the enum. Accepts the enum name and
    // common aliases; blank/unknown defaults to RubiKa (main). Rubi-Ka 2019 is the fresh-start
    // progression server (its own char list and chat server).
    private static Dimension ParseDimension(string value)
    {
        var key = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        switch (key)
        {
            case "rubika2019":
            case "rk2019":
            case "2019":
                return Dimension.RubiKa2019;
            case "":
            case "rubika":
            case "rk":
            case "rk1":
            case "rk5":
                return Dimension.RubiKa;
            default:
                if (Enum.TryParse(value, true, out Dimension parsed))
                {
                    return parsed;
                }

                Console.WriteLine($"Unknown dimension '{value}', defaulting to RubiKa. Use \"RubiKa\" or \"RubiKa2019\".");
                return Dimension.RubiKa;
        }
    }
}