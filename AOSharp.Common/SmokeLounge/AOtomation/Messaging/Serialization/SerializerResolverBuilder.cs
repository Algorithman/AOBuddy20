// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SerializerResolverBuilder.cs" company="SmokeLounge">
//   Copyright � 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the SerializerResolverBuilder type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Serialization.Serializers;
using SmokeLounge.AOtomation.Messaging.Serialization.Serializers.Custom;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public abstract class SerializerResolverBuilder
{
    #region Public Methods and Operators

    public abstract SerializerResolver Build();

    #endregion

    #region Methods

    internal abstract ISerializer GetSerializer(Type type);

    #endregion
}

public class SerializerResolverBuilder<T> : SerializerResolverBuilder
{
    #region Fields

    private readonly ConcurrentDictionary<Type, ISerializer> serializers;

    #endregion

    #region Constructors and Destructors

    public SerializerResolverBuilder()
    {
        serializers = new ConcurrentDictionary<Type, ISerializer>();
        serializers.TryAdd(typeof(bool), new BoolSerializer());
        serializers.TryAdd(typeof(byte), new ByteSerializer());
        serializers.TryAdd(typeof(short), new Int16Serializer());
        serializers.TryAdd(typeof(int), new Int32Serializer());
        serializers.TryAdd(typeof(long), new Int64Serializer());
        serializers.TryAdd(typeof(IPAddress), new IPAddressSerializer());
        serializers.TryAdd(typeof(float), new SingleSerializer());
        serializers.TryAdd(typeof(double), new DoubleSerializer());
        serializers.TryAdd(typeof(string), new StringSerializer());
        serializers.TryAdd(typeof(ushort), new UInt16Serializer());
        serializers.TryAdd(typeof(uint), new UInt32Serializer());
        serializers.TryAdd(typeof(PlayfieldVendorInfo), new PlayfieldVendorInfoSerializer());
        serializers.TryAdd(typeof(SimpleCharFullUpdateMessage), new SimpleCharFullUpdateSerializer());
        //this.serializers.TryAdd(typeof(GenericCmdMessage), new GenericCmdSerializer());
        serializers.TryAdd(typeof(GroupMsgMessage), new GroupMessageSerializer());
        serializers.TryAdd(typeof(PlayfieldTowerUpdateClientMessage), new PlayfieldTowerUpdateClientSerializer());
        serializers.TryAdd(typeof(LookupMessage), new LookupMessageSerializer());
        serializers.TryAdd(typeof(FriendStatusMessage), new FriendStatusSerializer());
        serializers.TryAdd(typeof(PrivateGroupMessage), new PrivateGroupMessageSerializer());
    }

    #endregion

    #region Public Methods and Operators

    public override SerializerResolver Build()
    {
        var rootType = typeof(T);

        var subTypes = rootType.Assembly.GetTypes().Where(rootType.IsAssignableFrom);

        foreach (var subType in subTypes)
        {
            if (serializers.ContainsKey(subType))
            {
                continue;
            }

            var serializer = CreateSerializer(subType);
            if (serializer != null)
            {
                serializers.TryAdd(subType, serializer);
            }
        }

        var serializationContext = new SerializerResolver(this);
        return serializationContext;
    }

    #endregion

    #region Methods

    internal override ISerializer GetSerializer(Type type)
    {
        ISerializer serializer;
        if (serializers.TryGetValue(type, out serializer))
        {
            return serializer;
        }

        if (type.IsEnum)
        {
            var enumType = type.GetEnumUnderlyingType();
            if (serializers.TryGetValue(enumType, out serializer))
            {
                return serializer;
            }
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            serializer = GetSerializer(elementType);
            if (serializer == null)
            {
                return null;
            }

            var arraySerializer = new ArraySerializer(type, serializer);
            serializers.TryAdd(type, arraySerializer);
            return arraySerializer;
        }

        serializer = CreateSerializer(type);
        if (serializer != null)
        {
            serializers.TryAdd(type, serializer);
        }

        return serializer;
    }

    private ISerializer CreateSerializer(Type type)
    {
        if (type.IsAbstract)
        {
            return null;
        }

        var typeSerializerBuilder = new TypeSerializerBuilder(type, GetSerializer);
        var typeSerializer = new TypeSerializer(type, typeSerializerBuilder);
        return typeSerializer;
    }

    #endregion
}