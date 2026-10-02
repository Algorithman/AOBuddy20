// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FormatFeedbackMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the FormatFeedbackMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using AOSharp.Common.SmokeLounge.AOtomation.Messaging;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.FormatFeedback)]
public class FormatFeedbackMessage : N3Message
{
    private string _formattedMessage;

    #region Constructors and Destructors

    public FormatFeedbackMessage()
    {
        N3MessageType = N3MessageType.FormatFeedback;
    }

    #endregion

    // Formatted in managed code from the '~&' ext string and the mmdb templates embedded in
    // MmdbData. The old getter called ldb.dll's RemoteFormat::ParseString, which only worked
    // inside the game process and threw clientless.
    public string FormattedMessage
    {
        get
        {
            if (_formattedMessage == null)
            {
                _formattedMessage = ExtMessageFormatter.Format(Message);
            }

            return _formattedMessage;
        }
    }

    #region AoMember Properties

    [AoMember(0)] public int ChatCategory { get; set; }

    [AoMember(1, SerializeSize = ArraySizeType.Int16)]
    public string Message { get; set; }

    [AoMember(2)] public int PayloadKind { get; set; }

    #endregion
}
