// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: ControlPriority.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-29 23:09
// 
// Long live OmniCell and AOBuddy
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
    LowHealthNanoEmergency = 600,
    Emergency = 700, // e.g. player dying, disconnect
    
}