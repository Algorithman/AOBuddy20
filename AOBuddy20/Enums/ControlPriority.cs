// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: ControlPriority.cs
// 
// Last modified: 2026-09-29 13:47
// Created:       2026-09-28 18:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

namespace AOBuddy20.Enums;

public enum ControlPriority
{
    None = 0,
    Travel = 100,
    Resupply = 200,
    Selling = 300,
    Mission = 400,
    Combat = 500, // interrupts everything below it
    Emergency = 600, // e.g. player dying, disconnect
}