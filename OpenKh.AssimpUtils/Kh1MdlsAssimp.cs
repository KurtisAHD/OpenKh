using OpenKh.Kh1;
using System.Numerics;
using static OpenKh.Kh1.Mdls;

namespace OpenKh.AssimpUtils
{
    public class Kh1MdlsAssimp
    {
        public static SharpAssimp.Scene getAssimpScene(Mdls model)
        {
            SharpAssimp.Scene scene = AssimpGeneric.GetBaseScene();

            List<SharpAssimp.TextureSlot> textures = new List<SharpAssimp.TextureSlot>();
            List<SharpAssimp.Material> materials = new List<SharpAssimp.Material>();
            for (int i = 0; i < model.Images.Count; i++)
            {
                SharpAssimp.TextureSlot texture = new SharpAssimp.TextureSlot();
                texture.FilePath = "Texture" + i.ToString("D4");
                texture.TextureType = SharpAssimp.TextureType.Diffuse;
                textures.Add(texture);
            }

            Matrix4x4[] jointMatrices = GetBoneMatrices(model.Joints);

            // Meshes
            //for (int i = 0; i < model.Meshes.Count; i++)
            for (int i = 0; i < 1; i++)
            {
                MdlsMesh mesh = model.Meshes[i];
                SharpAssimp.Mesh iMesh = new SharpAssimp.Mesh("Mesh" + i.ToString("D4"), SharpAssimp.PrimitiveType.Triangle);
                iMesh.UVComponentCount[0] = 2; // Required for some reason

                // MATERIAL
                SharpAssimp.Material mat = new SharpAssimp.Material();
                mat.Name = "Material" + i.ToString("D4");
                mat.TextureDiffuse = textures[mesh.Header.TextureIndex];
                //mat.TextureDiffuse = texture;
                iMesh.MaterialIndex = i;
                scene.Materials.Add(mat);

                // BONES - Assimp requires all of the bones even if they don't have weights
                for (int j = 0; j < model.Joints.Count; j++)
                {
                    iMesh.Bones.Add(new SharpAssimp.Bone("Bone" + j.ToString("D4"), SharpAssimp.Matrix3x3.Identity, new SharpAssimp.VertexWeight[0]));
                }

                /*
                // TRISTRIPS
                int currentVertex = 0;
                for (int j = 0; j < mesh.packet.TriangleStrips.Count; j++)
                {
                    if(j != 0)
                    {
                        continue;
                    }


                    List<MdlsVertex> strip = mesh.packet.TriangleStrips[j];
                    foreach (MdlsVertex vertex in strip)
                    {
                        Vector3 vertexPos = new Vector3(vertex.TranslateX, vertex.TranslateY, vertex.TranslateZ);

                        //MdlsJoint joint = model.Joints[vertex.JointId];
                        //
                        //SharpAssimp.Matrix4x4 jointMatrix = AssimpGeneric.GetNodeTransformMatrix(new Vector3(joint.ScaleX, joint.ScaleY, joint.ScaleZ),
                        //                                                        new Vector3(joint.RotateX, joint.RotateY, joint.RotateZ),
                        //                                                        new Vector3(joint.TranslateX, joint.TranslateY, joint.TranslateZ));
                        //
                        //Matrix4x4 jointMatrix2 = AssimpUtils.AssimpGeneric.ToNumerics(jointMatrix);
                        //Vector3 finalPos4 = Vector3.Transform(vertexPos, jointMatrix2);

                        Vector3 finalPos4 = Vector3.Transform(vertexPos, jointMatrices[vertex.JointId]);

                        iMesh.Vertices.Add(new Vector3(finalPos4.X, finalPos4.Y, finalPos4.Z));

                        iMesh.TextureCoordinateChannels[0].Add(new Vector3(vertex.TexCoordU, 1 - vertex.TexCoordV, vertex.TexCoord1));

                        // VERTEX WEIGHTS
                        SharpAssimp.Bone bone = AssimpGeneric.FindBone(iMesh.Bones, "Bone" + vertex.JointId.ToString("D4"));
                        bone.VertexWeights.Add(new SharpAssimp.VertexWeight(currentVertex, 1));

                        currentVertex++;

                        if (currentVertex >= 3)
                        {
                            // Counterclockwise
                            if (mesh.packet.StripHeaders[j].Unknown == 0)
                            {
                                if (strip.Count % 2 == 0)
                                {
                                    iMesh.Faces.Add(new SharpAssimp.Face(new int[3] { vertex.Index, vertex.Index - 1, vertex.Index - 2 }));
                                }
                                else
                                {
                                    iMesh.Faces.Add(new SharpAssimp.Face(new int[3] { vertex.Index, vertex.Index - 2, vertex.Index - 1 }));
                                }
                            }
                            // Clockwise
                            else
                            {
                                if (strip.Count % 2 == 0)
                                {
                                    iMesh.Faces.Add(new SharpAssimp.Face(new int[3] { vertex.Index, vertex.Index - 2, vertex.Index - 1 }));
                                }
                                else
                                {
                                    iMesh.Faces.Add(new SharpAssimp.Face(new int[3] { vertex.Index, vertex.Index - 1, vertex.Index - 2 }));
                                }
                            }
                        }
                    }
                }*/
                
                // VERTICES
                int currentVertex = 0;
                foreach (MdlsVertex vertex in mesh.packet.Vertices)
                {
                    Vector3 vertexPos = new Vector3(vertex.TranslateX, vertex.TranslateY, vertex.TranslateZ);

                    /*MdlsJoint joint = model.Joints[vertex.JointId];

                    SharpAssimp.Matrix4x4 jointMatrix = AssimpGeneric.GetNodeTransformMatrix(new Vector3(joint.ScaleX, joint.ScaleY, joint.ScaleZ),
                                                                            new Vector3(joint.RotateX, joint.RotateY, joint.RotateZ),
                                                                            new Vector3(joint.TranslateX, joint.TranslateY, joint.TranslateZ));

                    Matrix4x4 jointMatrix2 = AssimpUtils.AssimpGeneric.ToNumerics(jointMatrix);*/

                    Vector3 finalPosition = Vector3.Transform(vertexPos, jointMatrices[vertex.JointId]);
                    //Vector3 finalPos4 = Vector3.Transform(vertexPos, jointMatrix2);

                    iMesh.Vertices.Add(new Vector3(finalPosition.X, finalPosition.Y, finalPosition.Z));

                    iMesh.TextureCoordinateChannels[0].Add(new Vector3(vertex.TexCoordU, 1 - vertex.TexCoordV, vertex.TexCoord1));

                    // VERTEX WEIGHTS
                    SharpAssimp.Bone bone = AssimpGeneric.FindBone(iMesh.Bones, "Bone" + vertex.JointId.ToString("D4"));
                    bone.VertexWeights.Add(new SharpAssimp.VertexWeight(currentVertex, 1));

                    currentVertex++;
                }

                // FACES
                foreach(int[] faceVertices in mesh.packet.Faces)
                {
                    iMesh.Faces.Add(new SharpAssimp.Face(faceVertices));
                }

                scene.Meshes.Add(iMesh);

                // NODE
                SharpAssimp.Node iMeshNode = new SharpAssimp.Node("MeshNode" + i.ToString("D4"));
                iMeshNode.MeshIndices.Add(i);

                scene.RootNode.Children.Add(iMeshNode);
            }

            // BONES (Node hierarchy)
            foreach (MdlsJoint joint in model.Joints)
            {
                string boneName = "Bone" + joint.Index.ToString("D4");
                SharpAssimp.Node boneNode = new SharpAssimp.Node(boneName);

                SharpAssimp.Node parentNode;
                if (joint.ParentId == 1023)
                {
                    parentNode = scene.RootNode;
                }
                else
                {
                    parentNode = scene.RootNode.FindNode("Bone" + joint.ParentId.ToString("D4"));
                }

                boneNode.Transform = AssimpGeneric.GetNodeTransformMatrix(new Vector3(joint.ScaleX, joint.ScaleY, joint.ScaleZ),
                                                                          new Vector3(joint.RotateX, joint.RotateY, joint.RotateZ),
                                                                          new Vector3(joint.TranslateX, joint.TranslateY, joint.TranslateZ));

                parentNode.Children.Add(boneNode);
            }

            return scene;
        }

        public static Matrix4x4[] GetBoneMatrices(List<MdlsJoint> jointList)
        {
            Vector3[] absTranslationList = new Vector3[jointList.Count];
            Quaternion[] absRotationList = new Quaternion[jointList.Count];

            for (int i = 0; i < jointList.Count; i++)
            {
                MdlsJoint bone = jointList[i];
                int parentIndex = (int)bone.ParentId;
                Quaternion absRotation;
                Vector3 absTranslation;

                if (parentIndex == -1 || parentIndex == 1023)
                {
                    absRotation = Quaternion.Identity;
                    absTranslation = Vector3.Zero;
                }
                else
                {
                    absRotation = absRotationList[parentIndex];
                    absTranslation = absTranslationList[parentIndex];
                }

                Vector3 localTranslation = Vector3.Transform(new Vector3(bone.TranslateX, bone.TranslateY, bone.TranslateZ), Matrix4x4.CreateFromQuaternion(absRotation));
                absTranslationList[i] = absTranslation + localTranslation;

                var localRotation = Quaternion.Identity;
                if (bone.RotateX != 0)
                    localRotation *= (Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bone.RotateZ));
                if (bone.RotateY != 0)
                    localRotation *= (Quaternion.CreateFromAxisAngle(Vector3.UnitY, bone.RotateY));
                if (bone.RotateZ != 0)
                    localRotation *= (Quaternion.CreateFromAxisAngle(Vector3.UnitX, bone.RotateX));
                absRotationList[i] = absRotation * localRotation;
            }

            Matrix4x4[] matrices = new Matrix4x4[jointList.Count];
            for (int i = 0; i < jointList.Count; i++)
            {
                var absMatrix = Matrix4x4.Identity;
                absMatrix *= Matrix4x4.CreateFromQuaternion(absRotationList[i]);
                absMatrix *= Matrix4x4.CreateTranslation(absTranslationList[i]);
                matrices[i] = absMatrix;
            }

            return matrices;
        }
    }
}
