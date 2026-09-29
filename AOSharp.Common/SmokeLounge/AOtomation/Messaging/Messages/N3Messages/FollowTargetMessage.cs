// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FollowTargetMessage.cs" company="SmokeLounge">
//   Copyright © 2013 SmokeLounge.
//   This program is free software. It comes without any warranty, to
//   the extent permitted by applicable law. You can redistribute it
//   and/or modify it under the terms of the Do What The Fuck You Want
//   To Public License, Version 2, as published by Sam Hocevar. See
//   http://www.wtfpl.net/ for more details.
// </copyright>
// <summary>
//   Defines the FollowTargetMessage type.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

[AoContract((int)N3MessageType.FollowTarget)]
public class FollowTargetMessage : N3Message
{
    #region Constructors and Destructors

    public FollowTargetMessage()
    {
        N3MessageType = N3MessageType.FollowTarget;
    }

    #endregion

    [AoMember(2)]
    [AoUsesFlags("Type", typeof(PathInfo), FlagsCriteria.EqualsToAny, (int)FollowTargetType.NpcPath)]
    [AoUsesFlags("Type", typeof(TargetInfo), FlagsCriteria.EqualsToAny, (int)FollowTargetType.Target)]
    public IInfo Info { get; set; }


    public interface IInfo
    {
    }

    // 25 bytes plus the coordinates: identity, a float the client's reader (0x10073740) takes as
    // a float - the distance kept from the target, going by GamecodeUnk.FollowTarget(vehicle,
    // dynel, float distance, waypoints) - the target's position, then a byte-counted Vector3[].
    public class TargetInfo : IInfo
    {
        [AoMember(0)] public Identity Target { get; set; }

        [AoMember(1)] public float Distance { get; set; }

        [AoMember(2)] public Vector3 TargetPosition { get; set; }

        [AoMember(3, SerializeSize = ArraySizeType.Byte)]
        public Vector3[] Coordinates { get; set; }
    }

    // Always two points: where the mover is now and where it is heading.
    public class PathInfo : IInfo
    {
        [AoMember(0, SerializeSize = ArraySizeType.Byte)]
        public Vector3[] Waypoints { get; set; }

        public Vector3 Current => Waypoints[0];

        public Vector3 End => Waypoints[Waypoints.Length - 1];
    }

    #region AoMember Properties

    [AoMember(0)] [AoFlags("Type")] public FollowTargetType Type { get; set; }

    [AoMember(1)] public byte MoveMode { get; set; }

    #endregion
}

public enum FollowTargetType : byte
{
    NpcPath = 1,
    Target = 2,
}