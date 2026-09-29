// --------------------------------------------------------------------------------------------------------------------
// <copyright file="StreamWriter.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the StreamWriter type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public sealed class StreamWriter : IDisposable
{
    #region Constructors and Destructors

    public StreamWriter(Stream stream)
    {
        this.stream = stream;
        writer = new BinaryWriter(this.stream);
    }

    #endregion

    #region Public Properties

    public long Position
    {
        get => stream.Position;

        set => stream.Position = value;
    }

    #endregion

    #region Fields

    private readonly Stream stream;

    private readonly BinaryWriter writer;

    #endregion

    #region Public Methods and Operators

    public void Dispose()
    {
        writer.Dispose();
        stream.Dispose();
    }

    public void WriteBool(bool value)
    {
        writer.Write(value);
    }

    public void WriteByte(byte value)
    {
        writer.Write(value);
    }

    public void WriteBytes(byte[] buffer)
    {
        writer.Write(buffer);
    }

    public void WriteInt16(short value)
    {
        writer.Write(IPAddress.HostToNetworkOrder(value));
    }

    public void WriteInt32(int value)
    {
        writer.Write(IPAddress.HostToNetworkOrder(value));
    }

    public void WriteInt64(long value)
    {
        writer.Write(IPAddress.HostToNetworkOrder(value));
    }

    public void WriteSingle(float value)
    {
        var single = BitConverter.GetBytes(value);
        Array.Reverse(single);
        writer.Write(single);
    }

    public void WriteDouble(double value)
    {
        writer.Write(value);
    }

    public void WriteString(string str, int? padToLength = null)
    {
        var bytes = new byte[padToLength ?? str.Length];
        var length = str.Length > bytes.Length ? bytes.Length : str.Length;
        Encoding.ASCII.GetBytes(str, 0, length, bytes, 0);
        writer.Write(bytes);
    }

    public void WriteUInt16(ushort value)
    {
        var bigEndian = IPAddress.HostToNetworkOrder(value) >> 16;
        writer.Write((ushort)bigEndian);
    }

    public void WriteUInt32(uint value)
    {
        var bigEndian = IPAddress.HostToNetworkOrder(value) >> 32;
        writer.Write((uint)bigEndian);
    }

    #endregion
}