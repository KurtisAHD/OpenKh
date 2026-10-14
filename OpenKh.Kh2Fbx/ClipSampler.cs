using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using OpenKh.Kh2Anim.Mset;
using OpenKh.Kh2Anim.Mset.Interfaces;
using System.Numerics;

namespace OpenKh.Kh2Fbx
{
    // One clip, sampled once per tick on KH2's fixed 60-tick/second clock, as parent-relative
    // (local) bone transforms - exactly what an FBX bone's Lcl Translation/Rotation/Scaling
    // curves hold. Times are clip-relative ticks (first sample at 0).
    public sealed class SampledClip
    {
        public required string Name { get; init; }
        public required float DurationTicks { get; init; }
        public required float[] Times { get; init; }
        // [bone][sample]
        public required Vector3[][] Translation { get; init; }
        public required Vector3[][] RotationDegrees { get; init; } // Euler XYZ, unwrapped for continuity
        public required Quaternion[][] Rotation { get; init; }
        public required Vector3[][] Scale { get; init; }

        public float DurationSeconds => DurationTicks / ClipSampler.TicksPerSecond;

        // A one-tick clip holding every bone at its bind pose.
        public static SampledClip BindPose(IReadOnlyList<RestPose> rest)
        {
            T[] Pair<T>(T v) => new[] { v, v };
            return new SampledClip
            {
                Name = "BindPose",
                DurationTicks = 1,
                Times = new[] { 0f, 1f },
                Translation = rest.Select(r => Pair(r.Translation)).ToArray(),
                RotationDegrees = rest.Select(r => Pair(r.EulerDegrees)).ToArray(),
                Rotation = rest.Select(r => Pair(r.Rotation)).ToArray(),
                Scale = rest.Select(r => Pair(r.Scale)).ToArray(),
            };
        }
    }

    // A bone's bind-pose local transform. KH2 composes S * Rx * Ry * Rz * T (row-vector).
    public readonly record struct RestPose(Vector3 Translation, Quaternion Rotation, Vector3 EulerDegrees, Vector3 Scale, Matrix4x4 Local)
    {
        public static RestPose Of(ModelCommon.Bone bone)
        {
            Matrix4x4 rotation = Matrix4x4.CreateRotationX(bone.RotationX) * Matrix4x4.CreateRotationY(bone.RotationY) * Matrix4x4.CreateRotationZ(bone.RotationZ);
            Vector3 translation = new Vector3(bone.TranslationX, bone.TranslationY, bone.TranslationZ);
            Vector3 scale = new Vector3(bone.ScaleX, bone.ScaleY, bone.ScaleZ);
            return new RestPose(
                translation,
                Quaternion.CreateFromRotationMatrix(rotation),
                new Vector3(bone.RotationX, bone.RotationY, bone.RotationZ) * (180f / MathF.PI),
                scale,
                Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(bone.RotationX) * Matrix4x4.CreateRotationY(bone.RotationY)
                    * Matrix4x4.CreateRotationZ(bone.RotationZ) * Matrix4x4.CreateTranslation(translation));
        }
    }

    public static class ClipSampler
    {
        public const float TicksPerSecond = 60f;

        // Clip window: FrameStart/FrameEnd are stored in the clip's native-FPS frames, KeyTimes
        // and the evaluator run on a fixed 60-tick clock - scale by 60/FPS. Confirmed against
        // Kh2ObjectEditor's ViewerService.LoadMotion (FrameCount is NOT the end bound).
        public static SampledClip Sample(byte[] mdlxBytes, IReadOnlyList<ModelCommon.Bone> bones, AnimationBinary animation, string clipName)
        {
            Motion.InterpolatedMotionHeader header = animation.MotionFile.InterpolatedMotionHeader;
            if (header.BoneCount != bones.Count)
                throw new InvalidDataException($"motion has {header.BoneCount} bones, model has {bones.Count}");

            float adjust = 60f / header.FrameData.FramesPerSecond;
            float clipStart = header.FrameData.FrameStart * adjust;
            float clipEnd = header.FrameData.FrameEnd * adjust;
            float duration = clipEnd - clipStart;
            if (!(duration >= 0))
                throw new InvalidDataException($"clip window [{clipStart}, {clipEnd}] is inverted");

            List<float> times = new();
            for (int k = 0; k <= (int)MathF.Floor(duration); k++)
                times.Add(k);
            if (duration - times[^1] > 1e-4f)
                times.Add(duration);

            // ProvideMatrices takes a DELTA and accumulates internally, so one provider is
            // stepped forward through the clip rather than rebuilt per sample.
            IAnimMatricesProvider provider = new AnbIndir(Bar.Read(animation.toStream())).GetAnimProvider(new MemoryStream(mdlxBytes));
            double elapsed = 0;

            int boneCount = bones.Count;
            Vector3[][] tr = NewJagged<Vector3>(boneCount, times.Count);
            Vector3[][] eul = NewJagged<Vector3>(boneCount, times.Count);
            Quaternion[][] rot = NewJagged<Quaternion>(boneCount, times.Count);
            Vector3[][] sc = NewJagged<Vector3>(boneCount, times.Count);

            for (int k = 0; k < times.Count; k++)
            {
                double absolute = clipStart + times[k];
                Matrix4x4[] global = provider.ProvideMatrices(absolute - elapsed);
                elapsed = absolute;

                for (int b = 0; b < boneCount; b++)
                {
                    Matrix4x4 local = global[b];
                    int parent = bones[b].ParentIndex;
                    if (parent >= 0)
                    {
                        Matrix4x4.Invert(global[parent], out Matrix4x4 invParent);
                        local *= invParent;
                    }

                    // Intentional squash/stretch clips (e.g. M_EX930) produce sheared matrices
                    // that Decompose rejects; shear can't live in TRS anyway, take the closest TRS.
                    if (!Matrix4x4.Decompose(local, out Vector3 s, out Quaternion q, out Vector3 t)
                        && !TryDecomposeLenient(local, out s, out q, out t))
                        throw new InvalidOperationException($"t={absolute} bone {b}: non-decomposable local matrix");

                    Vector3 previous = k == 0 ? RestPose.Of(bones[b]).EulerDegrees : eul[b][k - 1];
                    tr[b][k] = t;
                    rot[b][k] = q;
                    eul[b][k] = EulerXyzDegreesNear(q, previous);
                    sc[b][k] = s;
                }
            }

            return new SampledClip
            {
                Name = clipName,
                DurationTicks = duration,
                Times = times.ToArray(),
                Translation = tr,
                RotationDegrees = eul,
                Rotation = rot,
                Scale = sc,
            };
        }

        // KH2 composes Rx*Ry*Rz in row-vector form (X applied first), which is FBX's default
        // eEulerXYZ order, so the angles map 1:1. Of the two equivalent Euler solutions (and
        // their 360-degree wraps) pick the one nearest `near`, keeping curves continuous.
        //
        // Near gimbal lock (Y close to +-90) X is atan2 of two near-zero terms; reading Z off the
        // matrix independently then gave a 50-degree error on a real clip. Instead, for each
        // candidate X (measured, its alternate branch, and - near gimbal - the previous X, which
        // keeps curves continuous), Y/Z are solved from Rx(x)^-1 * M, and only candidates that
        // reproduce the rotation exactly are eligible.
        public static Vector3 EulerXyzDegreesNear(Quaternion q, Vector3 near)
        {
            double qx = q.X, qy = q.Y, qz = q.Z, qw = q.W;
            double n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            qx /= n; qy /= n; qz /= n; qw /= n;
            // Same layout as Matrix4x4.CreateFromQuaternion (row-vector convention).
            double[,] m =
            {
                { 1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy + qz * qw), 2 * (qx * qz - qy * qw) },
                { 2 * (qx * qy - qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz + qx * qw) },
                { 2 * (qx * qz + qy * qw), 2 * (qy * qz - qx * qw), 1 - 2 * (qx * qx + qy * qy) },
            };

            const double toRad = Math.PI / 180.0;
            double measured = Math.Atan2(m[1, 2], m[2, 2]);
            List<double> xs = new() { measured, measured + Math.PI };
            if (Math.Sqrt(m[0, 0] * m[0, 0] + m[0, 1] * m[0, 1]) < 1e-2)
                xs.Add(near.X * toRad);

            Vector3 best = default;
            double bestError = double.MaxValue;
            float bestDistance = float.MaxValue;
            foreach (double x in xs)
            {
                // N = Rx(x)^-1 * M, which should equal Ry(y)*Rz(z) =
                // [[cy*cz, cy*sz, -sy], [-sz, cz, 0], [sy*cz, sy*sz, cy]]
                double c = Math.Cos(x), s = Math.Sin(x);
                double n21 = c * m[1, 0] - s * m[2, 0], n22 = c * m[1, 1] - s * m[2, 1];
                double n33 = s * m[1, 2] + c * m[2, 2];
                double y = Math.Atan2(-m[0, 2], n33);
                double z = Math.Atan2(-n21, n22);

                double error = ReconstructionError(m, x, y, z);
                Vector3 candidate = Unwrap(new Vector3((float)(x / toRad), (float)(y / toRad), (float)(z / toRad)), near);
                float distance = Vector3.DistanceSquared(candidate, near);
                bool exact = error < 1e-5, bestExact = bestError < 1e-5;
                if ((exact && (!bestExact || distance < bestDistance)) || (!exact && !bestExact && error < bestError))
                {
                    best = candidate;
                    bestError = error;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static double ReconstructionError(double[,] m, double x, double y, double z)
        {
            double cx = Math.Cos(x), sx = Math.Sin(x), cy = Math.Cos(y), sy = Math.Sin(y), cz = Math.Cos(z), sz = Math.Sin(z);
            double[,] r =
            {
                { cy * cz, cy * sz, -sy },
                { -cx * sz + sx * sy * cz, cx * cz + sx * sy * sz, sx * cy },
                { sx * sz + cx * sy * cz, -sx * cz + cx * sy * sz, cx * cy },
            };
            double error = 0;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    error = Math.Max(error, Math.Abs(r[i, j] - m[i, j]));
            return error;
        }

        private static Vector3 Unwrap(Vector3 v, Vector3 near) =>
            new Vector3(UnwrapAngle(v.X, near.X), UnwrapAngle(v.Y, near.Y), UnwrapAngle(v.Z, near.Z));

        private static float UnwrapAngle(float angle, float near) =>
            angle + 360f * MathF.Round((near - angle) / 360f);

        // Gram-Schmidt on the rows (row-vector convention, M = S*R): keeps the first axis exact and
        // preserves |determinant| (product of the scales); shear is dropped. Third scale is signed
        // so a reflection survives. Only fails on NaN/infinite input or a fully collapsed matrix.
        internal static bool TryDecomposeLenient(Matrix4x4 m, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
        {
            scale = Vector3.One;
            rotation = Quaternion.Identity;
            translation = new Vector3(m.M41, m.M42, m.M43);

            Vector3 a = new Vector3(m.M11, m.M12, m.M13);
            Vector3 b = new Vector3(m.M21, m.M22, m.M23);
            Vector3 c = new Vector3(m.M31, m.M32, m.M33);
            if (!float.IsFinite(a.Length() + b.Length() + c.Length() + translation.Length()))
                return false;

            const float eps = 1e-6f;
            float s0 = a.Length();
            if (s0 < eps)
                return false;
            Vector3 r0 = a / s0;

            Vector3 bPerp = b - Vector3.Dot(b, r0) * r0;
            float s1 = bPerp.Length();
            if (s1 < eps)
                return false;
            Vector3 r1 = bPerp / s1;

            Vector3 r2 = Vector3.Cross(r0, r1);
            float s2 = Vector3.Dot(c, r2);
            if (MathF.Abs(s2) < eps)
                return false;

            Matrix4x4 rot = new Matrix4x4(
                r0.X, r0.Y, r0.Z, 0,
                r1.X, r1.Y, r1.Z, 0,
                r2.X, r2.Y, r2.Z, 0,
                0, 0, 0, 1);
            rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rot));
            scale = new Vector3(s0, s1, s2);
            return true;
        }

        private static T[][] NewJagged<T>(int outer, int inner)
        {
            T[][] result = new T[outer][];
            for (int i = 0; i < outer; i++)
                result[i] = new T[inner];
            return result;
        }
    }

    internal static class CurveReducer
    {
        // Drops samples that linear interpolation between the kept neighbours reproduces within
        // `tolerance`. Always keeps the first and last sample.
        public static (float[] Times, float[] Values) Reduce(float[] times, float[] values, float tolerance)
        {
            int n = values.Length;
            List<int> kept = new() { 0 };
            int anchor = 0;
            while (anchor < n - 1)
            {
                int end = anchor + 1;
                while (end + 1 < n && SpanFits(times, values, anchor, end + 1, tolerance))
                    end++;
                kept.Add(end);
                anchor = end;
            }

            return (kept.Select(i => times[i]).ToArray(), kept.Select(i => values[i]).ToArray());
        }

        // Rotation is reduced on one shared set of key times, checked as a real angle against the
        // sampled rotation under both ways importers fill the gaps: interpolating the Euler
        // angles linearly (the FBX default) or converting each key to a quaternion and
        // interpolating that. Reducing each Euler axis on its own passed per-axis checks but
        // drifted up to ~0.5 degrees between keys under quaternion interpolation.
        public static int[] ReduceRotation(float[] times, Vector3[] eulerDegrees, Quaternion[] actual, float toleranceDegrees)
        {
            int n = times.Length;
            float cosHalfTolerance = MathF.Cos(toleranceDegrees * MathF.PI / 360f);
            if (actual.All(q => MathF.Abs(Quaternion.Dot(q, actual[0])) >= cosHalfTolerance))
                return new[] { 0 };

            Quaternion[] keyQuat = eulerDegrees.Select(FromEulerXyzDegrees).ToArray();
            List<int> kept = new() { 0 };
            int anchor = 0;
            while (anchor < n - 1)
            {
                int end = anchor + 1;
                while (end + 1 < n && RotationSpanFits(anchor, end + 1))
                    end++;
                kept.Add(end);
                anchor = end;
            }
            return kept.ToArray();

            bool RotationSpanFits(int a, int b)
            {
                Quaternion qa = keyQuat[a], qb = keyQuat[b];
                if (Quaternion.Dot(qa, qb) < 0)
                    qb = Quaternion.Negate(qb);
                for (int i = a + 1; i < b; i++)
                {
                    float f = (times[i] - times[a]) / (times[b] - times[a]);
                    Quaternion euler = FromEulerXyzDegrees(Vector3.Lerp(eulerDegrees[a], eulerDegrees[b], f));
                    Quaternion nlerp = Quaternion.Normalize(Quaternion.Lerp(qa, qb, f));
                    if (MathF.Abs(Quaternion.Dot(euler, actual[i])) < cosHalfTolerance
                        || MathF.Abs(Quaternion.Dot(nlerp, actual[i])) < cosHalfTolerance)
                        return false;
                }
                return true;
            }
        }

        // Rx*Ry*Rz in row-vector form (X applied first).
        private static Quaternion FromEulerXyzDegrees(Vector3 degrees)
        {
            Vector3 r = degrees * (MathF.PI / 180f);
            return Quaternion.Concatenate(Quaternion.Concatenate(
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, r.X),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, r.Y)),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, r.Z));
        }

        private static bool SpanFits(float[] t, float[] v, int a, int b, float tolerance)
        {
            for (int i = a + 1; i < b; i++)
            {
                float f = (t[i] - t[a]) / (t[b] - t[a]);
                float lerp = v[a] + (v[b] - v[a]) * f;
                if (MathF.Abs(lerp - v[i]) > tolerance)
                    return false;
            }
            return true;
        }
    }
}
