// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MemberOptions.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the MemberOptions type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public class MemberOptions
{
    #region Constructors and Destructors

    public MemberOptions(
        Type type,
        bool isFixedSize,
        int fixedSizeLength,
        ArraySizeType serializeSize,
        int padAfter,
        int padBefore,
        AoUsesFlagsAttribute[] usesFlagsAttributes)
    {
        this.Type = type;
        this.IsFixedSize = isFixedSize;
        this.FixedSizeLength = fixedSizeLength;
        this.SerializeSize = serializeSize;
        this.PadAfter = padAfter;
        this.PadBefore = padBefore;
        this.UsesFlagsAttributes = usesFlagsAttributes;
    }

    #endregion

    #region Fields

    #endregion

    #region Public Properties

    public int FixedSizeLength { get; }

    public bool IsFixedSize { get; }

    public int PadAfter { get; }

    public int PadBefore { get; }

    public ArraySizeType SerializeSize { get; }

    public Type Type { get; }

    public AoUsesFlagsAttribute[] UsesFlagsAttributes { get; }

    #endregion
}