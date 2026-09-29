// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ChatHeaderSerializer.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the ChatHeaderSerializer type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Linq.Expressions;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace SmokeLounge.AOtomation.Messaging.Serialization.Serializers;

public class ChatHeaderSerializer : ISerializer
{
    #region Fields

    #endregion

    #region Constructors and Destructors

    public ChatHeaderSerializer()
    {
        Type = typeof(Header);
        SerializerLambda =
            (streamWriter, serializationContext, value) => Serialize(streamWriter, serializationContext, value);
        DeserializerLambda =
            (streamReader, serializationContext) => Deserialize(streamReader, serializationContext);
    }

    #endregion

    #region Public Properties

    public Func<StreamReader, SerializationContext, object> DeserializerLambda { get; private set; }

    public Action<StreamWriter, SerializationContext, object> SerializerLambda { get; private set; }

    public Type Type { get; }

    #endregion

    #region Public Methods and Operators

    public object Deserialize(
        StreamReader streamReader,
        SerializationContext serializationContext,
        PropertyMetaData propertyMetaData = null)
    {
        var header = new ChatHeader();
        header.PacketType = (ChatMessageType)streamReader.ReadInt16();
        header.Size = streamReader.ReadUInt16();
        return header;
    }

    public Expression DeserializerExpression(
        ParameterExpression streamReaderExpression,
        ParameterExpression serializationContextExpression,
        Expression assignmentTargetExpression,
        PropertyMetaData propertyMetaData)
    {
        throw new NotImplementedException();
    }

    public void Serialize(
        StreamWriter streamWriter,
        SerializationContext serializationContext,
        object value,
        PropertyMetaData propertyMetaData = null)
    {
        var header = (ChatHeader)value;
        streamWriter.WriteInt16((short)header.PacketType);
        streamWriter.WriteUInt16(header.Size);
    }

    public Expression SerializerExpression(
        ParameterExpression streamWriterExpression,
        ParameterExpression serializationContextExpression,
        Expression valueExpression,
        PropertyMetaData propertyMetaData)
    {
        throw new NotImplementedException();
    }

    #endregion
}