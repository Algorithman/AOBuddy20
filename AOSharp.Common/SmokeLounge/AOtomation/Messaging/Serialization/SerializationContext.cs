// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SerializationContext.cs" company="SmokeLounge">
//   Copyright � 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the SerializationContext type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public class SerializationContext
{
    #region Constructors and Destructors

    public SerializationContext(SerializerResolver serializerResolver)
    {
        this.serializerResolver = serializerResolver;
        diagnosticInfos = new List<DiagnosticInfo>();
        flags = new Dictionary<string, int>();
    }

    #endregion

    #region Public Properties

    public IEnumerable<DiagnosticInfo> DiagnosticInfos => diagnosticInfos;

    #endregion

    #region Fields

    private readonly List<DiagnosticInfo> diagnosticInfos;

    private readonly IDictionary<string, int> flags;

    private readonly SerializerResolver serializerResolver;

    private Probe probe;

    #endregion

    #region Public Methods and Operators

    public Probe BeginProbe()
    {
        probe = new Probe(probe);
        return probe;
    }

    public void EndProbe(Probe probe)
    {
        this.probe = probe.Parent;
        if (this.probe != null)
        {
            this.probe.DiagnosticInfo.Add(probe.DiagnosticInfo);
        }
        else
        {
            diagnosticInfos.Add(probe.DiagnosticInfo);
        }
    }

    public AoUsesFlagsAttribute Evaluate(IEnumerable<AoUsesFlagsAttribute> usesFlags)
    {
        return usesFlags.FirstOrDefault(Evaluate);
    }

    public int GetFlagValue(string flag)
    {
        int value;
        flags.TryGetValue(flag, out value);
        return value;
    }

    public void SetFlagValue(string flag, int value)
    {
        flags[flag] = value;
    }

    #endregion

    #region Methods

    internal object Deserialize(StreamReader streamReader, PropertyMetaData propertyMetaData)
    {
        if (!propertyMetaData.UsesFlagsAttributes.Any())
        {
            return null;
        }

        var usesFlag = Evaluate(propertyMetaData.UsesFlagsAttributes);
        if (usesFlag == null)
        {
            return null;
        }

        var serializer = serializerResolver.GetSerializer(usesFlag.Type);
        var value = serializer.Deserialize(streamReader, this, propertyMetaData);

        if (propertyMetaData.Type.IsValueType)
        {
            if (propertyMetaData.Type.IsPrimitive)
            {
                return Convert.ChangeType(value, propertyMetaData.Type);
            }

            if (propertyMetaData.Type.IsEnum)
            {
                return Convert.ChangeType(value, Enum.GetUnderlyingType(propertyMetaData.Type));
            }
        }

        return value;
    }

    internal void Serialize(StreamWriter streamWriter, object obj, PropertyMetaData propertyMetaData)
    {
        ISerializer serializer;
        if (!propertyMetaData.UsesFlagsAttributes.Any())
        {
            serializer = serializerResolver.GetSerializer(obj.GetType());
        }
        else
        {
            var usesFlag = Evaluate(propertyMetaData.UsesFlagsAttributes);
            if (usesFlag == null)
            {
                return;
            }

            serializer = serializerResolver.GetSerializer(usesFlag.Type);
        }

        if (propertyMetaData.Type.IsValueType)
        {
            if (propertyMetaData.Type.IsPrimitive || propertyMetaData.Type.IsEnum)
            {
                obj = Convert.ChangeType(obj, serializer.Type);
            }
        }

        serializer.Serialize(streamWriter, this, obj, propertyMetaData);
    }

    private bool Evaluate(AoUsesFlagsAttribute usesFlags)
    {
        switch (usesFlags.Criteria)
        {
            case FlagsCriteria.HasAll:
                return EvaluateHasAll(usesFlags);
            case FlagsCriteria.HasAny:
                return EvaluateHasAny(usesFlags);
            case FlagsCriteria.EqualsToAny:
                return EvaluateEqualsToAny(usesFlags);
            case FlagsCriteria.Default:
                return true;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private bool EvaluateEqualsToAny(AoUsesFlagsAttribute usesFlags)
    {
        var flagValue = GetFlagValue(usesFlags.Flag);
        return usesFlags.CriteriaValues.Any(v => v == flagValue);
    }

    private bool EvaluateHasAll(AoUsesFlagsAttribute usesFlags)
    {
        var flagValue = GetFlagValue(usesFlags.Flag);
        return (flagValue & usesFlags.CriteriaValue) == usesFlags.CriteriaValue;
    }

    private bool EvaluateHasAny(AoUsesFlagsAttribute usesFlags)
    {
        var flagValue = GetFlagValue(usesFlags.Flag);
        return (flagValue & usesFlags.CriteriaValue) > 0;
    }

    #endregion
}