using System;
using UnityEngine;

namespace WarmupScpSelector.Models
{
    /// <summary>Resolved position, rotation, and scale for one MER transform in world space.</summary>
    internal readonly struct MerWorldTransform
    {
        public MerWorldTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Scale { get; }
    }

    /// <summary>
    /// Pure TRS composition shared by runtime MER spawning and headless marker tests. Quaternion math is
    /// implemented here instead of calling Unity engine ECalls, so the exact runtime composition can run in
    /// the net48 headless test executable.
    /// </summary>
    internal static class MerWorldTransformComposer
    {
        public static MerWorldTransform Compose(MerWorldTransform parent, Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
        {
            return Compose(parent, localPosition, QuaternionFromEuler(localEuler), localScale);
        }

        public static MerWorldTransform Compose(MerWorldTransform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            Vector3 scaledLocalPosition = Scale(localPosition, parent.Scale);
            Vector3 rotated = Rotate(parent.Rotation, scaledLocalPosition);
            return new MerWorldTransform(
                Add(parent.Position, rotated),
                Normalize(Multiply(parent.Rotation, localRotation)),
                Scale(parent.Scale, localScale));
        }

        /// <summary>Unity's documented Z-X-Y Euler application order, represented as qY * qX * qZ.</summary>
        public static Quaternion QuaternionFromEuler(Vector3 eulerDegrees)
        {
            double halfX = eulerDegrees.x * Math.PI / 360d;
            double halfY = eulerDegrees.y * Math.PI / 360d;
            double halfZ = eulerDegrees.z * Math.PI / 360d;
            Quaternion qx = new Quaternion((float)Math.Sin(halfX), 0f, 0f, (float)Math.Cos(halfX));
            Quaternion qy = new Quaternion(0f, (float)Math.Sin(halfY), 0f, (float)Math.Cos(halfY));
            Quaternion qz = new Quaternion(0f, 0f, (float)Math.Sin(halfZ), (float)Math.Cos(halfZ));
            return Normalize(Multiply(Multiply(qy, qx), qz));
        }

        public static bool Approximately(MerWorldTransform actual, MerWorldTransform expected, float positionTolerance, float rotationToleranceDegrees)
        {
            return Distance(actual.Position, expected.Position) <= positionTolerance &&
                RotationAngleDegrees(actual.Rotation, expected.Rotation) <= rotationToleranceDegrees;
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            double x = a.x - b.x;
            double y = a.y - b.y;
            double z = a.z - b.z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        public static float RotationAngleDegrees(Quaternion a, Quaternion b)
        {
            Quaternion an = Normalize(a);
            Quaternion bn = Normalize(b);
            double dot = Math.Abs(an.x * bn.x + an.y * bn.y + an.z * bn.z + an.w * bn.w);
            dot = Math.Min(1d, dot);
            return (float)(2d * Math.Acos(dot) * 180d / Math.PI);
        }

        private static Vector3 Add(Vector3 a, Vector3 b) =>
            new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        private static Vector3 Scale(Vector3 a, Vector3 b) =>
            new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        private static Vector3 Rotate(Quaternion rotation, Vector3 point)
        {
            Quaternion q = Normalize(rotation);
            float xx = q.x * q.x;
            float yy = q.y * q.y;
            float zz = q.z * q.z;
            float xy = q.x * q.y;
            float xz = q.x * q.z;
            float yz = q.y * q.z;
            float wx = q.w * q.x;
            float wy = q.w * q.y;
            float wz = q.w * q.z;
            return new Vector3(
                (1f - 2f * (yy + zz)) * point.x + 2f * (xy - wz) * point.y + 2f * (xz + wy) * point.z,
                2f * (xy + wz) * point.x + (1f - 2f * (xx + zz)) * point.y + 2f * (yz - wx) * point.z,
                2f * (xz - wy) * point.x + 2f * (yz + wx) * point.y + (1f - 2f * (xx + yy)) * point.z);
        }

        private static Quaternion Multiply(Quaternion lhs, Quaternion rhs)
        {
            return new Quaternion(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y - lhs.x * rhs.z + lhs.y * rhs.w + lhs.z * rhs.x,
                lhs.w * rhs.z + lhs.x * rhs.y - lhs.y * rhs.x + lhs.z * rhs.w,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);
        }

        private static Quaternion Normalize(Quaternion value)
        {
            double magnitude = Math.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (magnitude <= 1e-12d)
            {
                return new Quaternion(0f, 0f, 0f, 1f);
            }

            float inverse = (float)(1d / magnitude);
            return new Quaternion(value.x * inverse, value.y * inverse, value.z * inverse, value.w * inverse);
        }
    }
}
