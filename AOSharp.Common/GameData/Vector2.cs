using Newtonsoft.Json;

namespace AOSharp.Common.GameData;

public struct Vector2
{
    public float X;
    public float Y;

    [JsonIgnore] public Vector2 YX => new Vector2(Y, X);

    public Vector2(double x, double y)
    {
        X = (float)x;
        Y = (float)y;
    }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return X;
                case 1: return Y;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector2 index!");
            }
        }

        set
        {
            switch (index)
            {
                case 0: X = value; break;
                case 1: Y = value; break;
                default:
                    throw new IndexOutOfRangeException("Invalid Vector2 index!");
            }
        }
    }

    [JsonIgnore] public float Magnitude => Mathf.Sqrt(X * X + Y * Y);

    [JsonIgnore] public float SqrMagnitude => X * X + Y * Y;

    public void Set(float newX, float newY)
    {
        X = newX;
        Y = newY;
    }

    public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Vector2(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)
    {
        return new Vector2(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
    {
        // avoid vector ops because current scripting backends are terrible at inlining
        var toVector_x = target.X - current.X;
        var toVector_y = target.Y - current.Y;

        var sqDist = toVector_x * toVector_x + toVector_y * toVector_y;

        if (sqDist == 0 || (maxDistanceDelta >= 0 && sqDist <= maxDistanceDelta * maxDistanceDelta))
        {
            return target;
        }

        var dist = Mathf.Sqrt(sqDist);

        return new Vector2(current.X + toVector_x / dist * maxDistanceDelta,
            current.X + toVector_y / dist * maxDistanceDelta);
    }

    public static Vector2 Scale(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X * b.X, a.Y * b.Y);
    }

    public void Scale(Vector2 scale)
    {
        X *= scale.X;
        Y *= scale.Y;
    }

    // Makes this vector have a ::ref::magnitude of 1.
    public void Normalize()
    {
        var mag = Magnitude;
        if (mag > kEpsilon)
        {
            this = this / mag;
        }
        else
        {
            this = Zero;
        }
    }

    [JsonIgnore]
    public Vector2 Normalized
    {
        get
        {
            var v = new Vector2(X, Y);
            v.Normalize();
            return v;
        }
    }

    public override int GetHashCode()
    {
        return X.GetHashCode() ^ (Y.GetHashCode() << 2);
    }

    public override bool Equals(object other)
    {
        if (other is Vector2 v)
        {
            return Equals(v);
        }

        return false;
    }

    public bool Equals(Vector2 other)
    {
        return X == other.X && Y == other.Y;
    }

    public static Vector2 AngleToVector(float angle, float mag = 1f)
    {
        var rads = Mathf.PI * angle / 180;
        return new Vector2(mag * Mathf.Sin(rads), mag * Mathf.Sin(rads));
    }

    public float DistanceFrom(Vector2 v)
    {
        return Mathf.Sqrt(Mathf.Pow(Mathf.Abs(X - v.X), 2) + Mathf.Pow(Mathf.Abs(Y - v.Y), 2));
    }

    public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
    {
        var factor = -2f * Dot(inNormal, inDirection);
        return new Vector2(factor * inNormal.X + inDirection.X, factor * inNormal.Y + inDirection.Y);
    }

    public static Vector2 Perpendicular(Vector2 inDirection)
    {
        return new Vector2(-inDirection.Y, inDirection.X);
    }

    public static float Dot(Vector2 lhs, Vector2 rhs)
    {
        return lhs.X * rhs.X + lhs.Y * rhs.Y;
    }

    public static float Angle(Vector2 from, Vector2 to)
    {
        var denominator = Mathf.Sqrt(from.SqrMagnitude * to.SqrMagnitude);
        if (denominator < kEpsilonNormalSqrt)
        {
            return 0F;
        }

        var dot = Mathf.Clamp(Dot(from, to) / denominator, -1F, 1F);
        return Mathf.Acos(dot) * Mathf.Rad2Deg;
    }

    public static float SignedAngle(Vector2 from, Vector2 to)
    {
        var unsigned_angle = Angle(from, to);
        var sign = Mathf.Sign(from.X * to.Y - from.Y * to.X);
        return unsigned_angle * sign;
    }

    public static float Distance(Vector2 a, Vector2 b)
    {
        var diff_x = a.X - b.X;
        var diff_y = a.Y - b.Y;
        return Mathf.Sqrt(diff_x * diff_x + diff_y * diff_y);
    }

    public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
    {
        var sqrMagnitude = vector.SqrMagnitude;
        if (sqrMagnitude > maxLength * maxLength)
        {
            var mag = Mathf.Sqrt(sqrMagnitude);

            var normalized_x = vector.X / mag;
            var normalized_y = vector.Y / mag;
            return new Vector2(normalized_x * maxLength,
                normalized_y * maxLength);
        }

        return vector;
    }

    public static Vector2 Min(Vector2 lhs, Vector2 rhs)
    {
        return new Vector2(Mathf.Min(lhs.X, rhs.X), Mathf.Min(lhs.Y, rhs.Y));
    }

    public static Vector2 Max(Vector2 lhs, Vector2 rhs)
    {
        return new Vector2(Mathf.Max(lhs.X, rhs.X), Mathf.Max(lhs.Y, rhs.Y));
    }


    public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime, float deltaTime)
    {
        var maxSpeed = Mathf.Infinity;
        return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
    }

    public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
    {
        smoothTime = Mathf.Max(0.0001F, smoothTime);
        var omega = 2F / smoothTime;

        var x = omega * deltaTime;
        var exp = 1F / (1F + x + 0.48F * x * x + 0.235F * x * x * x);

        var change_x = current.X - target.X;
        var change_y = current.Y - target.Y;
        var originalTo = target;

        // Clamp maximum speed
        var maxChange = maxSpeed * smoothTime;

        var maxChangeSq = maxChange * maxChange;
        var sqDist = change_x * change_x + change_y * change_y;
        if (sqDist > maxChangeSq)
        {
            var mag = Mathf.Sqrt(sqDist);
            change_x = change_x / mag * maxChange;
            change_y = change_y / mag * maxChange;
        }

        target.X = current.X - change_x;
        target.Y = current.Y - change_y;

        var temp_x = (currentVelocity.X + omega * change_x) * deltaTime;
        var temp_y = (currentVelocity.Y + omega * change_y) * deltaTime;

        currentVelocity.X = (currentVelocity.X - omega * temp_x) * exp;
        currentVelocity.Y = (currentVelocity.Y - omega * temp_y) * exp;

        var output_x = target.X + (change_x + temp_x) * exp;
        var output_y = target.Y + (change_y + temp_y) * exp;

        var origMinusCurrent_x = originalTo.X - current.X;
        var origMinusCurrent_y = originalTo.Y - current.Y;
        var outMinusOrig_x = output_x - originalTo.X;
        var outMinusOrig_y = output_y - originalTo.Y;

        if (origMinusCurrent_x * outMinusOrig_x + origMinusCurrent_y * outMinusOrig_y > 0)
        {
            output_x = originalTo.X;
            output_y = originalTo.Y;

            currentVelocity.X = (output_x - originalTo.X) / deltaTime;
            currentVelocity.Y = (output_y - originalTo.Y) / deltaTime;
        }

        return new Vector2(output_x, output_y);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }

    public Vector3 ToVector3()
    {
        return new Vector3(X, Y, 0);
    }

    public static Vector2 operator +(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X + b.X, a.Y + b.Y);
    }

    public static Vector2 operator -(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X - b.X, a.Y - b.Y);
    }

    public static Vector2 operator *(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X * b.X, a.Y * b.Y);
    }

    public static Vector2 operator /(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X / b.X, a.Y / b.Y);
    }

    public static Vector2 operator -(Vector2 a)
    {
        return new Vector2(-a.X, -a.Y);
    }

    public static Vector2 operator *(Vector2 a, float d)
    {
        return new Vector2(a.X * d, a.Y * d);
    }

    public static Vector2 operator *(float d, Vector2 a)
    {
        return new Vector2(a.X * d, a.Y * d);
    }

    public static Vector2 operator /(Vector2 a, float d)
    {
        return new Vector2(a.X / d, a.Y / d);
    }

    public static bool operator ==(Vector2 lhs, Vector2 rhs)
    {
        var diff_x = lhs.X - rhs.X;
        var diff_y = lhs.Y - rhs.Y;
        return diff_x * diff_x + diff_y * diff_y < kEpsilon * kEpsilon;
    }

    public static bool operator !=(Vector2 lhs, Vector2 rhs)
    {
        return !(lhs == rhs);
    }

    public static implicit operator Vector2(Vector3 v)
    {
        return new Vector2(v.X, v.Y);
    }

    public static implicit operator Vector3(Vector2 v)
    {
        return new Vector3(v.X, v.Y, 0);
    }

    public static Vector2 Zero { get; } = new Vector2(0F, 0F);

    public static Vector2 One { get; } = new Vector2(1F, 1F);

    public static Vector2 Up { get; } = new Vector2(0F, 1F);

    public static Vector2 Down { get; } = new Vector2(0F, -1F);

    public static Vector2 Left { get; } = new Vector2(-1F, 0F);

    public static Vector2 Right { get; } = new Vector2(1F, 0F);

    public static Vector2 PositiveInfinity { get; } = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

    public static Vector2 NegativeInfinity { get; } = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

    private const float kEpsilon = 0.00001F;

    private const float kEpsilonNormalSqrt = 1e-15f;
}