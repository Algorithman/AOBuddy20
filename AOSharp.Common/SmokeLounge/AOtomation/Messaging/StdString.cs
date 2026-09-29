// ---------------------------------------------------------------------------------------
// Solution: AOBuddy20
// Project: AOSharp.Common
// Filename: StdString.cs
// 
// Last modified: 2026-09-29 12:35
// Created:       2026-09-29 12:09
// 
// Long live OmniCell and AOBuddy20
// ---------------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using System.Text;
using AOSharp.Common.Unmanaged.Imports;

namespace AOSharp.Common.SmokeLounge.AOtomation.Messaging;

public class StdString : IDisposable
{
    public readonly IntPtr Pointer;
    private readonly bool _shouldDispose;
    private bool _disposedValue;

    internal StdString(IntPtr pointer, bool shouldDispose = true)
    {
        Pointer = pointer;
        _shouldDispose = shouldDispose;
    }

    public unsafe int Length => ((StdStringStruct*)Pointer)->Length;

    public void Dispose()
    {
        if (!_shouldDispose)
        {
            return;
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public override int GetHashCode()
    {
        return ToString().GetHashCode();
    }

    public static StdString FromPointer(IntPtr pointer, bool shouldDispose = true)
    {
        return new StdString(pointer, shouldDispose);
    }

    public static StdString Create()
    {
        return Create(string.Empty);
    }

    public static StdString Create(string str)
    {
        var bytes = Encoding.ASCII.GetBytes(str);
        return new StdString(String_c.Constructor(MSVCR100.New(0x14), bytes, bytes.Length));
    }

    public override unsafe string ToString()
    {
        return ((StdStringStruct*)Pointer)->ToString();
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(obj, null))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return ToString() == obj.ToString();
    }

    public static bool operator ==(StdString str1, StdString str2)
    {
        if (ReferenceEquals(str1, null))
        {
            if (ReferenceEquals(str2, null))
            {
                return true;
            }

            return false;
        }

        return str1.Equals(str2);
    }

    public static bool operator !=(StdString str1, StdString str2)
    {
        return !(str1 == str2);
    }

    public static bool operator ==(StdString str1, string str2)
    {
        if (ReferenceEquals(str1, null))
        {
            if (ReferenceEquals(str2, null))
            {
                return true;
            }

            return false;
        }

        return str1.Equals(str2);
    }

    public static bool operator !=(StdString str1, string str2)
    {
        return !(str1 == str2);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            String_c.Deconstructor(Pointer);
            MSVCR100.Delete(Pointer);
            _disposedValue = true;
        }
    }

    ~StdString()
    {
        if (_shouldDispose)
        {
            Dispose(false);
        }
    }
}

[StructLayout(LayoutKind.Explicit, Pack = 0)]
public unsafe struct StdStringStruct
{
    [FieldOffset(0)] private fixed byte _shortBuffer[16];
    [FieldOffset(0)] private byte* _pLongBuffer;
    [FieldOffset(16)] public int Length;

    public override string ToString()
    {
        if (Length < 16)
        {
            fixed (byte* bytes = _shortBuffer)
            {
                return Encoding.ASCII.GetString(bytes, Length);
            }
        }

        return Encoding.ASCII.GetString(_pLongBuffer, Length);
    }
}