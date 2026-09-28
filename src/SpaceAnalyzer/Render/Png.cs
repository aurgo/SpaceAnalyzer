using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SpaceAnalyzer.Render;

/// <summary>Minimal PNG and ICO writer (RGBA, zlib through System.IO.Compression) for snapshots and icons.</summary>
public static class Png
{
    public static void Save(Surface s, string path)
    {
        using var file = File.Create(path);
        Write(s, file);
    }

    public static byte[] Encode(Surface s)
    {
        var ms = new MemoryStream();
        Write(s, ms);
        return ms.ToArray();
    }

    public static void Write(Surface s, Stream output)
    {
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, s.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), s.Height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        Chunk(output, "IHDR", header);

        int stride = s.Width * 4;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var prev = new byte[stride];
            var cur = new byte[stride];
            var best = new byte[stride + 1];
            var trial = new byte[stride + 1];
            for (int y = 0; y < s.Height; y++)
            {
                int o = 0;
                for (int x = 0; x < s.Width; x++)
                {
                    var (r, g, b, a) = Px.Unpremul(s.Pixels[y * s.Width + x]);
                    cur[o++] = r;
                    cur[o++] = g;
                    cur[o++] = b;
                    cur[o++] = a;
                }
                // Try every filter and keep the one with the smallest sum of absolute values (the usual heuristic).
                long bestScore = long.MaxValue;
                for (byte f = 0; f <= 4; f++)
                {
                    trial[0] = f;
                    long score = 0;
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= 4 ? cur[i - 4] : 0, up = prev[i], upLeft = i >= 4 ? prev[i - 4] : 0;
                        int predicted = f switch
                        {
                            1 => left,
                            2 => up,
                            3 => (left + up) >> 1,
                            4 => Paeth(left, up, upLeft),
                            _ => 0,
                        };
                        byte v = (byte)(cur[i] - predicted);
                        trial[i + 1] = v;
                        score += v < 128 ? v : 256 - v;
                    }
                    if (score < bestScore)
                    {
                        bestScore = score;
                        (best, trial) = (trial, best);
                    }
                }
                z.Write(best);
                (prev, cur) = (cur, prev);
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>A Windows .ico holding PNG images (supported since Windows Vista).</summary>
    public static void SaveIco(IReadOnlyList<Surface> images, string path)
    {
        var pngs = images.Select(Encode).ToArray();
        using var f = File.Create(path);
        var w = new BinaryWriter(f);
        w.Write((ushort)0);
        w.Write((ushort)1); // icon
        w.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        for (int i = 0; i < images.Count; i++)
        {
            int size = images[i].Width;
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);   // palette
            w.Write((byte)0);   // reserved
            w.Write((ushort)1); // planes
            w.Write((ushort)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }

    static readonly uint[] s_table = CreateTable();

    static uint[] CreateTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    static uint Crc(uint crc, byte[] data)
    {
        foreach (byte b in data) crc = s_table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
