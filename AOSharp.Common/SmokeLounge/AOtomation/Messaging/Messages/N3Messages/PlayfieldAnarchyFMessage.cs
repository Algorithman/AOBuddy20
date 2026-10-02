// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PlayfieldAnarchyFMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the PlayfieldAnarchyFMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.PlayfieldAnarchyF)]
public class PlayfieldAnarchyFMessage : N3Message
{
    #region Constructors and Destructors

    public PlayfieldAnarchyFMessage()
    {
        N3MessageType = N3MessageType.PlayfieldAnarchyF;
        Unknown = 0x00;
        Version = 0x00000004;
        TokenMarker = 0x61;
    }

    #endregion

    public class UnknownStruct1
    {
        [AoMember(0)] public int Version { get; set; }

        [AoMember(1)] public Identity Unknown2 { get; set; }

        [AoMember(2)] public int Unknown3 { get; set; }

        [AoMember(3)] public Vector3 Unknown4 { get; set; }

        [AoMember(4)] public int Group { get; set; }

        [AoMember(5)] public int Subgroup { get; set; }

        [AoMember(6)] public float Unknown7 { get; set; }

        [AoMember(7)] public int Unknown8 { get; set; }

        [AoMember(8)] public int Unknown9 { get; set; }
    }

    public class PlayfieldDynelInfo
    {
        [AoMember(0)] public IdentityType IdentityType { get; set; }

        [AoMember(1)] public int Unknown1 { get; set; }

        [AoMember(2)] public int Unknown2 { get; set; }

        [AoMember(3)] public int Unknown3 { get; set; }

        [AoMember(4)] public int Instance { get; set; }
    }

    #region AoMember Properties

    [AoMember(0)] public int Version { get; set; }

    [AoMember(1)] public Vector3 CharacterCoordinates { get; set; }

    [AoMember(2)] public byte TokenMarker { get; set; }

    [AoMember(3)] public Identity PlayfieldId1 { get; set; }

    [AoMember(4)] public int Group { get; set; }

    // The return playfield of a proxy instance (shop, house): the raw word 0xC0090000 | playfield
    // id, 0 when the zone-in carries none (plain zones, mission instances - owner, 2026-10-02;
    // verified on the saved 1187 zone-ins: one word each, the zone actually entered from). This is
    // where a proxy back exit leads - live server data, never remembered across sessions.
    [AoMember(5)] public int ProxyReturn { get; set; }

    /// <summary>The playfield to step out to (ProxyReturn's low half); 0 when none was sent.</summary>
    public int ReturnPlayfield => ProxyReturn & 0xFFFF;

    [AoMember(6)] public Identity ProxyId { get; set; }

    [AoFlags("flags")] [AoMember(7)] public int UnknownIdType { get; set; }

    [AoMember(8)] public int UnknownIdInstance { get; set; }

    [AoMember(9)] public int PlayfieldX { get; set; }

    [AoMember(10)] public int PlayfieldZ { get; set; }

    [AoUsesFlags("flags", typeof(UnknownStruct1), FlagsCriteria.EqualsToAny, 0xC77B)]
    [AoMember(11)]
    public UnknownStruct1 Unknown7 { get; set; }

    [AoUsesFlags("flags", typeof(PlayfieldDynelInfo[]), FlagsCriteria.EqualsToAny, 0xC77B, 0xC77D)]
    [AoMember(12, SerializeSize = ArraySizeType.Int32)]
    public PlayfieldDynelInfo[] Dynels { get; set; }

    #endregion
}