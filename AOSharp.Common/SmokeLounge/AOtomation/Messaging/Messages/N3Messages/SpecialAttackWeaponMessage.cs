// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SpecialAttackWeaponMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the SpecialAttackWeaponMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.SpecialAttackWeapon)]
public class SpecialAttackWeaponMessage : N3Message
{
    #region Constructors and Destructors

    public SpecialAttackWeaponMessage()
    {
        N3MessageType = N3MessageType.SpecialAttackWeapon;
        Unknown1 = 0x00000007;
        CloseCombatInitiative = 0x00000007;
        DistanceWeaponInitiative = 0x00000007;
        PhysicalProwessInitiative = 0x0000000E;
        NanoProwessInitiative = 0x00000064;
    }

    #endregion

    #region AoMember Properties

    [AoMember(0, SerializeSize = ArraySizeType.X3F1)]
    public SpecialAttackInfo[] Specials { get; set; }

    [AoMember(1)] public int Unknown1 { get; set; }

    [AoMember(2)] public int CloseCombatInitiative { get; set; }

    [AoMember(3)] public int DistanceWeaponInitiative { get; set; }

    [AoMember(4)] public int PhysicalProwessInitiative { get; set; }

    [AoMember(5)] public int NanoProwessInitiative { get; set; }

    #endregion
}