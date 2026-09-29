// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ResearchUpdateMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the ResearchUpdateMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.ResearchUpdate)]
public class ResearchUpdateMessage : N3Message
{
    #region Constructors and Destructors

    public ResearchUpdateMessage()
    {
        N3MessageType = N3MessageType.ResearchUpdate;
    }

    #endregion

    #region AoMember Properties

    [AoMember(0)] public byte Unknown1 { get; set; }

    [AoMember(1, FixedSizeLength = 54, IsFixedSize = true, SerializeSize = ArraySizeType.NoSerialization)]
    public ResearchLine[] ResearchLine { get; set; }

    [AoMember(1)] public int Unknown2 { get; set; }

    #endregion
}