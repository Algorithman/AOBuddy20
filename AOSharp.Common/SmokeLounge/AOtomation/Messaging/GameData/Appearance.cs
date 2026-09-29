// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Appearance.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the Appearance type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.GameData;

public class Appearance
{
    #region AoMember Properties

    [AoMember(0)]
    public uint Value
    {
        get => value;

        set
        {
            this.value = value;
            UpdateStats();
        }
    }

    #endregion

    #region Fields

    private Breed breed;

    private Fatness fatness;

    private Gender gender;

    private uint race;

    private Side side;

    private uint value;

    #endregion

    #region Public Properties

    public Breed Breed
    {
        get => breed;

        set
        {
            breed = value;
            UpdateValue();
        }
    }

    public Fatness Fatness
    {
        get => fatness;

        set
        {
            fatness = value;
            UpdateValue();
        }
    }

    public Gender Gender
    {
        get => gender;

        set
        {
            gender = value;
            UpdateValue();
        }
    }

    public uint Race
    {
        get => race;

        set
        {
            race = value;
            UpdateValue();
        }
    }

    public Side Side
    {
        get => side;

        set
        {
            side = value;
            UpdateValue();
        }
    }

    #endregion

    #region Methods

    private void UpdateStats()
    {
        var sideValue = value & 7;
        side = (Side)sideValue;
        var fatnessValue = (value & 31) >> 3;
        fatness = (Fatness)fatnessValue;
        var breedValue = (value & 255) >> 5;
        breed = (Breed)breedValue;
        var genderValue = (value & 1023) >> 8;
        gender = (Gender)genderValue;
        var raceValue = value >> 10;
        race = raceValue;
    }

    private void UpdateValue()
    {
        var sideValue = (uint)side;
        var fatnessValue = (uint)fatness << 3;
        var breedValue = (uint)breed << 5;
        var genderValue = (uint)gender << 8;
        var raceValue = race << 10;
        value = sideValue + fatnessValue + breedValue + genderValue + raceValue;
    }

    #endregion
}