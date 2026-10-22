using OpenKh.Engine;
using System.Numerics;
using System.Windows.Media.Imaging;

namespace OpenKh.AssimpUtils
{
    public class AssimpGeneric
    {
        // SharpAssimp matrices use Assimp's row-major layout (translation in M14/M24/M34),
        // the transpose of the System.Numerics/XNA convention.
        public static Microsoft.Xna.Framework.Matrix ToXna(Matrix4x4 m) => new Microsoft.Xna.Framework.Matrix(m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43, m.M14, m.M24, m.M34, m.M44);
        public static Matrix4x4 ToAssimp(Microsoft.Xna.Framework.Matrix m) => new Matrix4x4(m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43, m.M14, m.M24, m.M34, m.M44);

        public static Matrix4x4 ToAssimp(Matrix4x4 m) => Matrix4x4.Transpose(m);
        public static Matrix4x4 ToNumerics(Matrix4x4 m) => Matrix4x4.Transpose(m);
        public static Vector3 ToAssimp(Vector3 v) => v;
        public static Vector3 ToNumerics(Vector3 v) => v;

        // File formats supported by Assimp
        public enum FileFormat
        {
            // Common
            collada,
            fbx,
            fbxa,
            obj,

            // Other
            x,
            stp,
            objnomtl,
            stl,
            stlb,
            ply,
            plyb,
            //3ds,
            gltf2,
            glb2,
            gltf,
            glb,
            assbin,
            assxml,
            x3d,
            m3d,
            m3da,
            //3mf,
            pbrt,
            assjson
        }

        public static SharpAssimp.Scene getAssimpSceneFromFile(string filePath)
        {
            SharpAssimp.AssimpContext assimp = new SharpAssimp.AssimpContext();
            SharpAssimp.Scene scene = assimp.ImportFile(filePath);

            return scene;
        }

        // Returns the file extension that the given format uses
        public static string GetFormatFileExtension(FileFormat format)
        {
            switch (format)
            {
                case FileFormat.collada:
                    return "dae";
                case FileFormat.fbx:
                case FileFormat.fbxa:
                    return "fbx";
                default:
                    return format.ToString();
            }
        }
        // Exports the given Assimp scene in the given format
        public static void ExportScene(SharpAssimp.Scene scene, FileFormat format = FileFormat.fbx, string filename = "fileout.fbx")
        {
            SharpAssimp.AssimpContext context = new SharpAssimp.AssimpContext();
            context.ExportFile(scene, filename, format.ToString());
        }
        public static void ExportSceneWithDialog(SharpAssimp.Scene scene, FileFormat format = FileFormat.fbx, string filename = "fileout")
        {
            SaveFileDialog sfd;
            sfd = new SaveFileDialog();
            sfd.Title = "Save file";
            sfd.FileName = filename + "." + GetFormatFileExtension(format);
            sfd.ShowDialog();
            if (sfd.FileName != "")
            {
                SharpAssimp.AssimpContext context = new SharpAssimp.AssimpContext();
                context.ExportFile(scene, sfd.FileName, format.ToString());
            }
        }
        

        // Gets a basic Assimp scene. It also contains notes on how to make a valid Assimp scene for exporting
        public static SharpAssimp.Scene GetBaseScene()
        {
            SharpAssimp.Scene scene = new SharpAssimp.Scene();
            scene.RootNode = new SharpAssimp.Node("RootNode");

            return scene;

            /* To get a valid assimp scene you need the following:
             * A mesh.
             * A mesh node. One per mesh.
             * Material + texture for the mesh. (Optional)
             * Bones (Weights) for the mesh. Note that Assimp requires each mesh to have all bones even if they have no vertex weights (Optional)
             * Vertices for the mesh.
             * Faces for the mesh.
             * Nodes (Skeleton) as children of rootnode. One per bone. Same name as the bones.
             */
        }

        // Returns the Transform matrix used by Assimp nodes based on SRT data. Note: the rotation uses quaternions
        public static Matrix4x4 GetNodeTransformMatrix(Vector3 scale, Vector3 radianRotation, Vector3 translation)
        {
            Matrix4x4 nodeTransform = Matrix4x4.Identity;

            nodeTransform = AssimpMath.Multiply(nodeTransform, AssimpMath.FromScaling(scale));

            Matrix4x4 rotation = AssimpMath.Multiply(
                                    AssimpMath.Multiply(AssimpMath.FromRotationX(radianRotation.X),
                                                        AssimpMath.FromRotationY(radianRotation.Y)),
                                    AssimpMath.FromRotationZ(radianRotation.Z));

            nodeTransform = AssimpMath.Multiply(nodeTransform, rotation);

            nodeTransform = AssimpMath.Multiply(nodeTransform, AssimpMath.FromTranslation(translation));

            return nodeTransform;
        }

        // Finds a bone by name in a bone list
        public static SharpAssimp.Bone FindBone(List<SharpAssimp.Bone> list_bones, string boneName)
        {
            foreach(SharpAssimp.Bone bone in list_bones)
            {
                if (bone.Name == boneName)
                    return bone;
            }

            return null;
        }

        // Adds the given weight to the mesh's bone. Usually uneeded since all meshes need to have all bones and it's done in one go
        public static void AddVertexWeight(SharpAssimp.Mesh mesh, int boneId, int vertexId, float weight = 1)
        {
            bool boneExists = false;
            SharpAssimp.Bone bone = FindBone(mesh.Bones, "Bone" + boneId);
            SharpAssimp.VertexWeight vertexWeight = new SharpAssimp.VertexWeight(vertexId, weight);

            if (bone != null)
            {
                bone.VertexWeights.Add(vertexWeight);
            }
            else
            {
                List<SharpAssimp.VertexWeight> boneWeights = new List<SharpAssimp.VertexWeight>();
                boneWeights.Add(vertexWeight);
                mesh.Bones.Add(new SharpAssimp.Bone("Bone" + boneId, SharpAssimp.Matrix3x3.Identity, boneWeights.ToArray()));
            }
        }

        // Not Assimp related but exporting images is needed
        public static void ExportBitmapSourceAsPng(BitmapSource image, string filePath)
        {
            using (var fileStream = new FileStream(filePath + ".png", FileMode.Create))
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
            }
        }
    }
}
