// --------------------------------------------------------------------------------------------------------------------
// <copyright file="AoUsesFlagsAttribute.cs" company="SmokeLounge">
//   Copyright � 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the AoUsesFlagsAttribute type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public class AoUsesFlagsAttribute : Attribute
{
    #region Constructors and Destructors

    public AoUsesFlagsAttribute(string flag, Type type, FlagsCriteria criteria, params int[] criteriaValues)
    {
        this.Flag = flag;
        this.Type = type;
        this.Criteria = criteria;
        this.CriteriaValues = criteriaValues;

        foreach (var value in criteriaValues)
        {
            CriteriaValue |= value;
        }
    }

    #endregion

    #region Fields

    #endregion

    #region Public Properties

    public FlagsCriteria Criteria { get; }

    public int CriteriaValue { get; }

    public int[] CriteriaValues { get; }

    public string Flag { get; }

    public Type Type { get; }

    #endregion
}