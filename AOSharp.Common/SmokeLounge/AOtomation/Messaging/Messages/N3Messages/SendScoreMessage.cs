// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SendScoreMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the SendScoreMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.SendScore)]
public class SendScoreMessage : N3Message
{
    #region Constructors and Destructors

    public SendScoreMessage()
    {
        N3MessageType = N3MessageType.SendScore;
    }

    #endregion

    #region AoMember Properties

    [AoMember(0)] public int Unknown1 { get; set; }

    [AoMember(1)] public BattlestationSide A { get; set; }

    [AoMember(2)] public BattlestationSide B { get; set; }

    [AoMember(3)] public BattlestationSide C { get; set; }

    [AoMember(4)] public BattlestationSide Core { get; set; }

    [AoMember(5)] public int Unknown2 { get; set; }

    [AoMember(6)] public int RedScore { get; set; }

    [AoMember(7)] public int BlueScore { get; set; }

    #endregion
}