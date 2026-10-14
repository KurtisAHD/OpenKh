using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenKh.Engine;
using OpenKh.Kh2.Models;
using OpenKh.Kh2Fbx;
using SharpAssimp;
using Xunit;

namespace OpenKh.Tests.Assimp
{
    /// <summary>
    /// OpenKh.Kh2Fbx: the direct FBX writer, read back with Assimp as an independent reader.
    /// </summary>
    public class Kh2FbxWriterTests
    {
        internal static ModelSkeletal BuildKh2Model()
        {
            var model = new ModelSkeletal
            {
                TextureCount = 2,
                Bones = new List<ModelCommon.Bone>(),
                Groups = new List<ModelSkeletal.SkeletalGroup>(),
            };

            float[][] srt =
            {
                new[] { 1f, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f },
                new[] { 1f, 1f, 1f, 0.3f, -0.2f, 1.1f, 10f, 20f, -5f },
                new[] { 1.5f, 1f, 0.5f, -1.4f, 0.6f, 0.2f, 0f, 35f, 2.5f },
            };
            for (short i = 0; i < srt.Length; i++)
            {
                var p = srt[i];
                model.Bones.Add(new ModelCommon.Bone
                {
                    Index = i,
                    ParentIndex = (short)(i - 1),
                    ScaleX = p[0], ScaleY = p[1], ScaleZ = p[2],
                    RotationX = p[3], RotationY = p[4], RotationZ = p[5],
                    TranslationX = p[6], TranslationY = p[7], TranslationZ = p[8],
                });
            }

            for (int g = 0; g < 2; g++)
            {
                var group = new ModelSkeletal.SkeletalGroup
                {
                    Header = new ModelSkeletal.SkeletalGroupHeader { TextureIndex = (uint)(1 - g) },
                    Mesh = new ModelSkeletal.SkeletalMesh(),
                };
                for (int v = 0; v < 4; v++)
                {
                    var bone = (v + g) % 3;
                    group.Mesh.Vertices.Add(new ModelCommon.UVBVertex
                    {
                        Position = new Vector3(v * 1.5f + g, v * -2.25f, 0.125f * v * g),
                        BPositions = new List<ModelCommon.BPosition>
                        {
                            new ModelCommon.BPosition(new Vector4(v, g, 1, v == 2 ? 0.5f : 0), bone),
                        },
                        U = v * 1024,
                        V = 4096 - v * 512,
                        Color = new OpenKh.Kh2.Models.VIF.VifCommon.VertexColor((byte)(v * 60), (byte)(g * 100), 128, 255),
                        Normal = new OpenKh.Kh2.Models.VIF.VifCommon.VertexNormal(0, v % 2, 1 - v % 2),
                    });
                }
                group.Mesh.Triangles.Add(new List<int> { 0, 1, 2 });
                group.Mesh.Triangles.Add(new List<int> { 2, 1, 3 });
                model.Groups.Add(group);
            }

            return model;
        }

        private static string WriteRigOnly(ModelSkeletal model)
        {
            var dir = Path.Combine(Path.GetTempPath(), "openkh-kh2fbx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "model.fbx");
            Kh2FbxExporter.Write(path, model, Array.Empty<SampledClip>(), Path.Combine(dir, "Textures"), 0);
            return path;
        }

        // KH2 composes local * parentGlobal (row-vector), see RestPose.
        private static Matrix4x4[] ExpectedGlobals(ModelSkeletal model)
        {
            var globals = new Matrix4x4[model.Bones.Count];
            for (int b = 0; b < model.Bones.Count; b++)
            {
                var local = RestPose.Of(model.Bones[b]).Local;
                int parent = model.Bones[b].ParentIndex;
                globals[b] = parent >= 0 ? local * globals[parent] : local;
            }
            return globals;
        }

        private static Matrix4x4 WorldTransform(Node node)
        {
            var world = node.Transform;
            for (var parent = node.Parent; parent != null; parent = parent.Parent)
                world = AssimpMath.Multiply(world, parent.Transform);
            return world;
        }

        [Fact]
        public void RigOnlyExportIsDeterministic()
        {
            var model = BuildKh2Model();
            var a = File.ReadAllBytes(WriteRigOnly(model));
            var b = File.ReadAllBytes(WriteRigOnly(model));
            Assert.Equal(a, b);
        }

        [Fact]
        public void RigOnlyExportReadsBackWithAssimp()
        {
            var model = BuildKh2Model();
            var scene = new AssimpContext().ImportFile(WriteRigOnly(model), PostProcessSteps.None);

            // Skeleton: every bone lands at the world position KH2 computes for it.
            var expected = ExpectedGlobals(model);
            for (int b = 0; b < model.Bones.Count; b++)
            {
                var node = scene.RootNode.FindNode($"Bone{b:D4}");
                Assert.NotNull(node);
                var world = WorldTransform(node);
                var position = new Vector3(world.M14, world.M24, world.M34);
                Assert.True(Vector3.Distance(position, expected[b].Translation) < 1e-3f,
                    $"Bone{b:D4} at {position}, expected {expected[b].Translation}");
            }

            // Meshes: one per group, same triangles, every vertex position from the source group.
            Assert.Equal(model.Groups.Count, scene.MeshCount);
            for (int g = 0; g < model.Groups.Count; g++)
            {
                var group = model.Groups[g];
                var mesh = scene.Meshes[g];
                Assert.Equal(group.Mesh.Triangles.Count, mesh.FaceCount);
                foreach (var v in mesh.Vertices)
                    Assert.Contains(group.Mesh.Vertices, src => Vector3.Distance(src.Position, v) < 1e-4f);

                // Skin: each source vertex carries its full weight on the bones KH2 assigns.
                var weightPerVertex = new Dictionary<int, float>();
                foreach (var bone in mesh.Bones)
                    foreach (var w in bone.VertexWeights)
                        weightPerVertex[w.VertexID] = weightPerVertex.GetValueOrDefault(w.VertexID) + w.Weight;
                Assert.Equal(mesh.VertexCount, weightPerVertex.Count);
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    var src = group.Mesh.Vertices.First(s => Vector3.Distance(s.Position, mesh.Vertices[v]) < 1e-4f);
                    var expectedWeight = src.BPositions.Sum(bp => bp.Position.W == 0 ? 1f : bp.Position.W);
                    Assert.Equal(expectedWeight, weightPerVertex[v], 4);
                }
            }

            // The BindPose take is always written first.
            Assert.Equal($"{Kh2FbxExporter.RootName}|BindPose", scene.Animations[0].Name);
        }

        [Fact]
        public void OptionsRenameRootAndDropBindPoseTake()
        {
            var model = BuildKh2Model();
            var dir = Path.Combine(Path.GetTempPath(), "openkh-kh2fbx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "model.fbx");
            Kh2FbxExporter.Write(path, model, Array.Empty<SampledClip>(), null, 0,
                new Kh2FbxExportOptions { RootName = "Armature", WriteBindPoseTake = false });

            var scene = new AssimpContext().ImportFile(path, PostProcessSteps.None);
            Assert.NotNull(scene.RootNode.FindNode("Armature"));
            Assert.Null(scene.RootNode.FindNode(Kh2FbxExporter.RootName));
            Assert.Equal(0, scene.AnimationCount);
        }

        public static IEnumerable<object[]> EulerCases() => new[]
        {
            new object[] { 0f, 0f, 0f },
            new object[] { 10f, 20f, 30f },
            new object[] { -170f, 45f, 120f },
            new object[] { 30f, 90f, 40f },      // gimbal lock
            new object[] { 30f, -90f, 40f },
            new object[] { 15f, 89.99f, -60f },  // near gimbal lock
            new object[] { 179f, -0.5f, -179f },
        };

        private static Matrix4x4 RotationXyzDegrees(Vector3 d)
        {
            var r = d * (MathF.PI / 180f);
            return Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
        }

        [Theory]
        [MemberData(nameof(EulerCases))]
        public void EulerExtractionReproducesRotation(float x, float y, float z)
        {
            var source = RotationXyzDegrees(new Vector3(x, y, z));
            var q = Quaternion.CreateFromRotationMatrix(source);
            var euler = ClipSampler.EulerXyzDegreesNear(q, new Vector3(x, y, z));
            var rebuilt = RotationXyzDegrees(euler);

            var error = new[]
            {
                source.M11 - rebuilt.M11, source.M12 - rebuilt.M12, source.M13 - rebuilt.M13,
                source.M21 - rebuilt.M21, source.M22 - rebuilt.M22, source.M23 - rebuilt.M23,
                source.M31 - rebuilt.M31, source.M32 - rebuilt.M32, source.M33 - rebuilt.M33,
            }.Max(MathF.Abs);
            Assert.True(error < 1e-4f, $"euler {euler} rebuilds with error {error}");
        }

        [Fact]
        public void EulerExtractionStaysNearPreviousKey()
        {
            // 350 degrees about X must come back unwrapped next to a previous key at 340, not as -10.
            var q = Quaternion.CreateFromRotationMatrix(RotationXyzDegrees(new Vector3(350, 0, 0)));
            var euler = ClipSampler.EulerXyzDegreesNear(q, new Vector3(340, 0, 0));
            Assert.Equal(350f, euler.X, 3);
        }

        [Fact]
        public void CurveReducerKeepsOnlyNeededKeys()
        {
            var times = Enumerable.Range(0, 11).Select(i => (float)i).ToArray();

            var linear = CurveReducer.Reduce(times, times.Select(t => 2 * t + 1).ToArray(), 1e-3f);
            Assert.Equal(new[] { 0f, 10f }, linear.Times);

            var spike = times.Select(t => t == 5 ? 3f : 0f).ToArray();
            var reduced = CurveReducer.Reduce(times, spike, 1e-3f);
            Assert.Contains(5f, reduced.Times);
            for (int i = 0; i < times.Length; i++)
                Assert.Equal(spike[i], Interpolate(reduced.Times, reduced.Values, times[i]), 3);
        }

        private static float Interpolate(float[] t, float[] v, float time)
        {
            for (int i = 0; i < t.Length - 1; i++)
                if (time >= t[i] && time <= t[i + 1])
                    return v[i] + (v[i + 1] - v[i]) * (time - t[i]) / (t[i + 1] - t[i]);
            return v[^1];
        }

        [Fact]
        public void RotationReducerCollapsesConstantRotation()
        {
            var times = new[] { 0f, 1f, 2f, 3f };
            var euler = Enumerable.Repeat(new Vector3(10, 20, 30), 4).ToArray();
            var quats = euler.Select(e => Quaternion.CreateFromRotationMatrix(RotationXyzDegrees(e))).ToArray();
            Assert.Equal(new[] { 0 }, CurveReducer.ReduceRotation(times, euler, quats, 0.02f));
        }

        [Fact]
        public void LenientDecomposeHandlesShear()
        {
            // Squash/stretch clips produce sheared locals that Matrix4x4.Decompose rejects.
            var sheared = Matrix4x4.Identity;
            sheared.M21 = 0.5f;
            sheared.M41 = 1; sheared.M42 = 2; sheared.M43 = 3;
            Assert.False(Matrix4x4.Decompose(sheared, out _, out _, out _));

            Assert.True(ClipSampler.TryDecomposeLenient(sheared, out var scale, out var rotation, out var translation));
            Assert.Equal(new Vector3(1, 2, 3), translation);
            Assert.Equal(Math.Abs(sheared.GetDeterminant()), scale.X * scale.Y * scale.Z, 4);
            Assert.Equal(1f, rotation.Length(), 4);
        }
    }
}
