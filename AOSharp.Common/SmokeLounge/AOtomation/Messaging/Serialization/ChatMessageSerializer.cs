// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ChatMessageSerializer.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the ChatMessageSerializer type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Serialization.Serializers;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public class ChatMessageSerializer
{
    #region Fields

    private readonly ChatHeaderSerializer headerSerializer;

    private readonly PacketInspector packetInspector;

    private readonly SerializerResolver serializerResolver;

    #endregion

    #region Constructors and Destructors

    public ChatMessageSerializer()
    {
        packetInspector = new PacketInspector(new TypeInfo(typeof(ChatMessageBody)));
        serializerResolver = new SerializerResolverBuilder<ChatMessageBody>().Build();
        headerSerializer = new ChatHeaderSerializer();
    }

    public ChatMessageSerializer(SerializerResolverBuilder serializerResolverBuilder)
    {
        packetInspector = new PacketInspector(new TypeInfo(typeof(ChatMessageBody)));
        serializerResolver = serializerResolverBuilder.Build();
        headerSerializer = new ChatHeaderSerializer();
    }

    #endregion

    #region Public Methods and Operators

    public ChatMessage Deserialize(Stream stream)
    {
        SerializationContext ignore;
        return Deserialize(stream, out ignore);
    }

    public ChatMessage Deserialize(byte[] message)
    {
        using (var buffer = new MemoryStream(message))
        {
            return Deserialize(buffer);
        }
    }

    public ChatMessage Deserialize(Stream stream, out SerializationContext serializationContext)
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
        var message = new ChatMessage
        {
            Header = (ChatHeader)headerSerializer.Deserialize(reader, serializationContext),
            Body = (ChatMessageBody)serializer.Deserialize(reader, serializationContext),
        };
        return message;
    }

    public void Serialize(Stream stream, ChatMessage message)
    {
        SerializationContext ignore;
        Serialize(stream, message, out ignore);
    }

    public void Serialize(Stream stream, ChatMessage message, out SerializationContext serializationContext)
    {
        serializationContext = null;
        var serializer = serializerResolver.GetSerializer(message.Body.GetType());
        if (serializer == null)
        {
            return;
        }

        serializationContext = new SerializationContext(serializerResolver);
        var writer = new StreamWriter(stream) { Position = 0, };
        headerSerializer.Serialize(writer, serializationContext, message.Header);
        serializer.Serialize(writer, serializationContext, message.Body);
        var length = writer.Position;
        writer.Position = 2;
        writer.WriteInt16((short)(length - 4));
    }

    #endregion
}