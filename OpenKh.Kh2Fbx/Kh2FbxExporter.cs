using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using System.Numerics;

namespace OpenKh.Kh2Fbx
{
    public sealed record Kh2FbxExportOptions
    {
        // Name of the Null above the skeleton; takes are named "<RootName>|<clip>".
        public string RootName { get; init; } = Kh2FbxExporter.RootName;

        // Writes a one-frame "BindPose" take first. Some importers take a model's default pose
        // from frame 0 of the first take and fall back to it for anything a clip doesn't key;
        // this keeps that default at rest. Tools that don't need it can turn it off.
        public bool WriteBindPoseTake { get; init; } = true;
    }

    // Writes one KH2 model (mesh groups + skeleton + skin) and any number of sampled clips as
    // a single FBX: one AnimationStack ("take") per clip, all on the same rig/bind pose.
    //
    // Naming is stable so re-exports keep clip/bone bindings in downstream tools: the
    // armature root is a Null named "AxisCorrection", bones are "BoneNNNN", meshes "MeshNodeNNNN", takes
    // "AxisCorrection|<clip>". Units are KH2's raw centimetres (UnitScaleFactor 1), Y up.
    public static class Kh2FbxExporter
    {
        public const string RootName = "AxisCorrection";

        // FBX time: 46186158000 units per second; KH2 runs on a 60-tick clock.
        private const long KTimePerSecond = 46_186_158_000L;
        private const double KTimePerTick = KTimePerSecond / 60.0;

        // Rest-equality and key-reduction tolerances, in file units (cm / degrees / ratio).
        private const float TranslationTolerance = 5e-3f;
        private const float RotationToleranceDegrees = 2e-2f;
        private const float ScaleTolerance = 1e-4f;

        public static void Write(string path, ModelSkeletal model, IReadOnlyList<SampledClip> clips, string? textureDir, int textureFileCount, Kh2FbxExportOptions? options = null)
        {
            options ??= new Kh2FbxExportOptions();
            string rootName = options.RootName;

            long nextId = 1_000_000;
            long NewId() => nextId++;

            List<FbxNode> objects = new();
            List<FbxNode> connections = new();
            Dictionary<string, int> typeCounts = new();
            void AddObject(FbxNode node) { objects.Add(node); typeCounts[node.Name] = typeCounts.GetValueOrDefault(node.Name) + 1; }
            void ConnectOO(long child, long parent) => connections.Add(new FbxNode("C", "OO", child, parent));
            void ConnectOP(long child, long parent, string property) => connections.Add(new FbxNode("C", "OP", child, parent, property));

            List<ModelCommon.Bone> bones = model.Bones;
            RestPose[] rest = bones.Select(RestPose.Of).ToArray();

            // --- Skeleton -------------------------------------------------------------------
            long rootId = NewId();
            FbxNode rootAttr = new FbxNode("NodeAttribute", NewId(), FbxNode.ObjectName(rootName, "NodeAttribute"), "Null");
            rootAttr.Add("TypeFlags", "Null");
            AddObject(rootAttr);
            AddObject(ModelNode(rootId, rootName, "Null", Vector3.Zero, Vector3.Zero, Vector3.One));
            ConnectOO(rootId, 0);
            ConnectOO((long)rootAttr.Props[0], rootId);

            long[] boneIds = new long[bones.Count];
            Matrix4x4[] boneGlobal = new Matrix4x4[bones.Count];
            for (int b = 0; b < bones.Count; b++)
            {
                int parent = bones[b].ParentIndex;
                boneIds[b] = NewId();
                boneGlobal[b] = parent >= 0 ? rest[b].Local * boneGlobal[parent] : rest[b].Local;

                AddObject(ModelNode(boneIds[b], BoneName(b), "LimbNode", rest[b].Translation, rest[b].EulerDegrees, rest[b].Scale));
                ConnectOO(boneIds[b], parent >= 0 ? boneIds[parent] : rootId);

                FbxNode attr = new FbxNode("NodeAttribute", NewId(), FbxNode.ObjectName(BoneName(b), "NodeAttribute"), "LimbNode");
                attr.Add("TypeFlags", "Skeleton");
                AddObject(attr);
                ConnectOO((long)attr.Props[0], boneIds[b]);
            }

            // --- Textures (one Video per image file, shared by materials) ---------------------
            long[] videoIds = new long[textureFileCount];
            for (int i = 0; i < textureFileCount; i++)
            {
                videoIds[i] = NewId();
                string file = TextureFileName(i);
                string relative = Path.Combine("Textures", file);
                string absolute = Path.Combine(textureDir!, file);
                FbxNode video = new FbxNode("Video", videoIds[i], FbxNode.ObjectName(file, "Video"), "Clip");
                video.Add("Type", "Clip");
                video.Add("Properties70").Add(P("Path", "KString", "Url", "", absolute));
                video.Add("UseMipMap", 0);
                video.Add("Filename", absolute);
                video.Add("RelativeFilename", relative);
                AddObject(video);
            }

            // --- Meshes -----------------------------------------------------------------------
            List<long> meshModelIds = new();
            for (int g = 0; g < model.Groups.Count; g++)
            {
                ModelSkeletal.SkeletalGroup group = model.Groups[g];
                List<ModelCommon.UVBVertex> verts = group.Mesh.Vertices;

                long meshId = NewId(), geomId = NewId(), skinId = NewId(), materialId = NewId();
                meshModelIds.Add(meshId);
                AddObject(ModelNode(meshId, MeshName(g), "Mesh", Vector3.Zero, Vector3.Zero, Vector3.One));
                ConnectOO(meshId, 0);

                AddObject(Geometry(geomId, g, group));
                ConnectOO(geomId, meshId);

                // Material: texture wired to DiffuseColor and TransparencyFactor, so importers
                // that derive transparency from the latter pick up the texture's alpha.
                FbxNode material = new FbxNode("Material", materialId, FbxNode.ObjectName($"Material{g:D4}", "Material"), "");
                material.Add("Version", 102);
                material.Add("ShadingModel", "Phong");
                material.Add("MultiLayer", 0);
                FbxNode matProps = material.Add("Properties70");
                matProps.Add(P("DiffuseColor", "Color", "", "A", 1.0, 1.0, 1.0));
                matProps.Add(P("AmbientColor", "Color", "", "A", 0.0, 0.0, 0.0));
                matProps.Add(P("AmbientFactor", "Number", "", "A", 0.0));
                matProps.Add(P("SpecularColor", "Color", "", "A", 1.0, 1.0, 1.0));
                matProps.Add(P("SpecularFactor", "Number", "", "A", 0.25));
                matProps.Add(P("Shininess", "Number", "", "A", 20.0));
                matProps.Add(P("ShininessExponent", "Number", "", "A", 20.0));
                matProps.Add(P("ReflectionColor", "Color", "", "A", 1.0, 1.0, 1.0));
                matProps.Add(P("ReflectionFactor", "Number", "", "A", 0.0));
                AddObject(material);
                ConnectOO(materialId, meshId);

                int textureIndex = (int)group.Header.TextureIndex;
                if (textureIndex < textureFileCount)
                {
                    foreach (string slot in new[] { "DiffuseColor", "TransparencyFactor" })
                    {
                        long texId = NewId();
                        string texName = $"Material{g:D4}_{slot}";
                        string file = TextureFileName(textureIndex);
                        FbxNode texture = new FbxNode("Texture", texId, FbxNode.ObjectName(texName, "Texture"), "");
                        texture.Add("Type", "TextureVideoClip");
                        texture.Add("Version", 202);
                        texture.Add("TextureName", FbxNode.ObjectName(texName, "Texture"));
                        texture.Add("Properties70").Add(P("UseMaterial", "bool", "", "", 1));
                        texture.Add("Media", FbxNode.ObjectName(file, "Video"));
                        texture.Add("FileName", Path.Combine(textureDir!, file));
                        texture.Add("RelativeFilename", Path.Combine("Textures", file));
                        AddObject(texture);
                        ConnectOP(texId, materialId, slot);
                        ConnectOO(videoIds[textureIndex], texId);
                    }
                }

                // Skin: one cluster per bone (empty for unweighted bones, as Blender writes it).
                FbxNode skin = new FbxNode("Deformer", skinId, FbxNode.ObjectName(MeshName(g), "Deformer"), "Skin");
                skin.Add("Version", 101);
                skin.Add("Link_DeformAcuracy", 50.0);
                AddObject(skin);
                ConnectOO(skinId, geomId);

                SortedDictionary<int, double>[] weightsPerBone = new SortedDictionary<int, double>[bones.Count];
                for (int v = 0; v < verts.Count; v++)
                {
                    foreach (ModelCommon.BPosition bp in verts[v].BPositions)
                    {
                        SortedDictionary<int, double> map = weightsPerBone[bp.BoneIndex] ??= new();
                        map[v] = map.GetValueOrDefault(v) + (bp.Position.W == 0 ? 1.0 : bp.Position.W);
                    }
                }

                for (int b = 0; b < bones.Count; b++)
                {
                    long clusterId = NewId();
                    FbxNode cluster = new FbxNode("Deformer", clusterId, FbxNode.ObjectName(BoneName(b), "SubDeformer"), "Cluster");
                    cluster.Add("Version", 100);
                    cluster.Add("UserData", "", "");
                    if (weightsPerBone[b] is { } weights)
                    {
                        cluster.Add("Indexes", weights.Keys.ToArray());
                        cluster.Add("Weights", weights.Values.ToArray());
                    }
                    Matrix4x4.Invert(boneGlobal[b], out Matrix4x4 inverseBind);
                    cluster.Add("Transform", ToArray(inverseBind));
                    cluster.Add("TransformLink", ToArray(boneGlobal[b]));
                    cluster.Add("TransformAssociateModel", ToArray(Matrix4x4.Identity));
                    AddObject(cluster);
                    ConnectOO(clusterId, skinId);
                    ConnectOO(boneIds[b], clusterId);
                }
            }

            // --- Bind pose --------------------------------------------------------------------
            FbxNode pose = new FbxNode("Pose", NewId(), FbxNode.ObjectName(rootName, "Pose"), "BindPose");
            pose.Add("Type", "BindPose");
            pose.Add("Version", 100);
            pose.Add("NbPoseNodes", meshModelIds.Count + 1 + bones.Count);
            void PoseNode(long id, Matrix4x4 m) { FbxNode pn = pose.Add("PoseNode"); pn.Add("Node", id); pn.Add("Matrix", ToArray(m)); }
            foreach (long meshId in meshModelIds)
                PoseNode(meshId, Matrix4x4.Identity);
            PoseNode(rootId, Matrix4x4.Identity);
            for (int b = 0; b < bones.Count; b++)
                PoseNode(boneIds[b], boneGlobal[b]);
            AddObject(pose);

            // --- Animation --------------------------------------------------------------------
            // A "BindPose" take goes first: some importers build the default pose from frame 0
            // of the FIRST take (not from Lcl/bind pose) and fall back to it for anything a clip
            // doesn't key. Without it, unkeyed bones/axes snap to the first clip's first frame.
            FbxNode takes = new FbxNode("Takes");
            takes.Add("Current", "");
            IEnumerable<SampledClip> allClips = options.WriteBindPoseTake ? clips.Prepend(SampledClip.BindPose(rest)) : clips;
            foreach (SampledClip clip in allClips)
            {
                string takeName = $"{rootName}|{clip.Name}";
                long stop = ToKTime(clip.DurationTicks);

                long stackId = NewId(), layerId = NewId();
                FbxNode stack = new FbxNode("AnimationStack", stackId, FbxNode.ObjectName(takeName, "AnimStack"), "");
                FbxNode stackProps = stack.Add("Properties70");
                stackProps.Add(P("LocalStop", "KTime", "Time", "", stop));
                stackProps.Add(P("ReferenceStop", "KTime", "Time", "", stop));
                AddObject(stack);
                AddObject(new FbxNode("AnimationLayer", layerId, FbxNode.ObjectName(takeName, "AnimLayer"), ""));
                ConnectOO(layerId, stackId);

                for (int b = 0; b < bones.Count; b++)
                {
                    Vector3 restT = rest[b].Translation, restS = rest[b].Scale;
                    Quaternion restQ = rest[b].Rotation;

                    // Channels that sit at the bind pose for the whole clip get no curves - except
                    // every bone keeps at least one curve. Blender only resets bones the active
                    // action keys (an unkeyed bone keeps whatever pose the previous action left),
                    // and one curve is enough for its importer to key the bone fully, filling the
                    // other axes from the bind pose. Also guarantees no take is empty.
                    int boneCurves = 0;
                    if (clip.Translation[b].Any(t => Vector3.Distance(t, restT) > TranslationTolerance))
                        boneCurves += AddCurveNode("T", "Lcl Translation", clip.Translation[b], restT, TranslationTolerance, null, false);
                    if (clip.Scale[b].Any(s => Vector3.Distance(s, restS) > ScaleTolerance))
                        boneCurves += AddCurveNode("S", "Lcl Scaling", clip.Scale[b], restS, ScaleTolerance, null, false);
                    if (boneCurves == 0 || clip.Rotation[b].Any(q => AngleDegrees(q, restQ) > RotationToleranceDegrees))
                        AddCurveNode("R", "Lcl Rotation", clip.RotationDegrees[b], rest[b].EulerDegrees, RotationToleranceDegrees,
                            CurveReducer.ReduceRotation(clip.Times, clip.RotationDegrees[b], clip.Rotation[b], RotationToleranceDegrees), forceFirstAxis: boneCurves == 0);

                    // sharedKeys: key sample indices used for all three axes (rotation); null reduces
                    // each axis on its own (translation/scale interpolate linearly everywhere).
                    int AddCurveNode(string shortName, string property, Vector3[] samples, Vector3 rest, float tolerance, int[]? sharedKeys, bool forceFirstAxis)
                    {
                        float[] times = clip.Times;
                        int written = 0;
                        long nodeId = NewId();
                        FbxNode curveNode = new FbxNode("AnimationCurveNode", nodeId, FbxNode.ObjectName(shortName, "AnimCurveNode"), "");
                        FbxNode nodeProps = curveNode.Add("Properties70");
                        AddObject(curveNode);
                        ConnectOO(nodeId, layerId);
                        ConnectOP(nodeId, boneIds[b], property);

                        string[] axes = { "X", "Y", "Z" };
                        for (int axis = 0; axis < 3; axis++)
                        {
                            float[] values = samples.Select(s => axis == 0 ? s.X : axis == 1 ? s.Y : s.Z).ToArray();
                            float restValue = axis == 0 ? rest.X : axis == 1 ? rest.Y : rest.Z;
                            bool flat = values.Max() - values.Min() <= tolerance;
                            (float[] keyTimes, float[] keyValues) = flat
                                ? (new[] { times[0] }, new[] { values[0] })
                                : sharedKeys != null
                                    ? (sharedKeys.Select(i => times[i]).ToArray(), sharedKeys.Select(i => values[i]).ToArray())
                                    : CurveReducer.Reduce(times, values, tolerance);

                            // An axis pinned at its rest value needs no curve: importers fill a
                            // missing axis from the bone's Lcl value or the default (BindPose) pose,
                            // and both equal rest.
                            if (!(forceFirstAxis && axis == 0) && flat && MathF.Abs(values[0] - restValue) <= tolerance)
                            {
                                nodeProps.Add(P("d|" + axes[axis], "Number", "", "A", (double)restValue));
                                continue;
                            }
                            nodeProps.Add(P("d|" + axes[axis], "Number", "", "A", (double)keyValues[0]));

                            // Flat curves still span the whole clip, so a take whose curves are all
                            // flat keeps its real length in importers that derive it from keys.
                            if (keyTimes.Length == 1 && times.Length > 1)
                                (keyTimes, keyValues) = (new[] { times[0], times[^1] }, new[] { keyValues[0], keyValues[0] });

                            long curveId = NewId();
                            FbxNode curve = new FbxNode("AnimationCurve", curveId, FbxNode.ObjectName("", "AnimCurve"), "");
                            curve.Add("Default", (double)keyValues[0]);
                            curve.Add("KeyVer", 4008);
                            curve.Add("KeyTime", keyTimes.Select(ToKTime).ToArray());
                            curve.Add("KeyValueFloat", keyValues);
                            // Linear interpolation for every key (flags/data copied from Blender).
                            curve.Add("KeyAttrFlags", new[] { 24836 });
                            curve.Add("KeyAttrDataFloat", new[] { 0f, 0f, 9.419963346924634e-30f, 0f });
                            curve.Add("KeyAttrRefCount", new[] { keyTimes.Length });
                            AddObject(curve);
                            ConnectOP(curveId, nodeId, "d|" + axes[axis]);
                            written++;
                        }
                        return written;
                    }
                }

                FbxNode take = takes.Add("Take", takeName);
                take.Add("FileName", takeName + ".tak");
                take.Add("LocalTime", 0L, stop);
                take.Add("ReferenceTime", 0L, stop);
            }

            // --- Document ---------------------------------------------------------------------
            long maxStop = clips.Count == 0 ? KTimePerSecond : clips.Max(c => ToKTime(c.DurationTicks));
            List<FbxNode> document = new()
            {
                HeaderExtension(),
                new FbxNode("FileId", FbxBinaryWriter.FileIdBytes),
                new FbxNode("CreationTime", "1970-01-01 10:00:00:000"),
                new FbxNode("Creator", Creator),
                GlobalSettings(maxStop),
                Documents(),
                new FbxNode("References"),
                Definitions(typeCounts),
                WithChildren(new FbxNode("Objects"), objects),
                WithChildren(new FbxNode("Connections"), connections),
                takes,
            };
            FbxBinaryWriter.Write(path, document);
        }

        public static string TextureFileName(int index) => $"Texture{index:D4}.png";

        private const string Creator = "kh2fbxexport";

        private static string BoneName(int index) => $"Bone{index:D4}";

        private static string MeshName(int index) => $"MeshNode{index:D4}";

        private static long ToKTime(float ticks) => (long)Math.Round(ticks * KTimePerTick);

        private static double AngleDegrees(Quaternion a, Quaternion b)
        {
            double dot = Math.Min(1.0, Math.Abs(Quaternion.Dot(a, b)));
            return 2.0 * Math.Acos(dot) * 180.0 / Math.PI;
        }

        // System.Numerics row-vector matrices flattened row by row put translation at elements
        // 12..14 - the same layout FBX stores (column-major, column-vector convention).
        private static double[] ToArray(Matrix4x4 m) => new double[]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44,
        };

        private static FbxNode P(string name, string type, string label, string flags, params object[] values)
        {
            List<object> props = new() { name, type, label, flags };
            props.AddRange(values);
            return new FbxNode("P", props.ToArray());
        }

        private static FbxNode WithChildren(FbxNode node, IEnumerable<FbxNode> children)
        {
            node.Children.AddRange(children);
            return node;
        }

        private static FbxNode ModelNode(long id, string name, string type, Vector3 translation, Vector3 rotationDegrees, Vector3 scale)
        {
            FbxNode model = new FbxNode("Model", id, FbxNode.ObjectName(name, "Model"), type);
            model.Add("Version", 232);
            FbxNode props = model.Add("Properties70");
            props.Add(P("Lcl Translation", "Lcl Translation", "", "A", (double)translation.X, (double)translation.Y, (double)translation.Z));
            props.Add(P("Lcl Rotation", "Lcl Rotation", "", "A", (double)rotationDegrees.X, (double)rotationDegrees.Y, (double)rotationDegrees.Z));
            props.Add(P("Lcl Scaling", "Lcl Scaling", "", "A", (double)scale.X, (double)scale.Y, (double)scale.Z));
            props.Add(P("DefaultAttributeIndex", "int", "Integer", "", 0));
            // RSrs: child local transform is applied inside the parent's full matrix, which is
            // KH2's own plain local*parentGlobal composition.
            props.Add(P("InheritType", "enum", "", "", 1));
            model.Add("MultiLayer", 0);
            model.Add("MultiTake", 0);
            model.Add("Shading", true);
            model.Add("Culling", "CullingOff");
            return model;
        }

        private static FbxNode Geometry(long id, int index, ModelSkeletal.SkeletalGroup group)
        {
            List<ModelCommon.UVBVertex> verts = group.Mesh.Vertices;

            double[] positions = new double[verts.Count * 3];
            double[] uvs = new double[verts.Count * 2];
            for (int v = 0; v < verts.Count; v++)
            {
                positions[v * 3] = verts[v].Position.X;
                positions[v * 3 + 1] = verts[v].Position.Y;
                positions[v * 3 + 2] = verts[v].Position.Z;
                uvs[v * 2] = verts[v].U / 4096.0;
                uvs[v * 2 + 1] = 1 - verts[v].V / 4096.0;
            }

            // Last index of each polygon is stored as ~index (-index - 1).
            int[] polygonIndices = new int[group.Mesh.Triangles.Count * 3];
            for (int t = 0; t < group.Mesh.Triangles.Count; t++)
            {
                List<int> tri = group.Mesh.Triangles[t];
                polygonIndices[t * 3] = tri[0];
                polygonIndices[t * 3 + 1] = tri[1];
                polygonIndices[t * 3 + 2] = ~tri[2];
            }
            int[] uvIndices = polygonIndices.Select(i => i < 0 ? ~i : i).ToArray();

            FbxNode geom = new FbxNode("Geometry", id, FbxNode.ObjectName($"Mesh{index:D4}", "Geometry"), "Mesh");
            geom.Add("Properties70");
            geom.Add("GeometryVersion", 124);
            geom.Add("Vertices", positions);
            geom.Add("PolygonVertexIndex", polygonIndices);

            FbxNode layer = new FbxNode("Layer", 0);
            layer.Add("Version", 100);

            if (verts.Count > 0 && verts.All(v => v.Normal != null))
            {
                double[] normals = verts.SelectMany(v => new double[] { v.Normal.X, v.Normal.Y, v.Normal.Z }).ToArray();
                FbxNode normalLayer = geom.Add("LayerElementNormal", 0);
                normalLayer.Add("Version", 101);
                normalLayer.Add("Name", "");
                normalLayer.Add("MappingInformationType", "ByVertice");
                normalLayer.Add("ReferenceInformationType", "Direct");
                normalLayer.Add("Normals", normals);
                FbxNode le = layer.Add("LayerElement");
                le.Add("Type", "LayerElementNormal");
                le.Add("TypedIndex", 0);
            }

            FbxNode uvLayer = geom.Add("LayerElementUV", 0);
            uvLayer.Add("Version", 101);
            uvLayer.Add("Name", "UVMap");
            uvLayer.Add("MappingInformationType", "ByPolygonVertex");
            uvLayer.Add("ReferenceInformationType", "IndexToDirect");
            uvLayer.Add("UV", uvs);
            uvLayer.Add("UVIndex", uvIndices);
            FbxNode uvLe = layer.Add("LayerElement");
            uvLe.Add("Type", "LayerElementUV");
            uvLe.Add("TypedIndex", 0);

            FbxNode matLayer = geom.Add("LayerElementMaterial", 0);
            matLayer.Add("Version", 101);
            matLayer.Add("Name", "");
            matLayer.Add("MappingInformationType", "AllSame");
            matLayer.Add("ReferenceInformationType", "IndexToDirect");
            matLayer.Add("Materials", new[] { 0 });
            FbxNode matLe = layer.Add("LayerElement");
            matLe.Add("Type", "LayerElementMaterial");
            matLe.Add("TypedIndex", 0);

            geom.Add(layer);
            return geom;
        }

        private static FbxNode HeaderExtension()
        {
            FbxNode header = new FbxNode("FBXHeaderExtension");
            header.Add("FBXHeaderVersion", 1003);
            header.Add("FBXVersion", 7400);
            header.Add("EncryptionType", 0);
            FbxNode stamp = header.Add("CreationTimeStamp");
            stamp.Add("Version", 1000);
            foreach (string field in new[] { "Year", "Month", "Day", "Hour", "Minute", "Second", "Millisecond" })
                stamp.Add(field, field == "Year" ? 1970 : field is "Month" or "Day" ? 1 : 0);
            header.Add("Creator", Creator);
            return header;
        }

        private static FbxNode GlobalSettings(long timeSpanStop)
        {
            FbxNode settings = new FbxNode("GlobalSettings");
            settings.Add("Version", 1000);
            FbxNode props = settings.Add("Properties70");
            props.Add(P("UpAxis", "int", "Integer", "", 1));
            props.Add(P("UpAxisSign", "int", "Integer", "", 1));
            props.Add(P("FrontAxis", "int", "Integer", "", 2));
            props.Add(P("FrontAxisSign", "int", "Integer", "", 1));
            props.Add(P("CoordAxis", "int", "Integer", "", 0));
            props.Add(P("CoordAxisSign", "int", "Integer", "", 1));
            props.Add(P("OriginalUpAxis", "int", "Integer", "", -1));
            props.Add(P("OriginalUpAxisSign", "int", "Integer", "", 1));
            props.Add(P("UnitScaleFactor", "double", "Number", "", 1.0));
            props.Add(P("OriginalUnitScaleFactor", "double", "Number", "", 1.0));
            props.Add(P("AmbientColor", "ColorRGB", "Color", "", 0.0, 0.0, 0.0));
            props.Add(P("DefaultCamera", "KString", "", "", "Producer Perspective"));
            props.Add(P("TimeMode", "enum", "", "", 3)); // 60 fps
            props.Add(P("TimeSpanStart", "KTime", "Time", "", 0L));
            props.Add(P("TimeSpanStop", "KTime", "Time", "", timeSpanStop));
            props.Add(P("CustomFrameRate", "double", "Number", "", 60.0));
            return settings;
        }

        private static FbxNode Documents()
        {
            FbxNode documents = new FbxNode("Documents");
            documents.Add("Count", 1);
            FbxNode doc = documents.Add("Document", 1L, "Scene", "Scene");
            FbxNode props = doc.Add("Properties70");
            props.Add(P("SourceObject", "object", "", ""));
            props.Add(P("ActiveAnimStackName", "KString", "", "", ""));
            doc.Add("RootNode", 0L);
            return documents;
        }

        private static FbxNode Definitions(Dictionary<string, int> typeCounts)
        {
            FbxNode defs = new FbxNode("Definitions");
            defs.Add("Version", 100);
            defs.Add("Count", typeCounts.Values.Sum() + 1);
            defs.Add("ObjectType", "GlobalSettings").Add("Count", 1);
            foreach ((string type, int count) in typeCounts)
                defs.Add("ObjectType", type).Add("Count", count);
            return defs;
        }
    }
}
