// --------------------------------------------------------------------------------------------------------------------
// <copyright file="TypeSerializer.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the TypeSerializer type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Linq.Expressions;

namespace SmokeLounge.AOtomation.Messaging.Serialization.Serializers;

public class TypeSerializer : ISerializer
{
    #region Constructors and Destructors

    public TypeSerializer(Type type, TypeSerializerBuilder typeSerializerBuilder)
    {
        this.Type = type;
        this.typeSerializerBuilder = typeSerializerBuilder;
        serializerExpression = new Lazy<Expression>(BuildSerializerExpression);
        lazySerializerLambda =
            new Lazy<Action<StreamWriter, SerializationContext, object>>(CompileSerializer);
        deserializerExpression = new Lazy<Expression>(BuildDeserializerExpression);
        lazyDeserializerLambda =
            new Lazy<Func<StreamReader, SerializationContext, object>>(CompileDeserializer);
    }

    #endregion

    #region Public Properties

    public Type Type { get; }

    #endregion

    #region Fields

    private readonly Lazy<Expression> deserializerExpression;

    private readonly Lazy<Func<StreamReader, SerializationContext, object>> lazyDeserializerLambda;

    private readonly Lazy<Action<StreamWriter, SerializationContext, object>> lazySerializerLambda;

    private readonly Lazy<Expression> serializerExpression;

    private readonly TypeSerializerBuilder typeSerializerBuilder;

    #endregion

    #region Properties

    private Func<StreamReader, SerializationContext, object> DeserializerLambda => lazyDeserializerLambda.Value;

    private Action<StreamWriter, SerializationContext, object> SerializerLambda => lazySerializerLambda.Value;

    #endregion

    #region Public Methods and Operators

    public object Deserialize(
        StreamReader streamReader,
        SerializationContext serializationContext,
        PropertyMetaData propertyMetaData = null)
    {
        return DeserializerLambda(streamReader, serializationContext);
    }

    public Expression DeserializerExpression(
        ParameterExpression streamReaderExpression,
        ParameterExpression serializationContextExpression,
        Expression assignmentTargetExpression,
        PropertyMetaData propertyMetaData)
    {
        var invokeExp = Expression.Invoke(
            deserializerExpression.Value, streamReaderExpression, serializationContextExpression);
        var assignExp = Expression.Assign(assignmentTargetExpression, Expression.Convert(invokeExp, Type));
        return assignExp;
    }

    public void Serialize(
        StreamWriter streamWriter,
        SerializationContext serializationContext,
        object value,
        PropertyMetaData propertyMetaData = null)
    {
        SerializerLambda(streamWriter, serializationContext, value);
    }

    public Expression SerializerExpression(
        ParameterExpression streamWriterExpression,
        ParameterExpression serializationContextExpression,
        Expression valueExpression,
        PropertyMetaData propertyMetaData)
    {
        var invokeExp = Expression.Invoke(
            serializerExpression.Value, streamWriterExpression, serializationContextExpression, valueExpression);
        return invokeExp;
    }

    #endregion

    #region Methods

    private Expression BuildDeserializerExpression()
    {
        var readerParam = Expression.Parameter(typeof(StreamReader), "streamReader");
        var optionsParam = Expression.Parameter(typeof(SerializationContext), "serializationContext");

        var expression = typeSerializerBuilder.BuildDeserializer(readerParam, optionsParam);
        return expression;
    }

    private Expression BuildSerializerExpression()
    {
        var writerParam = Expression.Parameter(typeof(StreamWriter), "streamWriter");
        var optionsParam = Expression.Parameter(typeof(SerializationContext), "serializationContext");

        var expression = typeSerializerBuilder.BuildSerializer(writerParam, optionsParam);
        return expression;
    }

    private Func<StreamReader, SerializationContext, object> CompileDeserializer()
    {
        var lambda = (Expression<Func<StreamReader, SerializationContext, object>>)deserializerExpression.Value;
        var compiledLambda = lambda.Compile();
        return compiledLambda;
    }

    private Action<StreamWriter, SerializationContext, object> CompileSerializer()
    {
        var lambda = (Expression<Action<StreamWriter, SerializationContext, object>>)serializerExpression.Value;
        var compiledLambda = lambda.Compile();
        return compiledLambda;
    }

    #endregion
}