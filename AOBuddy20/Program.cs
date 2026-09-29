// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: Program.cs
// 
// Last modified: 2026-09-29 22:45
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
using ILogger = Serilog.ILogger;

namespace AOBuddy20;

internal class Program
{
    private static ILogger _logger;
    private static readonly List<ClientDomain> _domains = new List<ClientDomain>();

    private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

    public static async Task Main(string[] args)
    {
        int index = Array.IndexOf(args, "--logfile");
        string logfile = "AOBuddy.log";
        if (index >= 0)
        {
            logfile = args[index + 1];
        }
        
        var loggerConfiguration = new LoggerConfiguration();
        loggerConfiguration.MinimumLevel.Verbose();
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            var attr = type.GetCustomAttribute<MinLogLevelAttribute>();
            if (attr != null)
            {
                loggerConfiguration.MinimumLevel.Override(type.FullName!, attr.Level);
            }
        }

        Log.Logger = loggerConfiguration
            .WriteTo.Console()
            .WriteTo.File(logfile, rollingInterval: RollingInterval.Month)
            .CreateLogger();
        _logger = Log.Logger;
        Log.Logger.Information("Starting AOBuddy...");

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

        if (!File.Exists(configPath))
        {
            Log.Fatal($"Config file not found at '{configPath}'.");
            Console.ReadLine();
            return;
        }

        var config = JsonConvert.DeserializeObject<AccountInfo>(File.ReadAllText(configPath));

        if (config == null)
        {
            Log.Fatal("Deserialization of config file failed. Please check your configuration file.");
            Console.ReadLine();
            return;
        }

        // The window is named after the character(s) it runs (owner, 2026-09-26: "name the console the bots name,
        // we may run a few at a time").
        try
        {
            Console.Title = config.Character + " - AOBuddy";
        }
        catch
        {
        }

        Client.SuppressItemDataLoad();

        Log.Logger.Information("Creating client...");
        CreateBot(config);
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
        var dimension = ParseDimension(accInfo.Dimension);
        _logger.Information($"Logging {accInfo.Character} into dimension {dimension}.");
        var instance = Client.CreateInstance(accInfo.Username, accInfo.Password, accInfo.Character, dimension, _logger);

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