using OpenKh.AssimpUtils;
using OpenKh.Kh2;
using OpenKh.Kh2.Models;
using OpenKh.Kh2Fbx;
using OpenKh.Tools.Common.Wpf;
using OpenKh.Tools.Kh2ObjectEditor.Classes;
using OpenKh.Tools.Kh2ObjectEditor.Services;
using System.Collections.Generic;
using System.IO;

namespace OpenKh.Tools.Kh2ObjectEditor.Utils
{
    // FBX export of the loaded model and its motions through OpenKh.Kh2Fbx's direct FBX writer.
    // Textures are written as <fbx folder>/Textures/TextureNNNN.png, which the FBX references.
    public static class Kh2FbxExport
    {
        public static ModelSkeletal LoadModel()
        {
            ModelSkeletal model = null;
            foreach (Bar.Entry barEntry in MdlxService.Instance.MdlxBar)
            {
                if (barEntry.Type == Bar.EntryType.Model)
                {
                    model = ModelSkeletal.Read(barEntry.Stream);
                    barEntry.Stream.Position = 0;
                }
            }
            return model;
        }

        // The animation emulator reads the model from a serialized mdlx.
        public static byte[] GetMdlxBytes()
        {
            using MemoryStream stream = new MemoryStream();
            Bar.Write(stream, MdlxService.Instance.MdlxBar);
            return stream.ToArray();
        }

        public static bool IsExportable(MotionSelector_Wrapper motion) =>
            motion.Entry.Type == BinaryArchive.EntryType.Anb && !motion.IsDummy;

        public static string ClipName(MotionSelector_Wrapper motion)
        {
            string name = motion.Entry.Name.Replace("\0", "").Trim();
            return string.IsNullOrEmpty(name) ? $"Motion{motion.Index:0000}" : $"{motion.Index:0000}_{name}";
        }

        public static SampledClip Sample(ModelSkeletal model, byte[] mdlxBytes, MotionSelector_Wrapper motion)
        {
            AnimationBinary animation;
            using (MemoryStream stream = new MemoryStream(motion.LinkedSubfile))
                animation = new AnimationBinary(stream);
            return ClipSampler.Sample(mdlxBytes, model.Bones, animation, ClipName(motion));
        }

        public static void Write(string fbxPath, ModelSkeletal model, IReadOnlyList<SampledClip> clips)
        {
            string textureDir = Path.Combine(Path.GetDirectoryName(fbxPath), "Textures");
            int textureCount = WriteTextures(textureDir);
            Kh2FbxExporter.Write(fbxPath, model, clips, textureDir, textureCount);
        }

        private static int WriteTextures(string textureDir)
        {
            List<ModelTexture.Texture> images = MdlxService.Instance.TextureFile?.Images;
            if (images == null || images.Count == 0)
                return 0;

            Directory.CreateDirectory(textureDir);
            for (int i = 0; i < images.Count; i++)
            {
                // ExportBitmapSourceAsPng appends ".png" itself
                string path = Path.Combine(textureDir, Path.GetFileNameWithoutExtension(Kh2FbxExporter.TextureFileName(i)));
                AssimpGeneric.ExportBitmapSourceAsPng(images[i].GetBimapSource(), path);
            }
            return images.Count;
        }
    }
}
