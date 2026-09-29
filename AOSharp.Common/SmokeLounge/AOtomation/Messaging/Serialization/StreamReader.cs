// --------------------------------------------------------------------------------------------------------------------
// <copyright file="StreamReader.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the StreamReader type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Net;
using System.Text;

namespace SmokeLounge.AOtomation.Messaging.Serialization;

public sealed class StreamReader : IDisposable
{
    #region Constructors and Destructors

    public StreamReader(Stream stream)
    {
        this.stream = stream;
        reader = new BinaryReader(stream);
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

    private readonly BinaryReader reader;

    private readonly Stream stream;

    #endregion

    #region Public Methods and Operators

    public void Dispose()
    {
        reader.Dispose();
        stream.Dispose();
    }

    public bool ReadBool()
    {
        return reader.ReadBoolean();
    }

    public byte ReadByte()
    {
        return reader.ReadByte();
    }

    public byte[] ReadBytes(int count)
    {
        return reader.ReadBytes(count);
    }

    public short ReadInt16()
    {
        return IPAddress.NetworkToHostOrder(reader.ReadInt16());
    }

    public int ReadInt32()
    {
        return IPAddress.NetworkToHostOrder(reader.ReadInt32());
    }

    public long ReadInt64()
    {
        return IPAddress.NetworkToHostOrder(reader.ReadInt64());
    }

    public float ReadSingle()
    {
        var single = reader.ReadBytes(4);
        Array.Reverse(single);
        return BitConverter.ToSingle(single, 0);
    }

    public double ReadDouble()
    {
        return reader.ReadDouble();
    }

    public string ReadString(int length)
    {
        var bytes = reader.ReadBytes(length);
        return Encoding.ASCII.GetString(bytes).TrimEnd(char.MinValue);
    }

    public ushort ReadUInt16()
    {
        var littleEndian = reader.ReadUInt16() << 16;
        return (ushort)IPAddress.NetworkToHostOrder(littleEndian);
    }

    public uint ReadUInt32()
    {
        var littleEndian = reader.ReadUInt32() << 32;
        return (uint)(IPAddress.NetworkToHostOrder(littleEndian) >> 32);
    }

    public string ReadNullTerminatedString()
    {
        var str = "";
        byte ch;
        while ((ch = reader.ReadByte()) != 0)
        {
            str = str + Convert.ToChar(ch);
        }

        return str;
    }

    public int PeekNullTermStringLength()
    {
        var origin = reader.BaseStream.Position;
        while (reader.ReadByte() != 0)
        {
            ;
        }

        var length = reader.BaseStream.Position - origin;
        reader.BaseStream.Position = origin;
        return (int)length;
    }

    public int PeekUntilEnd()
    {
        var origin = reader.BaseStream.Position;
        var length = 0;
        while (reader.BaseStream.Position != reader.BaseStream.Length)
        {
            reader.ReadByte();
            length++;
        }

        reader.BaseStream.Position = origin;
        return length;
    }

    public byte[] ReadAll()
    {
        reader.BaseStream.Position = 0;
        return reader.ReadBytes((int)reader.BaseStream.Length);
    }

    #endregion
}