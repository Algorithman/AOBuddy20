// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ArraySizeSerializer.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the ArraySizeSerializer type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Linq.Expressions;
using System.Reflection;

namespace SmokeLounge.AOtomation.Messaging.Serialization.Serializers;

public class ArraySizeSerializer : ISerializer
{
    #region Constructors and Destructors

    public ArraySizeSerializer(ArraySizeType arraySizeType)
    {
        this.arraySizeType = arraySizeType;
        switch (this.arraySizeType)
        {
            case ArraySizeType.Byte:
                Type = typeof(byte);
                break;
            case ArraySizeType.Int16:
                Type = typeof(short);
                break;
            case ArraySizeType.Int32:
                Type = typeof(int);
                break;
            case ArraySizeType.X3F1:
                Type = typeof(int);
                break;
        }
    }

    #endregion

    #region Public Properties

    public Type Type { get; }

    #endregion

    #region Fields

    private readonly ArraySizeType arraySizeType;

    #endregion

    #region Public Methods and Operators

    public object Deserialize(
        StreamReader streamReader,
        SerializationContext serializationContext,
        PropertyMetaData propertyMetaData = null)
    {
        switch (arraySizeType)
        {
            case ArraySizeType.NoSerialization:
                return null;
            case ArraySizeType.Byte:
                return (int)streamReader.ReadByte();
            case ArraySizeType.Int16:
                return (int)streamReader.ReadInt16();
            case ArraySizeType.Int32:
                return streamReader.ReadInt32();
            case ArraySizeType.X3F1:
                var length3F1 = streamReader.ReadInt32();
                return length3F1 / 0x03F1 - 1;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public Expression DeserializerExpression(
        ParameterExpression streamReaderExpression,
        ParameterExpression serializationContextExpression,
        Expression assignmentTargetExpression,
        PropertyMetaData propertyMetaData)
    {
        if (arraySizeType == ArraySizeType.NoSerialization)
        {
            return null;
        }

        MethodInfo readMethodInfo = null;

        if (Type == typeof(byte))
        {
            readMethodInfo = ReflectionHelper.GetMethodInfo<StreamReader, Func<byte>>(o => o.ReadByte);
        }

        if (Type == typeof(short))
        {
            readMethodInfo = ReflectionHelper.GetMethodInfo<StreamReader, Func<short>>(o => o.ReadInt16);
        }

        if (Type == typeof(int))
        {
            readMethodInfo = ReflectionHelper.GetMethodInfo<StreamReader, Func<int>>(o => o.ReadInt32);
        }

        if (readMethodInfo == null)
        {
            return null;
        }

        Expression deserializedValueExpression;

        if (propertyMetaData.Options.IsFixedSize)
        {
            deserializedValueExpression = Expression.Constant(propertyMetaData.Options.FixedSizeLength, Type);
        }
        else
        {
            deserializedValueExpression = Expression.Call(streamReaderExpression, readMethodInfo);
        }

        if (arraySizeType == ArraySizeType.X3F1)
        {
            var originalValue = deserializedValueExpression;
            deserializedValueExpression =
                Expression.Subtract(
                    Expression.Divide(originalValue, Expression.Constant(0x03F1)), Expression.Constant(1));
        }

        var deserializerExpression = Expression.Assign(
            assignmentTargetExpression,
            Type == assignmentTargetExpression.Type
                ? deserializedValueExpression
                : Expression.Convert(deserializedValueExpression, assignmentTargetExpression.Type));

        return deserializerExpression;
    }

    public void Serialize(
        StreamWriter streamWriter,
        SerializationContext serializationContext,
        object value,
        PropertyMetaData propertyMetaData = null)
    {
        if (arraySizeType == ArraySizeType.NoSerialization)
        {
            return;
        }

        var array = value as Array;
        var length = array != null ? array.Length : ((string)value).Length;

        switch (arraySizeType)
        {
            case ArraySizeType.NoSerialization:
                break;
            case ArraySizeType.Byte:
                streamWriter.WriteByte((byte)length);
                break;
            case ArraySizeType.Int16:
                streamWriter.WriteInt16((short)length);
                break;
            case ArraySizeType.Int32:
                streamWriter.WriteInt32(length);
                break;
            case ArraySizeType.X3F1:
                streamWriter.WriteInt32((length + 1) * 0x03F1);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public Expression SerializerExpression(
        ParameterExpression streamWriterExpression,
        ParameterExpression serializationContextExpression,
        Expression valueExpression,
        PropertyMetaData propertyMetaData)
    {
        if (arraySizeType == ArraySizeType.NoSerialization)
        {
            return null;
        }

        MethodInfo writeMethodInfo = null;

        if (Type == typeof(byte))
        {
            writeMethodInfo = ReflectionHelper.GetMethodInfo<StreamWriter, Action<byte>>(o => o.WriteByte);
        }

        if (Type == typeof(short))
        {
            writeMethodInfo = ReflectionHelper.GetMethodInfo<StreamWriter, Action<short>>(o => o.WriteInt16);
        }

        if (Type == typeof(int))
        {
            writeMethodInfo = ReflectionHelper.GetMethodInfo<StreamWriter, Action<int>>(o => o.WriteInt32);
        }

        if (writeMethodInfo == null)
        {
            return null;
        }

        Expression serializedValueExpression;

        if (propertyMetaData.Options.IsFixedSize)
        {
            serializedValueExpression = Expression.Constant(propertyMetaData.Options.FixedSizeLength, Type);
        }
        else
        {
            serializedValueExpression = Expression.Convert(
                Expression.Property(valueExpression, "Length"), Type);
        }

        if (arraySizeType == ArraySizeType.X3F1)
        {
            var originalValue = serializedValueExpression;
            serializedValueExpression = Expression.Multiply(
                Expression.Add(originalValue, Expression.Constant(1)), Expression.Constant(0x03F1));
        }

        var serializerExpression = Expression.Call(
            streamWriterExpression, writeMethodInfo, serializedValueExpression);
        return serializerExpression;
    }

    #endregion
}