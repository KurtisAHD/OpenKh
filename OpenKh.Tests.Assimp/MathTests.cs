using System.Numerics;
using OpenKh.AssimpUtils;
using OpenKh.Engine;
using Xunit;
using MtkUtils = ModelingToolkit.AssimpModule.AssimpUtils;

namespace OpenKh.Tests.Assimp
{
    /// <summary>
    /// SharpAssimp hands out System.Numerics types in Assimp's layout (the transpose of the
    /// System.Numerics convention). These check AssimpMath and the conversion helpers against
    /// System.Numerics on the transposed matrices.
    /// </summary>
    public class MathTests
    {
        private const float Eps = 1e-4f;

        private static readonly Vector3 S = new Vector3(2, 2, 2);
        private static readonly Vector3 R = new Vector3(0.1f, -0.7f, 2.1f);
        private static readonly Vector3 T = new Vector3(-5, 6.5f, 7.25f);

        // Assimp layout of a node transform: scale, then X, Y, Z rotation, then translation.
        private static Matrix4x4 Srt() => AssimpGeneric.GetNodeTransformMatrix(S, R, T);

        private static Matrix4x4 NumericsSrt() =>
            Matrix4x4.CreateScale(S) * Matrix4x4.CreateRotationX(R.X) * Matrix4x4.CreateRotationY(R.Y) *
            Matrix4x4.CreateRotationZ(R.Z) * Matrix4x4.CreateTranslation(T);

        private static void Near(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Assert.True(System.MathF.Abs(expected[r, c] - actual[r, c]) < Eps, $"[{r},{c}] expected {expected} got {actual}");
        }

        private static void SameRotation(Quaternion expected, Quaternion actual) =>
            Assert.True(System.MathF.Abs(System.MathF.Abs(Quaternion.Dot(expected, actual)) - 1) < Eps, $"expected {expected} got {actual}");

        [Fact]
        public void NodeTransformIsTransposedNumerics() => Near(Matrix4x4.Transpose(NumericsSrt()), Srt());

        [Fact]
        public void BuildersMatchNumerics()
        {
            Near(Matrix4x4.Transpose(Matrix4x4.CreateTranslation(T)), AssimpMath.FromTranslation(T));
            Near(Matrix4x4.Transpose(Matrix4x4.CreateScale(T)), AssimpMath.FromScaling(T));
            Near(Matrix4x4.Transpose(Matrix4x4.CreateRotationX(0.3f)), AssimpMath.FromRotationX(0.3f));
            Near(Matrix4x4.Transpose(Matrix4x4.CreateRotationY(0.3f)), AssimpMath.FromRotationY(0.3f));
            Near(Matrix4x4.Transpose(Matrix4x4.CreateRotationZ(0.3f)), AssimpMath.FromRotationZ(0.3f));
        }

        [Fact]
        public void MultiplyAppliesLeftOperandFirst()
        {
            var a = AssimpMath.FromRotationZ(0.5f);
            var b = AssimpMath.FromTranslation(T);
            Near(Matrix4x4.Transpose(Matrix4x4.Transpose(a) * Matrix4x4.Transpose(b)), AssimpMath.Multiply(a, b));
        }

        [Fact]
        public void InverseAndDeterminant()
        {
            var m = Srt();
            Assert.Equal(m.GetDeterminant(), AssimpMath.Determinant(m), 2);
            Near(Matrix4x4.Identity, AssimpMath.Multiply(m, AssimpMath.Inverse(m)));
        }

        [Fact]
        public void DecomposeRecomposes()
        {
            AssimpMath.Decompose(Srt(), out var scale, out var rotation, out var translation);
            Assert.True(Vector3.Distance(S, scale) < Eps);
            Assert.True(Vector3.Distance(T, translation) < Eps);
            Assert.True(Matrix4x4.Decompose(NumericsSrt(), out _, out var expected, out _));
            SameRotation(expected, rotation);
        }

        [Fact]
        public void QuaternionHelpersMatchNumerics()
        {
            var a = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 2, 3)), 0.8f);
            var b = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -1.3f);

            Near(Matrix4x4.Transpose(Matrix4x4.CreateFromQuaternion(a)), AssimpMath.GetMatrix(a));
            SameRotation(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f), AssimpMath.FromAxisAngle(Vector3.UnitZ, 0.4f));
            SameRotation(Quaternion.Slerp(a, b, 0.25f), AssimpMath.Slerp(a, b, 0.25f));
            SameRotation(a, AssimpMath.Slerp(a, b, 0));
            SameRotation(b, AssimpMath.Slerp(a, b, 1));

            var m = AssimpMath.GetMatrix(a);
            SameRotation(a, AssimpMath.FromRotationMatrix(m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33));
        }

        [Fact]
        public void ConversionsRoundTrip()
        {
            var m = Srt();
            var numerics = AssimpGeneric.ToNumerics(m);
            Assert.Equal(T, numerics.Translation);
            Assert.Equal(m, AssimpGeneric.ToAssimp(numerics));
            Assert.Equal(m, AssimpGeneric.ToAssimp(AssimpGeneric.ToXna(m)));
            Assert.Equal(T.X, AssimpGeneric.ToXna(m).M41);

            Assert.Equal(numerics, MtkUtils.ToNumerics(m));
            Assert.Equal(m, MtkUtils.ToAssimp(MtkUtils.ToNumerics(m)));
        }
    }
}
