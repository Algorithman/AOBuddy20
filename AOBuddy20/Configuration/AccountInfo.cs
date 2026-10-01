// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: AccountInfo.cs
// 
// Last modified: 2026-09-30 00:19
// Created:       2026-09-29 23:09
// 
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

namespace AOBuddy20.Configuration;

public class AccountInfo
{
    public string Character = "";
    public string Dimension = ""; // "RubiKa" (default) or "RubiKa2019"
    public string Owner = ""; // the character whose /tells are obeyed (empty: no one can command the bot)
    public string Password = "";
    public string Username = "";

    // --- Resupply (AOBuddy10 Config.cs:120-138, 'resupply' command): buying stims/rechargers in a shop.
    // A field missing from the JSON keeps its default.
    public int LowStimCount = 10; // warn when usable stims fall to this many (solo mode's shopping check)
    public int LowRechargerCount = 10; // warn when usable rechargers fall to this many
    public string ResupplyStimName = "Health and Nano Stim"; // exact name: "Stim" also matches Boosted/Burst/Swim
    public string ResupplyRechargerName = "Health and Nano Recharger";
    public int ResupplyStimTarget = 2; // top usable stims back up to this many (times 25)
    public int ResupplyRechargerTarget = 3; // top usable rechargers back up to this many (times 20)
    public float ResupplySearchRadius = 40f; // terminals within this of the bot are considered
    public float ResupplyUseRange = 3f; // walk this close to a terminal before using it
    public int ResupplyKeepFreeSlots = 2; // never fill the last inventory slots with supplies
    public int ResupplyCashReserve = 0; // credits never spent on supplies
    public float ResupplyNagSeconds = 120f; // short of credits: remind the owner this often
    public List<string> ResupplyMachineKeywords = new()
    {
        "Medic", "Health", "Stim", "Recharg", "First Aid", "Treatment", "Pharma",
    }; // a terminal name with one of these ranks first when nothing is remembered about it
}