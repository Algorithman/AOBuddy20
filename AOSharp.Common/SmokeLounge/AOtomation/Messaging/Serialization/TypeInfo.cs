// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TypeInfo.cs" company="SmokeLounge">
//   Copyright � 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the TypeInfo type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Reflection;
using SmokeLounge.AOtomation.Messaging.Exceptions;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public class TypeInfo
{
    #region Constructors and Destructors

    public TypeInfo(Type type)
    {
        this.Type = type;
        subTypes = new Dictionary<int, TypeInfo>();

        var knownTypes =
            this.Type.GetCustomAttributes(typeof(AoKnownTypeAttribute), false)
                .Cast<AoKnownTypeAttribute>()
                .FirstOrDefault();
        if (knownTypes != null)
        {
            KnownType = new KnownType(knownTypes.Offset, knownTypes.IdentifierType);
        }

        InitializeSubTypes();
    }

    #endregion

    #region Public Methods and Operators

    public TypeInfo GetSubType(int identifier)
    {
        TypeInfo typeInfo;
        subTypes.TryGetValue(identifier, out typeInfo);
        return typeInfo;
    }

    #endregion

    #region Fields

    private readonly Dictionary<int, TypeInfo> subTypes;

    #endregion

    #region Public Properties

    public KnownType KnownType { get; set; }

    public Type Type { get; }

    #endregion

    #region Methods

    private void InitializeSubTypes()
    {
        InitializeSubTypesForAssembly(Type.Assembly);
    }

    public void InitializeSubTypesForAssembly(Assembly assembly)
    {
        var types = assembly.GetTypes().Where(t => t.BaseType == Type);
        foreach (var subType in types)
        {
            var contract =
                subType.GetCustomAttributes(typeof(AoContractAttribute), false)
                    .Cast<AoContractAttribute>()
                    .FirstOrDefault();

            if (contract == null)
            {
                continue;
            }

            var typeInfo = new TypeInfo(subType);

            if (subTypes.ContainsKey(contract.Identifier))
            {
                throw new ContractIdCollisionException(
                    $"Contracts must have unique identifiers. {typeInfo.Type.Name}({contract.Identifier}) shares the same identifier as {subTypes[contract.Identifier].Type.Name}({contract.Identifier})");
            }

            subTypes.Add(contract.Identifier, typeInfo);
        }
    }

    #endregion
}