using System;
using System.Text;
using System.Runtime.InteropServices;

namespace AOSharp.Common.GameData
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ResearchGoal
    {
        public int ResearchId;
        [MarshalAs(UnmanagedType.I1)]
        public bool Available;
    }
}
