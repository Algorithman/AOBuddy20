// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MessageSerializer.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the MessageSerializer type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Serialization.Serializers;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public class MessageSerializer
{
    #region Fields

    private readonly HeaderSerializer headerSerializer;

    private readonly PacketInspector packetInspector;

    private readonly SerializerResolver serializerResolver;

    #endregion

    #region Constructors and Destructors

    public MessageSerializer()
    {
        packetInspector = new PacketInspector(new TypeInfo(typeof(MessageBody)));
        serializerResolver = new SerializerResolverBuilder<MessageBody>().Build();
        headerSerializer = new HeaderSerializer();
    }

    public MessageSerializer(SerializerResolverBuilder serializerResolverBuilder)
    {
        packetInspector = new PacketInspector(new TypeInfo(typeof(MessageBody)));
        serializerResolver = serializerResolverBuilder.Build();
        headerSerializer = new HeaderSerializer();
    }

    #endregion

    #region Public Methods and Operators

    public AOMessage Deserialize(Stream stream)
    {
        SerializationContext ignore;
        return Deserialize(stream, out ignore);
    }

    public AOMessage Deserialize(byte[] datablock)
    {
        using (var buffer = new MemoryStream(datablock))
        {
            return Deserialize(buffer);
        }
    }

    public AOMessage Deserialize(Stream stream, out SerializationContext serializationContext)
    {
        serializationContext = null;
        var reader = new StreamReader(stream) { Position = 0, };
        var subTypeInfo = packetInspector.FindSubType(reader, out var _);

        if (subTypeInfo == null)
        {
            return null;
        }

        var serializer = serializerResolver.GetSerializer(subTypeInfo.Type);
        if (serializer == null)
        {
            return null;
        }

        reader.Position = 0;
        serializationContext = new SerializationContext(serializerResolver);

        return new AOMessage
        {
            Header = (Header)headerSerializer.Deserialize(reader, serializationContext),
            Body = (MessageBody)serializer.Deserialize(reader, serializationContext),
            RawPacket = reader.ReadAll(),
        };
    }

    public MessageBody DeserializeDatablock(Stream stream)
    {
        SerializationContext serializationContext = null;

        using (var reader = new StreamReader(stream) { Position = 0, })
        {
            var subTypeInfo = packetInspector.FindSubType(reader, out var _);

            if (subTypeInfo == null)
            {
                return null;
            }

            var serializer = serializerResolver.GetSerializer(subTypeInfo.Type);
            if (serializer == null)
            {
                return null;
            }

            reader.Position = 16;
            serializationContext = new SerializationContext(serializerResolver);

            return (MessageBody)serializer.Deserialize(reader, serializationContext);
        }
    }

    public void Serialize(Stream stream, AOMessage aoMessage)
    {
        SerializationContext ignore;
        Serialize(stream, aoMessage, out ignore);
    }

    public void Serialize(Stream stream, AOMessage aoMessage, out SerializationContext serializationContext)
    {
        serializationContext = null;
        var serializer = serializerResolver.GetSerializer(aoMessage.Body.GetType());
        if (serializer == null)
        {
            return;
        }

        serializationContext = new SerializationContext(serializerResolver);
        var writer = new StreamWriter(stream) { Position = 0, };
        headerSerializer.Serialize(writer, serializationContext, aoMessage.Header);
        serializer.Serialize(writer, serializationContext, aoMessage.Body);

        var length = (int)writer.Position;
        var padding = length % 4 == 0 ? 0 : 4 - length % 4;

        //Padding
        for (var i = 0; i < padding; i++)
        {
            writer.WriteByte(0);
        }

        writer.Position = 6;
        writer.WriteInt16((short)length);
    }

    #endregion
}