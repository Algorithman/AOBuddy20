// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CharacterDeletedMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the CharacterDeletedMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;

[AoContract((int)SystemMessageType.CharacterDeleted)]
public class CharacterDeletedMessage : SystemMessage
{
    #region Constructors and Destructors

    public CharacterDeletedMessage()
    {
        SystemMessageType = SystemMessageType.CharacterDeleted;
    }

    #endregion

    #region AoMember Properties

    [AoMember(0)] public int CharacterId { get; set; }

    #endregion
}