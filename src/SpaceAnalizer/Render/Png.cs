using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SpaceAnalizer.Render;

/// <summary>Minimal PNG writer (RGBA, zlib through System.IO.Compression) for snapshots and the app icon.</summary>
public static class Png
{
    public static void Save(Surface s, string path)
    {
        using var file = File.Create(path);
        Write(s, file);
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

        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + s.Width * 4];
            for (int y = 0; y < s.Height; y++)
            {
                row[0] = 0; // no filter
                int o = 1;
                for (int x = 0; x < s.Width; x++)
                {
                    var (r, g, b, a) = Px.Unpremul(s.Pixels[y * s.Width + x]);
                    row[o++] = r;
                    row[o++] = g;
                    row[o++] = b;
                    row[o++] = a;
                }
                z.Write(row);
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
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
