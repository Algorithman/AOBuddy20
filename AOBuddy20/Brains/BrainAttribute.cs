// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOBuddy20
// Filename: BrainAttribute.cs
//
// Last modified: 2026-10-02
// Created:       2026-10-02
//
// Long live OmniCell and AOBuddy
// ---------------------------------------------------------------------------------------

using AOBuddy20.Enums;
using AOSharp.Common.GameData;

namespace AOBuddy20.Brains;

/// <summary>
///     Tags a brain class for one family and one profession (BrainRegistry scans for it once at
///     startup, like the MinLogLevel scan). Profession.Unknown marks the GENERAL fallback brain of
///     a family - the one that runs when no brain for the character's profession is registered.
///     Inherited = false: a SoldierCombatBrain : GeneralCombatBrain must not read as another
///     general - every brain declares its own attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BrainAttribute : Attribute
{
    public BrainAttribute(BrainKind kind, Profession profession = Profession.Unknown)
    {
        Kind = kind;
        Profession = profession;
    }

    public BrainKind Kind { get; }

    public Profession Profession { get; }
}