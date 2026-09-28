// FontBaker: converts a TrueType font into SpaceAnalyzer's tiny embedded font format (.saf).
//
//   dotnet run tools/FontBaker.cs -- <input.ttf> <output.saf>
//
// Only the characters the app needs are kept (Latin + punctuation + a few symbols). Composite glyphs are
// flattened, GPOS pair kerning is extracted for those glyphs, and the result is Deflate-compressed.
//
// .saf layout before compression (u = unsigned LEB128 varint, s = zigzag varint):
//   "SAF1" u unitsPerEm, s ascender, descender, lineGap, xHeight, capHeight, u glyphCount
//   u cmapCount, cmapCount × (u codepointDelta, u glyph)
//   glyphCount × (u advance, u kernBaseGlyph, u contours, contours × u endPointDelta,
//                 onCurve bits (8 per byte), points × s xDelta, points × s yDelta)
//   u kernCount, kernCount × (u leftDelta, u rightDelta (reset per left), s value)

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: FontBaker <input.ttf> <output.saf>");
    return 1;
}

var ttf = new Ttf(File.ReadAllBytes(args[0]));

var codepoints = new SortedSet<int>();
void Range(int a, int b) { for (int c = a; c <= b; c++) codepoints.Add(c); }
Range(0x20, 0x7E);     // ASCII
Range(0xA0, 0xFF);     // Latin-1
Range(0x100, 0x17F);   // Latin Extended-A
Range(0x2010, 0x2027); // dashes, quotes, bullet, ellipsis
foreach (int c in new[] { 0x2030, 0x2039, 0x203A, 0x20AC, 0x2122, 0x2190, 0x2191, 0x2192, 0x2193, 0x2318, 0x2325, 0x21E7, 0x232B, 0x2303, 0x2212, 0x2248, 0x2260, 0x2264, 0x2265, 0x00D7 })
    codepoints.Add(c);

// Glyph 0 (.notdef) always first; then every glyph used by the selected characters.
var glyphMap = new Dictionary<int, int> { [0] = 0 };
var glyphOrder = new List<int> { 0 };
var cmap = new List<(int cp, int glyph)>();
foreach (int cp in codepoints)
{
    int g = ttf.GlyphIndex(cp);
    if (g == 0) continue;
    if (!glyphMap.TryGetValue(g, out int ng))
    {
        ng = glyphOrder.Count;
        glyphMap[g] = ng;
        glyphOrder.Add(g);
    }
    cmap.Add((cp, ng));
}

// Kerning is stored only between "base" glyphs; accented letters reuse the pairs of their base letter
// (á → a, Ž → Z...). That keeps the table small without visible loss.
var baseOf = new int[glyphOrder.Count];
for (int i = 0; i < baseOf.Length; i++) baseOf[i] = i;
foreach (var (cp, g) in cmap)
{
    if (cp < 0x80) continue;
    string decomposed = char.ConvertFromUtf32(cp).Normalize(NormalizationForm.FormD);
    int baseCp = decomposed[0];
    if (baseCp >= 0x80 || baseCp == cp) continue;
    int baseGlyph = cmap.FirstOrDefault(m => m.cp == baseCp).glyph;
    if (baseGlyph != 0) baseOf[g] = baseGlyph;
}
var baseGlyphs = glyphOrder.Where((g, i) => baseOf[i] == i).ToList();

var ms = new MemoryStream();
var w = new BinaryWriter(ms);
void VarU(long v) { ulong u = (ulong)v; while (u >= 0x80) { w.Write((byte)(u | 0x80)); u >>= 7; } w.Write((byte)u); }
void VarS(long v) => VarU((v << 1) ^ (v >> 63)); // zigzag

w.Write(Encoding.ASCII.GetBytes("SAF1"));
VarU(ttf.UnitsPerEm);
VarS(ttf.Ascender);
VarS(ttf.Descender);
VarS(ttf.LineGap);
VarS(ttf.XHeight);
VarS(ttf.CapHeight);
VarU(glyphOrder.Count);

VarU(cmap.Count);
int lastCp = 0;
foreach (var (cp, g) in cmap)
{
    VarU(cp - lastCp);
    VarU(g);
    lastCp = cp;
}

int totalPoints = 0;
for (int gi = 0; gi < glyphOrder.Count; gi++)
{
    int g = glyphOrder[gi];
    var outline = ttf.Outline(g);
    VarU(ttf.Advance(g));
    VarU(baseOf[gi]);
    VarU(outline.EndPoints.Count);
    int lastEnd = -1;
    foreach (int e in outline.EndPoints) { VarU(e - lastEnd); lastEnd = e; }
    int n = outline.Points.Count;
    for (int i = 0; i < n; i += 8)
    {
        byte bits = 0;
        for (int b = 0; b < 8 && i + b < n; b++) if (outline.Points[i + b].On) bits |= (byte)(1 << b);
        w.Write(bits);
    }
    int px = 0, py = 0;
    foreach (var p in outline.Points) { int x = (int)Math.Round(p.X); VarS(x - px); px = x; }
    foreach (var p in outline.Points) { int y = (int)Math.Round(p.Y); VarS(y - py); py = y; }
    totalPoints += n;
}

var kern = ttf.Kerning(baseGlyphs)
    .Select(k => (L: glyphMap[k.Key.Item1], R: glyphMap[k.Key.Item2], V: k.Value))
    .OrderBy(k => k.L).ThenBy(k => k.R).ToList();
VarU(kern.Count);
int lastL = 0, lastR = 0;
foreach (var (l, r, v) in kern)
{
    VarU(l - lastL);
    if (l != lastL) lastR = 0;
    VarU(r - lastR);
    VarS(v);
    lastL = l;
    lastR = r;
}
w.Flush();

using (var output = File.Create(args[1]))
using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize))
    deflate.Write(ms.ToArray());

Console.WriteLine($"{Path.GetFileName(args[0])}: {cmap.Count} chars, {glyphOrder.Count} glyphs, {totalPoints} points, {kern.Count} kerning pairs -> {new FileInfo(args[1]).Length:N0} bytes");
return 0;

/// <summary>Just enough of a TrueType reader for the baker.</summary>
sealed class Ttf
{
    readonly byte[] _d;
    readonly Dictionary<string, (int Offset, int Length)> _tables = new();
    readonly int _glyf, _loca, _hmtx, _numHMetrics, _numGlyphs, _locFormat;
    readonly int _cmapFormatOffset, _cmapFormat;

    public int UnitsPerEm, Ascender, Descender, LineGap, XHeight, CapHeight;

    public Ttf(byte[] data)
    {
        _d = data;
        int numTables = U16(4);
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(_d, rec, 4);
            _tables[tag] = ((int)U32(rec + 8), (int)U32(rec + 12));
        }
        int head = _tables["head"].Offset;
        UnitsPerEm = U16(head + 18);
        _locFormat = I16(head + 50);
        int hhea = _tables["hhea"].Offset;
        Ascender = I16(hhea + 4);
        Descender = I16(hhea + 6);
        LineGap = I16(hhea + 8);
        _numHMetrics = U16(hhea + 34);
        _numGlyphs = U16(_tables["maxp"].Offset + 4);
        _hmtx = _tables["hmtx"].Offset;
        _glyf = _tables["glyf"].Offset;
        _loca = _tables["loca"].Offset;
        if (_tables.TryGetValue("OS/2", out var os2) && U16(os2.Offset) >= 2)
        {
            XHeight = I16(os2.Offset + 86);
            CapHeight = I16(os2.Offset + 88);
        }

        int cmap = _tables["cmap"].Offset;
        int n = U16(cmap + 2);
        int best = -1, bestFormat = 0, bestScore = -1;
        for (int i = 0; i < n; i++)
        {
            int platform = U16(cmap + 4 + i * 8), encoding = U16(cmap + 6 + i * 8);
            int offset = cmap + (int)U32(cmap + 8 + i * 8);
            int format = U16(offset);
            int score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 3,
                (0, _, 12) => 2,
                (3, 1, 4) => 1,
                (0, _, 4) => 0,
                _ => -1,
            };
            if (score > bestScore) { bestScore = score; best = offset; bestFormat = format; }
        }
        if (best < 0) throw new InvalidDataException("No usable cmap subtable");
        _cmapFormatOffset = best;
        _cmapFormat = bestFormat;
    }

    int U16(int o) => BinaryPrimitives.ReadUInt16BigEndian(_d.AsSpan(o));
    int I16(int o) => BinaryPrimitives.ReadInt16BigEndian(_d.AsSpan(o));
    uint U32(int o) => BinaryPrimitives.ReadUInt32BigEndian(_d.AsSpan(o));

    public int GlyphIndex(int cp)
    {
        int t = _cmapFormatOffset;
        if (_cmapFormat == 12)
        {
            uint groups = U32(t + 12);
            for (int i = 0; i < groups; i++)
            {
                int g = t + 16 + i * 12;
                uint start = U32(g), end = U32(g + 4);
                if (cp >= start && cp <= end) return (int)(U32(g + 8) + (cp - start));
            }
            return 0;
        }
        if (cp > 0xFFFF) return 0;
        int segX2 = U16(t + 6), seg = segX2 / 2;
        int ends = t + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
        for (int i = 0; i < seg; i++)
        {
            int end = U16(ends + i * 2);
            if (cp > end) continue;
            int start = U16(starts + i * 2);
            if (cp < start) return 0;
            int delta = I16(deltas + i * 2), rangeOffset = U16(ranges + i * 2);
            if (rangeOffset == 0) return (cp + delta) & 0xFFFF;
            int glyphAddr = ranges + i * 2 + rangeOffset + (cp - start) * 2;
            int g = U16(glyphAddr);
            return g == 0 ? 0 : (g + delta) & 0xFFFF;
        }
        return 0;
    }

    public int Advance(int g) => U16(_hmtx + Math.Min(g, _numHMetrics - 1) * 4);

    (int Start, int End) GlyphRange(int g)
    {
        if (_locFormat == 0) return (_glyf + U16(_loca + g * 2) * 2, _glyf + U16(_loca + g * 2 + 2) * 2);
        return (_glyf + (int)U32(_loca + g * 4), _glyf + (int)U32(_loca + g * 4 + 4));
    }

    public sealed record Pt(double X, double Y, bool On);

    public sealed class GlyphOutline
    {
        public List<Pt> Points = [];
        public List<int> EndPoints = [];
        public int XMin, YMin, XMax, YMax;
    }

    public GlyphOutline Outline(int g)
    {
        var o = new GlyphOutline();
        Append(g, o, 1, 0, 0, 1, 0, 0, 0);
        if (o.Points.Count > 0)
        {
            o.XMin = (int)Math.Floor(o.Points.Min(p => p.X));
            o.XMax = (int)Math.Ceiling(o.Points.Max(p => p.X));
            o.YMin = (int)Math.Floor(o.Points.Min(p => p.Y));
            o.YMax = (int)Math.Ceiling(o.Points.Max(p => p.Y));
        }
        return o;
    }

    void Append(int g, GlyphOutline o, double a, double b, double c, double d, double dx, double dy, int depth)
    {
        if (depth > 8) return;
        var (start, end) = GlyphRange(g);
        if (end <= start) return;
        int contours = I16(start);
        if (contours >= 0)
        {
            int p = start + 10;
            var ends = new int[contours];
            for (int i = 0; i < contours; i++) ends[i] = U16(p + i * 2);
            p += contours * 2;
            int count = contours == 0 ? 0 : ends[^1] + 1;
            p += 2 + U16(p); // skip instructions
            var flags = new byte[count];
            for (int i = 0; i < count;)
            {
                byte f = _d[p++];
                flags[i++] = f;
                if ((f & 8) != 0)
                {
                    int repeat = _d[p++];
                    while (repeat-- > 0 && i < count) flags[i++] = f;
                }
            }
            var xs = new int[count];
            int x = 0;
            for (int i = 0; i < count; i++)
            {
                byte f = flags[i];
                if ((f & 2) != 0) { int v = _d[p++]; x += (f & 16) != 0 ? v : -v; }
                else if ((f & 16) == 0) { x += I16(p); p += 2; }
                xs[i] = x;
            }
            var ys = new int[count];
            int y = 0;
            for (int i = 0; i < count; i++)
            {
                byte f = flags[i];
                if ((f & 4) != 0) { int v = _d[p++]; y += (f & 32) != 0 ? v : -v; }
                else if ((f & 32) == 0) { y += I16(p); p += 2; }
                ys[i] = y;
            }
            int baseIndex = o.Points.Count;
            for (int i = 0; i < count; i++)
                o.Points.Add(new Pt(a * xs[i] + c * ys[i] + dx, b * xs[i] + d * ys[i] + dy, (flags[i] & 1) != 0));
            foreach (int e in ends) o.EndPoints.Add(baseIndex + e);
            return;
        }

        // Composite glyph
        int q = start + 10;
        while (true)
        {
            int flags = U16(q), component = U16(q + 2);
            q += 4;
            double ox, oy;
            if ((flags & 1) != 0) { ox = I16(q); oy = I16(q + 2); q += 4; }
            else { ox = (sbyte)_d[q]; oy = (sbyte)_d[q + 1]; q += 2; }
            double ca = 1, cb = 0, cc = 0, cd = 1;
            if ((flags & 8) != 0) { ca = cd = F2Dot14(q); q += 2; }
            else if ((flags & 0x40) != 0) { ca = F2Dot14(q); cd = F2Dot14(q + 2); q += 4; }
            else if ((flags & 0x80) != 0) { ca = F2Dot14(q); cb = F2Dot14(q + 2); cc = F2Dot14(q + 4); cd = F2Dot14(q + 6); q += 8; }
            if ((flags & 2) == 0) { ox = 0; oy = 0; } // point matching: not supported (rare)
            // Combine: parent ∘ component
            double na = a * ca + c * cb, nb = b * ca + d * cb, nc = a * cc + c * cd, nd = b * cc + d * cd;
            double ndx = a * ox + c * oy + dx, ndy = b * ox + d * oy + dy;
            Append(component, o, na, nb, nc, nd, ndx, ndy, depth + 1);
            if ((flags & 0x20) == 0) break;
        }
    }

    double F2Dot14(int o) => I16(o) / 16384.0;

    // ------------------------------------------------------------------ GPOS pair kerning

    public Dictionary<(int, int), int> Kerning(List<int> glyphs)
    {
        var result = new Dictionary<(int, int), int>();
        if (!_tables.TryGetValue("GPOS", out var gpos)) return result;
        int g0 = gpos.Offset;
        int featureList = g0 + U16(g0 + 6), lookupList = g0 + U16(g0 + 8);
        var lookups = new SortedSet<int>();
        int featureCount = U16(featureList);
        for (int i = 0; i < featureCount; i++)
        {
            int rec = featureList + 2 + i * 6;
            if (Encoding.ASCII.GetString(_d, rec, 4) != "kern") continue;
            int feature = featureList + U16(rec + 4);
            int n = U16(feature + 2);
            for (int k = 0; k < n; k++) lookups.Add(U16(feature + 4 + k * 2));
        }
        var set = new HashSet<int>(glyphs);
        foreach (int li in lookups)
        {
            int lookup = lookupList + U16(lookupList + 2 + li * 2);
            int type = U16(lookup), subCount = U16(lookup + 4);
            for (int s = 0; s < subCount; s++)
            {
                int sub = lookup + U16(lookup + 6 + s * 2);
                int subType = type;
                if (type == 9) { subType = U16(sub + 2); sub += (int)U32(sub + 4); }
                if (subType != 2) continue;
                ReadPairPos(sub, glyphs, set, result);
            }
        }
        return result;
    }

    void ReadPairPos(int sub, List<int> glyphs, HashSet<int> set, Dictionary<(int, int), int> result)
    {
        int format = U16(sub);
        int coverage = sub + U16(sub + 2);
        int vf1 = U16(sub + 4), vf2 = U16(sub + 6);
        int size1 = ValueSize(vf1), size2 = ValueSize(vf2);
        foreach (int left in glyphs)
        {
            int ci = CoverageIndex(coverage, left);
            if (ci < 0) continue;
            if (format == 1)
            {
                int pairSet = sub + U16(sub + 10 + ci * 2);
                int n = U16(pairSet);
                int recSize = 2 + size1 + size2;
                for (int i = 0; i < n; i++)
                {
                    int rec = pairSet + 2 + i * recSize;
                    int right = U16(rec);
                    if (!set.Contains(right)) continue;
                    int v = XAdvance(rec + 2, vf1);
                    if (v != 0) result.TryAdd((left, right), v);
                }
            }
            else if (format == 2)
            {
                int classDef1 = sub + U16(sub + 8), classDef2 = sub + U16(sub + 10);
                int class1Count = U16(sub + 12), class2Count = U16(sub + 14);
                int c1 = ClassOf(classDef1, left);
                if (c1 >= class1Count) continue;
                int rowSize = class2Count * (size1 + size2);
                foreach (int right in glyphs)
                {
                    int c2 = ClassOf(classDef2, right);
                    if (c2 >= class2Count) continue;
                    int rec = sub + 16 + c1 * rowSize + c2 * (size1 + size2);
                    int v = XAdvance(rec, vf1);
                    if (v != 0) result.TryAdd((left, right), v);
                }
            }
        }
    }

    static int ValueSize(int format)
    {
        int n = 0;
        for (int b = 0; b < 8; b++) if ((format & (1 << b)) != 0) n++;
        return n * 2;
    }

    int XAdvance(int record, int format)
    {
        if ((format & 4) == 0) return 0;
        int offset = 0;
        if ((format & 1) != 0) offset += 2;
        if ((format & 2) != 0) offset += 2;
        return I16(record + offset);
    }

    int CoverageIndex(int coverage, int glyph)
    {
        int format = U16(coverage);
        if (format == 1)
        {
            int n = U16(coverage + 2);
            for (int i = 0; i < n; i++) if (U16(coverage + 4 + i * 2) == glyph) return i;
            return -1;
        }
        int ranges = U16(coverage + 2);
        for (int i = 0; i < ranges; i++)
        {
            int r = coverage + 4 + i * 6;
            int start = U16(r), end = U16(r + 2);
            if (glyph >= start && glyph <= end) return U16(r + 4) + glyph - start;
        }
        return -1;
    }

    int ClassOf(int classDef, int glyph)
    {
        int format = U16(classDef);
        if (format == 1)
        {
            int start = U16(classDef + 2), count = U16(classDef + 4);
            return glyph >= start && glyph < start + count ? U16(classDef + 6 + (glyph - start) * 2) : 0;
        }
        int n = U16(classDef + 2);
        for (int i = 0; i < n; i++)
        {
            int r = classDef + 4 + i * 6;
            if (glyph >= U16(r) && glyph <= U16(r + 2)) return U16(r + 4);
        }
        return 0;
    }
}
