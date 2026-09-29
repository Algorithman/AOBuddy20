// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Int16Serializer.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the Int16Serializer type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Linq.Expressions;

namespace SmokeLounge.AOtomation.Messaging.Serialization.Serializers;

public class Int16Serializer : ISerializer
{
    #region Fields

    #endregion

    #region Constructors and Destructors

    public Int16Serializer()
    {
        Type = typeof(short);
    }

    #endregion

    #region Public Properties

    public Type Type { get; }

    #endregion

    #region Public Methods and Operators

    public object Deserialize(
        StreamReader streamReader,
        SerializationContext serializationContext,
        PropertyMetaData propertyMetaData = null)
    {
        return streamReader.ReadInt16();
    }

    public Expression DeserializerExpression(
        ParameterExpression streamReaderExpression,
        ParameterExpression serializationContextExpression,
        Expression assignmentTargetExpression,
        PropertyMetaData propertyMetaData)
    {
        var readMethodInfo = ReflectionHelper.GetMethodInfo<StreamReader, Func<short>>(o => o.ReadInt16);
        var callReadExp = Expression.Call(streamReaderExpression, readMethodInfo);
        if (assignmentTargetExpression.Type.IsAssignableFrom(Type))
        {
            return Expression.Assign(assignmentTargetExpression, callReadExp);
        }

        var assignmentExp = Expression.Assign(
            assignmentTargetExpression, Expression.Convert(callReadExp, assignmentTargetExpression.Type));
        return assignmentExp;
    }

    public void Serialize(
        StreamWriter streamWriter,
        SerializationContext serializationContext,
        object value,
        PropertyMetaData propertyMetaData = null)
    {
        streamWriter.WriteInt16((short)value);
    }

    public Expression SerializerExpression(
        ParameterExpression streamWriterExpression,
        ParameterExpression serializationContextExpression,
        Expression valueExpression,
        PropertyMetaData propertyMetaData)
    {
        var writeMethodInfo = ReflectionHelper.GetMethodInfo<StreamWriter, Action<short>>(o => o.WriteInt16);
        if (valueExpression.Type.IsAssignableFrom(Type))
        {
            return Expression.Call(streamWriterExpression, writeMethodInfo, valueExpression);
        }

        var callWriteExp = Expression.Call(
            streamWriterExpression,
            writeMethodInfo, Expression.Convert(valueExpression, Type));
        return callWriteExp;
    }

    #endregion
}