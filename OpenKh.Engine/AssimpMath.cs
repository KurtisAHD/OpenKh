using System;
using System.Numerics;

namespace OpenKh.Engine
{
    /// <summary>
    /// Matrix and quaternion operations with the exact semantics of the old AssimpNet math types.
    ///
    /// SharpAssimp exposes <see cref="Matrix4x4"/> in Assimp's raw row-major layout
    /// (A1..A4 = M11..M14, translation in M14/M24/M34), which is the transpose of the
    /// System.Numerics convention. System.Numerics helpers (CreateTranslation, Decompose,
    /// operator *) therefore give different results on these matrices. These functions are
    /// straight ports of AssimpNet 5.0 so existing results stay bit-identical.
    /// </summary>
    public static class AssimpMath
    {
        /// <summary>AssimpNet <c>a * b</c>: applies <paramref name="a"/> first, then <paramref name="b"/>.</summary>
        public static Matrix4x4 Multiply(Matrix4x4 a, Matrix4x4 b) => new Matrix4x4(
            a.M11 * b.M11 + a.M21 * b.M12 + a.M31 * b.M13 + a.M41 * b.M14,
            a.M12 * b.M11 + a.M22 * b.M12 + a.M32 * b.M13 + a.M42 * b.M14,
            a.M13 * b.M11 + a.M23 * b.M12 + a.M33 * b.M13 + a.M43 * b.M14,
            a.M14 * b.M11 + a.M24 * b.M12 + a.M34 * b.M13 + a.M44 * b.M14,
            a.M11 * b.M21 + a.M21 * b.M22 + a.M31 * b.M23 + a.M41 * b.M24,
            a.M12 * b.M21 + a.M22 * b.M22 + a.M32 * b.M23 + a.M42 * b.M24,
            a.M13 * b.M21 + a.M23 * b.M22 + a.M33 * b.M23 + a.M43 * b.M24,
            a.M14 * b.M21 + a.M24 * b.M22 + a.M34 * b.M23 + a.M44 * b.M24,
            a.M11 * b.M31 + a.M21 * b.M32 + a.M31 * b.M33 + a.M41 * b.M34,
            a.M12 * b.M31 + a.M22 * b.M32 + a.M32 * b.M33 + a.M42 * b.M34,
            a.M13 * b.M31 + a.M23 * b.M32 + a.M33 * b.M33 + a.M43 * b.M34,
            a.M14 * b.M31 + a.M24 * b.M32 + a.M34 * b.M33 + a.M44 * b.M34,
            a.M11 * b.M41 + a.M21 * b.M42 + a.M31 * b.M43 + a.M41 * b.M44,
            a.M12 * b.M41 + a.M22 * b.M42 + a.M32 * b.M43 + a.M42 * b.M44,
            a.M13 * b.M41 + a.M23 * b.M42 + a.M33 * b.M43 + a.M43 * b.M44,
            a.M14 * b.M41 + a.M24 * b.M42 + a.M34 * b.M43 + a.M44 * b.M44);

        public static float Determinant(Matrix4x4 m) =>
            m.M11 * m.M22 * m.M33 * m.M44 - m.M11 * m.M22 * m.M34 * m.M43 + m.M11 * m.M23 * m.M34 * m.M42 - m.M11 * m.M23 * m.M32 * m.M44
            + m.M11 * m.M24 * m.M32 * m.M43 - m.M11 * m.M24 * m.M33 * m.M42 - m.M12 * m.M23 * m.M34 * m.M41 + m.M12 * m.M23 * m.M31 * m.M44
            - m.M12 * m.M24 * m.M31 * m.M43 + m.M12 * m.M24 * m.M33 * m.M41 - m.M12 * m.M21 * m.M33 * m.M44 + m.M12 * m.M21 * m.M34 * m.M43
            + m.M13 * m.M24 * m.M31 * m.M42 - m.M13 * m.M24 * m.M32 * m.M41 + m.M13 * m.M21 * m.M32 * m.M44 - m.M13 * m.M21 * m.M34 * m.M42
            + m.M13 * m.M22 * m.M34 * m.M41 - m.M13 * m.M22 * m.M31 * m.M44 - m.M14 * m.M21 * m.M32 * m.M43 + m.M14 * m.M21 * m.M33 * m.M42
            - m.M14 * m.M22 * m.M33 * m.M41 + m.M14 * m.M22 * m.M31 * m.M43 - m.M14 * m.M23 * m.M31 * m.M42 + m.M14 * m.M23 * m.M32 * m.M41;

        /// <summary>AssimpNet <c>Matrix4x4.Inverse()</c>. A singular matrix yields non-finite values.</summary>
        public static Matrix4x4 Inverse(Matrix4x4 m)
        {
            float det = Determinant(m);
            float s = 1f / det;
            float A1 = m.M11, A2 = m.M12, A3 = m.M13, A4 = m.M14;
            float B1 = m.M21, B2 = m.M22, B3 = m.M23, B4 = m.M24;
            float C1 = m.M31, C2 = m.M32, C3 = m.M33, C4 = m.M34;
            float D1 = m.M41, D2 = m.M42, D3 = m.M43, D4 = m.M44;
            return new Matrix4x4(
                s * (B2 * (C3 * D4 - C4 * D3) + B3 * (C4 * D2 - C2 * D4) + B4 * (C2 * D3 - C3 * D2)),
                -s * (A2 * (C3 * D4 - C4 * D3) + A3 * (C4 * D2 - C2 * D4) + A4 * (C2 * D3 - C3 * D2)),
                s * (A2 * (B3 * D4 - B4 * D3) + A3 * (B4 * D2 - B2 * D4) + A4 * (B2 * D3 - B3 * D2)),
                -s * (A2 * (B3 * C4 - B4 * C3) + A3 * (B4 * C2 - B2 * C4) + A4 * (B2 * C3 - B3 * C2)),
                -s * (B1 * (C3 * D4 - C4 * D3) + B3 * (C4 * D1 - C1 * D4) + B4 * (C1 * D3 - C3 * D1)),
                s * (A1 * (C3 * D4 - C4 * D3) + A3 * (C4 * D1 - C1 * D4) + A4 * (C1 * D3 - C3 * D1)),
                -s * (A1 * (B3 * D4 - B4 * D3) + A3 * (B4 * D1 - B1 * D4) + A4 * (B1 * D3 - B3 * D1)),
                s * (A1 * (B3 * C4 - B4 * C3) + A3 * (B4 * C1 - B1 * C4) + A4 * (B1 * C3 - B3 * C1)),
                s * (B1 * (C2 * D4 - C4 * D2) + B2 * (C4 * D1 - C1 * D4) + B4 * (C1 * D2 - C2 * D1)),
                -s * (A1 * (C2 * D4 - C4 * D2) + A2 * (C4 * D1 - C1 * D4) + A4 * (C1 * D2 - C2 * D1)),
                s * (A1 * (B2 * D4 - B4 * D2) + A2 * (B4 * D1 - B1 * D4) + A4 * (B1 * D2 - B2 * D1)),
                -s * (A1 * (B2 * C4 - B4 * C2) + A2 * (B4 * C1 - B1 * C4) + A4 * (B1 * C2 - B2 * C1)),
                -s * (B1 * (C2 * D3 - C3 * D2) + B2 * (C3 * D1 - C1 * D3) + B3 * (C1 * D2 - C2 * D1)),
                s * (A1 * (C2 * D3 - C3 * D2) + A2 * (C3 * D1 - C1 * D3) + A3 * (C1 * D2 - C2 * D1)),
                -s * (A1 * (B2 * D3 - B3 * D2) + A2 * (B3 * D1 - B1 * D3) + A3 * (B1 * D2 - B2 * D1)),
                s * (A1 * (B2 * C3 - B3 * C2) + A2 * (B3 * C1 - B1 * C3) + A3 * (B1 * C2 - B2 * C1)));
        }

        /// <summary>
        /// AssimpNet <c>Matrix4x4.Decompose</c>. Note: scale is the length of each row, and the
        /// returned quaternion uses AssimpNet's normalisation (division by the squared length).
        /// </summary>
        public static void Decompose(Matrix4x4 m, out Vector3 scaling, out Quaternion rotation, out Vector3 translation)
        {
            translation = new Vector3(m.M14, m.M24, m.M34);
            var row1 = new Vector3(m.M11, m.M12, m.M13);
            var row2 = new Vector3(m.M21, m.M22, m.M23);
            var row3 = new Vector3(m.M31, m.M32, m.M33);
            scaling = new Vector3(Length(row1), Length(row2), Length(row3));
            if (Determinant(m) < 0f)
                scaling = new Vector3(-scaling.X, -scaling.Y, -scaling.Z);
            if (scaling.X != 0f) row1 = Divide(row1, scaling.X);
            if (scaling.Y != 0f) row2 = Divide(row2, scaling.Y);
            if (scaling.Z != 0f) row3 = Divide(row3, scaling.Z);
            rotation = FromRotationMatrix(row1.X, row1.Y, row1.Z, row2.X, row2.Y, row2.Z, row3.X, row3.Y, row3.Z);
        }

        /// <summary>AssimpNet <c>Matrix4x4.DecomposeNoScaling</c>.</summary>
        public static void DecomposeNoScaling(Matrix4x4 m, out Quaternion rotation, out Vector3 translation)
        {
            translation = new Vector3(m.M14, m.M24, m.M34);
            rotation = FromRotationMatrix(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33);
        }

        public static Matrix4x4 FromTranslation(Vector3 translation)
        {
            var m = Matrix4x4.Identity;
            m.M14 = translation.X;
            m.M24 = translation.Y;
            m.M34 = translation.Z;
            return m;
        }

        public static Matrix4x4 FromScaling(Vector3 scaling)
        {
            var m = Matrix4x4.Identity;
            m.M11 = scaling.X;
            m.M22 = scaling.Y;
            m.M33 = scaling.Z;
            return m;
        }

        public static Matrix4x4 FromRotationX(float radians)
        {
            var m = Matrix4x4.Identity;
            m.M22 = m.M33 = (float)Math.Cos(radians);
            m.M32 = (float)Math.Sin(radians);
            m.M23 = -m.M32;
            return m;
        }

        public static Matrix4x4 FromRotationY(float radians)
        {
            var m = Matrix4x4.Identity;
            m.M11 = m.M33 = (float)Math.Cos(radians);
            m.M13 = (float)Math.Sin(radians);
            m.M31 = -m.M13;
            return m;
        }

        public static Matrix4x4 FromRotationZ(float radians)
        {
            var m = Matrix4x4.Identity;
            m.M11 = m.M22 = (float)Math.Cos(radians);
            m.M21 = (float)Math.Sin(radians);
            m.M12 = -m.M21;
            return m;
        }

        /// <summary>AssimpNet <c>new Quaternion(Matrix3x3)</c>, rotation matrix given row by row.</summary>
        public static Quaternion FromRotationMatrix(
            float a1, float a2, float a3,
            float b1, float b2, float b3,
            float c1, float c2, float c3)
        {
            float w, x, y, z;
            float trace = a1 + b2 + c3;
            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (c2 - b3) / s;
                y = (a3 - c1) / s;
                z = (b1 - a2) / s;
            }
            else if (a1 > b2 && a1 > c3)
            {
                float s = (float)Math.Sqrt(1.0 + (double)a1 - (double)b2 - (double)c3) * 2f;
                w = (c2 - b3) / s;
                x = 0.25f * s;
                y = (a2 + b1) / s;
                z = (a3 + c1) / s;
            }
            else if (b2 > c3)
            {
                float s = (float)Math.Sqrt(1f + b2 - a1 - c3) * 2f;
                w = (a3 - c1) / s;
                x = (a2 + b1) / s;
                y = 0.25f * s;
                z = (b3 + c2) / s;
            }
            else
            {
                float s = (float)Math.Sqrt(1f + c3 - a1 - b2) * 2f;
                w = (b1 - a2) / s;
                x = (a3 + c1) / s;
                y = (b3 + c2) / s;
                z = 0.25f * s;
            }
            return Normalize(new Quaternion(x, y, z, w));
        }

        /// <summary>AssimpNet <c>Quaternion.Normalize</c> (divides by the squared length, as AssimpNet did).</summary>
        public static Quaternion Normalize(Quaternion q)
        {
            float n = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            if (n != 0f)
                return new Quaternion(q.X / n, q.Y / n, q.Z / n, q.W / n);
            return q;
        }

        /// <summary>AssimpNet <c>Quaternion.GetMatrix</c> as a 4x4 matrix in Assimp layout.</summary>
        public static Matrix4x4 GetMatrix(Quaternion q)
        {
            float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
            float xy = q.X * q.Y, zw = q.Z * q.W, zx = q.Z * q.X;
            float yw = q.Y * q.W, yz = q.Y * q.Z, xw = q.X * q.W;
            var m = Matrix4x4.Identity;
            m.M11 = 1f - 2f * (yy + zz);
            m.M21 = 2f * (xy + zw);
            m.M31 = 2f * (zx - yw);
            m.M12 = 2f * (xy - zw);
            m.M22 = 1f - 2f * (zz + xx);
            m.M32 = 2f * (yz + xw);
            m.M13 = 2f * (zx + yw);
            m.M23 = 2f * (yz - xw);
            m.M33 = 1f - 2f * (yy + xx);
            return m;
        }

        /// <summary>AssimpNet <c>Quaternion.Slerp</c>.</summary>
        public static Quaternion Slerp(Quaternion start, Quaternion end, float factor)
        {
            float cosom = start.X * end.X + start.Y * end.Y + start.Z * end.Z + start.W * end.W;
            if (cosom < 0f)
            {
                cosom = -cosom;
                end = new Quaternion(-end.X, -end.Y, -end.Z, -end.W);
            }
            float sclp, sclq;
            if (1f - cosom > 0.0001f)
            {
                float omega = (float)Math.Acos(cosom);
                float sinom = (float)Math.Sin(omega);
                sclp = (float)Math.Sin((1f - factor) * omega) / sinom;
                sclq = (float)Math.Sin(factor * omega) / sinom;
            }
            else
            {
                sclp = 1f - factor;
                sclq = factor;
            }
            return new Quaternion(
                sclp * start.X + sclq * end.X,
                sclp * start.Y + sclq * end.Y,
                sclp * start.Z + sclq * end.Z,
                sclp * start.W + sclq * end.W);
        }

        /// <summary>AssimpNet <c>new Quaternion(axis, angle)</c>.</summary>
        public static Quaternion FromAxisAngle(Vector3 axis, float angle)
        {
            axis = NormalizeVector(axis);
            float half = angle * 0.5f;
            float sin = (float)Math.Sin(half);
            return new Quaternion(axis.X * sin, axis.Y * sin, axis.Z * sin, (float)Math.Cos(half));
        }

        /// <summary>AssimpNet quaternion product <c>a * b</c>.</summary>
        public static Quaternion Multiply(Quaternion a, Quaternion b) => new Quaternion(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        private static float Length(Vector3 v) => (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        // AssimpNet divides by multiplying with the reciprocal
        private static Vector3 Divide(Vector3 v, float s)
        {
            float inv = 1f / s;
            return new Vector3(v.X * inv, v.Y * inv, v.Z * inv);
        }
        private static Vector3 NormalizeVector(Vector3 v)
        {
            float inv = 1f / (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            return new Vector3(v.X * inv, v.Y * inv, v.Z * inv);
        }
    }
}
