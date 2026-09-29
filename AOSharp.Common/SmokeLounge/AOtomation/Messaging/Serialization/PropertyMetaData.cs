// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PropertyMetaData.cs" company="SmokeLounge">
//   Copyright � 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the PropertyMetaData type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Reflection;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public class PropertyMetaData
{
    #region Constructors and Destructors

    public PropertyMetaData(
        PropertyInfo propertyInfo,
        AoMemberAttribute memberAttribute,
        AoFlagsAttribute flagsAttribute,
        AoUsesFlagsAttribute[] usesFlagsAttributes)
    {
        this.Property = propertyInfo;
        this.FlagsAttribute = flagsAttribute;
        this.UsesFlagsAttributes = usesFlagsAttributes;
        Options = new MemberOptions(
            this.Property.PropertyType,
            memberAttribute.IsFixedSize,
            memberAttribute.FixedSizeLength,
            memberAttribute.SerializeSize,
            memberAttribute.PadAfter,
            memberAttribute.PadBefore,
            usesFlagsAttributes);
    }

    #endregion

    #region Fields

    #endregion

    #region Public Properties

    public AoFlagsAttribute FlagsAttribute { get; }

    public MemberOptions Options { get; }

    public PropertyInfo Property { get; }

    public Type Type => Property.PropertyType;

    public AoUsesFlagsAttribute[] UsesFlagsAttributes { get; }

    #endregion
}