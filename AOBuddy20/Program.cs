// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: Program.cs
// 
// Last modified: 2026-09-29 20:40
// Created:       2026-09-29 15:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using System.Reflection;
using AOBuddy20.Configuration;
using AOBuddy20.Controlling;
using AOBuddy20.Network;
using AOBuddy20.PacketConsumers;
using AOBuddy20.Utils;
using AOSharp.Clientless;
using AOSharp.Clientless.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;

namespace AOBuddy20;

internal class Program
{
    private static readonly List<ClientDomain> _domains = new List<ClientDomain>();

    private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    public static async Task Main(string[] args)
    {
        var loggerConfiguration = new LoggerConfiguration();

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            var attr = type.GetCustomAttribute<MinLogLevelAttribute>();
            if (attr != null)
                loggerConfiguration.MinimumLevel.Override(type.FullName, attr.Level);
        }

        Log.Logger = loggerConfiguration
            .WriteTo.Console(LogEventLevel.Information)
            .WriteTo.File("AOBuddy.log",LogEventLevel.Debug)
            .CreateLogger();
        
        var services = new ServiceCollection();

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(dispose: true);
        });
        
        services.AddSingleton<PacketRouter>();
        services.AddSingleton<ControlArbiter>();
        services.AddSingleton<MissionController>();
        services.AddSingleton<Awareness>();

        var provider = services.BuildServiceProvider();

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

        if (config == null)
        {
            Console.WriteLine($"'{configPath}' has no Accounts. Copy config.example.json over it and fill it in.");
            Console.ReadLine();
            return;
        }


        // The window is named after the character(s) it runs (owner, 2026-09-26: "name the console the bots name,
        // we may run a few at a time").
        try
        {
            Console.Title = config.Account.Character + " - AOBuddy";
        }
        catch
        {
        }

        Client.SuppressItemDataLoad();

        CreateBot(config.Account);
        // init packet router first
        provider.GetService<PacketRouter>()?.Init();

        WirePackets(provider);

        Console.ReadLine();
    }

    private static void WirePackets(ServiceProvider provider)
    {
        var router = provider.GetService<PacketRouter>();
        if (router != null)
        {
            provider.GetService<Awareness>()?.RegisterPackets(router);
        }
    }

    private static void CreateBot(AccountInfo accInfo)
    {
        var logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();

        var dimension = ParseDimension(accInfo.Dimension);
        logger.Information($"Logging {accInfo.Character} into dimension {dimension}.");
        var instance = Client.CreateInstance(accInfo.Username, accInfo.Password, accInfo.Character, dimension, logger);

        Client.SuppressItemDataLoad(false);
        instance.Start();
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