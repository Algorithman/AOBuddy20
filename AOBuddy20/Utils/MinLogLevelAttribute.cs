// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: MinLogLevelAttribute.cs
// 
// Last modified: 2026-09-29 21:50
// Created:       2026-09-29 21:09
// 
// Long live OmniCell and AOBuddy20
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