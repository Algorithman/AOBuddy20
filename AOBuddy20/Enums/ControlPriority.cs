// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: ControlPriority.cs
// 
// Last modified: 2026-09-28 18:24
// Created:       2026-09-28 18:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

namespace AOBuddy20.Enums;

public enum ControlPriority
{
    None = 0,
    Travel = 1,
    Resupply = 2,
    Selling = 3,
    Mission = 4,
    Combat = 5, // interrupts everything below it
    Emergency = 6, // e.g. player dying, disconnect
}