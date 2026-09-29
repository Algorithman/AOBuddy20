// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MinLogLevelAttribute.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-29 23:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using Serilog.Events;

namespace AOBuddy20.Utils;

/// <summary>
///     Attribute for per class minimum log levels
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class MinLogLevelAttribute : Attribute
{
    public MinLogLevelAttribute(LogEventLevel level)
    {
        Level = level;
    }

    public LogEventLevel Level { get; }
}