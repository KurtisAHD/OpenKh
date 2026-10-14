using System.IO.Compression;
using System.Text;

namespace OpenKh.Kh2Fbx
{
    // One FBX record: a name, a list of typed properties, and child records. Property CLR type
    // decides the FBX type code (see WriteProperty). Use FbxNode.ObjectName for the
    // "Name\0\x01Class" form FBX binary uses wherever ASCII FBX shows "Class::Name".
    internal sealed class FbxNode
    {
        public string Name { get; }
        public List<object> Props { get; }
        public List<FbxNode> Children { get; } = new();

        public FbxNode(string name, params object[] props)
        {
            Name = name;
            Props = props.ToList();
        }

        public FbxNode Add(string name, params object[] props)
        {
            FbxNode child = new FbxNode(name, props);
            Children.Add(child);
            return child;
        }

        public FbxNode Add(FbxNode child)
        {
            Children.Add(child);
            return child;
        }

        public static string ObjectName(string name, string cls) => name + "\0\u0001" + cls;
    }

    // FBX 7.4 binary encoder. Layout (header, null-record rules, footer) mirrors Blender's
    // io_scene_fbx/encode_bin.py exactly, since Blender's output is known to load in both
    // Blender and Unity's FBX SDK.
    internal static class FbxBinaryWriter
    {
        private const int Version = 7400;
        private const int SentinelLength = 13; // 3 x uint32 + 1 byte, all zero
        private static readonly byte[] HeadMagic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\u001a\0");
        private static readonly byte[] FileId = { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 };
        private static readonly byte[] FootId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
        private static readonly byte[] FootMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };
        private static readonly HashSet<string> AlwaysSentinel = new() { "AnimationStack", "AnimationLayer" };

        public static byte[] FileIdBytes => FileId;

        public static void Write(string path, IReadOnlyList<FbxNode> topLevel)
        {
            using FileStream fs = File.Create(path);
            using BinaryWriter w = new BinaryWriter(fs);

            w.Write(HeadMagic);
            w.Write(Version);

            for (int i = 0; i < topLevel.Count; i++)
                WriteNode(w, topLevel[i], isLast: i == topLevel.Count - 1);
            w.Write(new byte[SentinelLength]);

            w.Write(FootId);
            w.Write(new byte[4]);
            long pos = fs.Position;
            int pad = (int)(((pos + 15) & ~15L) - pos);
            w.Write(new byte[pad == 0 ? 16 : pad]);
            w.Write(Version);
            w.Write(new byte[120]);
            w.Write(FootMagic);
        }

        private static void WriteNode(BinaryWriter w, FbxNode node, bool isLast)
        {
            Stream s = w.BaseStream;
            long headerPos = s.Position;
            w.Write(0u); // end offset, patched below
            w.Write((uint)node.Props.Count);
            w.Write(0u); // property list length, patched below
            byte[] name = Encoding.ASCII.GetBytes(node.Name);
            w.Write((byte)name.Length);
            w.Write(name);

            long propsStart = s.Position;
            foreach (object prop in node.Props)
                WriteProperty(w, prop);
            long propsEnd = s.Position;

            if (node.Children.Count > 0)
            {
                for (int i = 0; i < node.Children.Count; i++)
                    WriteNode(w, node.Children[i], isLast: i == node.Children.Count - 1);
                w.Write(new byte[SentinelLength]);
            }
            else if ((node.Props.Count == 0 && !isLast) || AlwaysSentinel.Contains(node.Name))
            {
                w.Write(new byte[SentinelLength]);
            }

            long end = s.Position;
            s.Position = headerPos;
            w.Write((uint)end);
            s.Position = headerPos + 8;
            w.Write((uint)(propsEnd - propsStart));
            s.Position = end;
        }

        private static void WriteProperty(BinaryWriter w, object prop)
        {
            switch (prop)
            {
                case bool b: w.Write((byte)'C'); w.Write(b ? (byte)1 : (byte)0); break;
                case int v: w.Write((byte)'I'); w.Write(v); break;
                case long v: w.Write((byte)'L'); w.Write(v); break;
                case double v: w.Write((byte)'D'); w.Write(v); break;
                case string v:
                    byte[] str = Encoding.UTF8.GetBytes(v);
                    w.Write((byte)'S'); w.Write(str.Length); w.Write(str);
                    break;
                case byte[] v: w.Write((byte)'R'); w.Write(v.Length); w.Write(v); break;
                case int[] v: WriteArray(w, 'i', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan())); break;
                case long[] v: WriteArray(w, 'l', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan())); break;
                case float[] v: WriteArray(w, 'f', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan())); break;
                case double[] v: WriteArray(w, 'd', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan())); break;
                default: throw new ArgumentException($"Unsupported FBX property type {prop.GetType()}");
            }
        }

        // Arrays over 128 bytes are zlib-compressed (encoding 1), matching FBX SDK/Blender.
        private static void WriteArray(BinaryWriter w, char type, int count, ReadOnlySpan<byte> raw)
        {
            w.Write((byte)type);
            w.Write(count);
            if (raw.Length <= 128)
            {
                w.Write(0);
                w.Write(raw.Length);
                w.Write(raw);
                return;
            }

            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw);
            w.Write(1);
            w.Write((int)compressed.Length);
            w.Write(compressed.GetBuffer(), 0, (int)compressed.Length);
        }
    }
}
