using OpenKh.Engine;
using SharpAssimp;
using System.Numerics;

namespace OpenKh.Command.AnbMaker.Extensions
{
    /// <remarks>
    /// SharpAssimp uses System.Numerics types directly, so the vector/quaternion conversions are identities.
    /// Its matrices keep Assimp's row-major layout, which is the transpose of System.Numerics.
    /// </remarks>
    internal static class AssimpExtensions
    {
        public static Vector3 ToAssimpVector3D(this Vector3 it) => it;

        public static Quaternion ToAssimpQuaternion(this Quaternion it) => it;

        public static Vector3 ToDotNetVector3(this Vector3 it) => it;

        public static Quaternion ToDotNetQuaternion(this Quaternion it) => it;


        public static Quaternion GetInterpolatedQuaternion(this List<QuaternionKey> keys, double time)
        {
            if (keys.Any())
            {
                if (time < keys.First().Time)
                {
                    return keys.First().Value;
                }
                if (keys.Last().Time < time)
                {
                    return keys.Last().Value;
                }

                for (int x = 0, cx = keys.Count - 1; x < cx; x++)
                {
                    var time0 = keys[x].Time;
                    var time1 = keys[x + 1].Time;

                    if (time0 <= time && time <= time1)
                    {
                        float ratio = (float)((time - time0) / (time1 - time0));

                        return AssimpMath.Slerp(
                            keys[x].Value,
                            keys[x + 1].Value,
                            ratio
                        );
                    }
                }
            }

            return Quaternion.Identity;
        }

        public static Vector3 GetInterpolatedVector(this List<VectorKey> keys, double time)
        {
            if (keys.Any())
            {
                if (time < keys.First().Time)
                {
                    return keys.First().Value;
                }
                if (keys.Last().Time < time)
                {
                    return keys.Last().Value;
                }

                for (int x = 0, cx = keys.Count - 1; x < cx; x++)
                {
                    var time0 = keys[x].Time;
                    var time1 = keys[x + 1].Time;

                    if (time0 <= time && time <= time1)
                    {
                        float ratio = (float)((time - time0) / (time1 - time0));

                        return (keys[x].Value * (1f - ratio)) + (keys[x + 1].Value * ratio);
                    }
                }
            }

            return new Vector3(0, 0, 0);
        }

        public static Matrix4x4 ToDotNetMatrix4x4(this Matrix4x4 m) =>
            Matrix4x4.Transpose(m);
    }
}
