using System.Text;

namespace SmokeLounge.AOtomation.Messaging;

/// <summary>
///     Formats AO's '~&' ext messages into readable text, in managed code.
///     The wire form is '~&' + category and message id (5 base-85 chars each) + typed params + '~'.
///     The template for (category, id) comes from <see cref="MmdbData"/>; the params fill its
///     printf-style placeholders (%s, %d, %u, %i, %02d, %02u). This replaces the old path through
///     ldb.dll's RemoteFormat::ParseString, which only worked inside the game process.
/// </summary>
public static class ExtMessageFormatter
{
    // Param type codes. 'l' is a plain instance id into category 20000; 'f' is a float's bit
    // pattern in base-85 (the mission clear-% line carries one); 'R' is another mmdb reference.
    private const int CategoryForL = 20000;

    public static string Format(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length < 12 || raw[0] != '~' || raw[1] != '&')
            return raw;

        long category = Base85(raw, 2), instance = Base85(raw, 7);
        if (category < 0 || instance < 0 || !MmdbData.TryGet((int)category, (int)instance, out string template))
            return "$" + category + ":" + instance + "$";

        List<object> args = ParseParams(raw, 12);
        return Substitute(template, args);
    }

    private static List<object> ParseParams(string s, int p)
    {
        List<object> args = new List<object>();
        while (p < s.Length)
        {
            char type = s[p++];
            switch (type)
            {
                case '~': // terminator
                    return args;
                case 'S': // 2-byte big-endian size + that many chars
                {
                    if (p + 2 > s.Length) return args;
                    int size = (s[p] << 8) | s[p + 1];
                    if (p + 2 + size > s.Length) return args;
                    args.Add(s.Substring(p + 2, size));
                    p += 2 + size;
                    break;
                }
                case 's': // 1-byte size, one less than the char count
                {
                    if (p >= s.Length) return args;
                    int size = s[p] - 1;
                    if (p + 1 + size > s.Length) return args;
                    args.Add(s.Substring(p + 1, size));
                    p += 1 + size;
                    break;
                }
                case 'I': // 4-byte big-endian int
                {
                    if (p + 4 > s.Length) return args;
                    int v = 0;
                    for (int i = 0; i < 4; i++) v = (v << 8) | s[p + i];
                    args.Add((long)(uint)v);
                    p += 4;
                    break;
                }
                case 'i': // base-85 int (5 chars)
                case 'u':
                {
                    long v = Base85(s, p);
                    if (v < 0) return args;
                    args.Add(v);
                    p += 5;
                    break;
                }
                case 'f': // base-85 float bit pattern (5 chars)
                {
                    long v = Base85(s, p);
                    if (v < 0) return args;
                    args.Add(BitConverter.ToSingle(BitConverter.GetBytes((int)(uint)v), 0));
                    p += 5;
                    break;
                }
                case 'R': // another mmdb reference: category + instance, 5 base-85 chars each
                {
                    long cat2 = Base85(s, p), inst2 = Base85(s, p + 5);
                    if (cat2 < 0 || inst2 < 0) return args;
                    p += 10;
                    args.Add(MmdbData.TryGet((int)cat2, (int)inst2, out string t) ? t : "$" + cat2 + ":" + inst2 + "$");
                    break;
                }
                case 'l': // plain instance id into category 20000
                {
                    if (p + 4 > s.Length) return args;
                    int inst2 = 0;
                    for (int i = 0; i < 4; i++) inst2 = (inst2 << 8) | s[p + i];
                    p += 4;
                    args.Add(MmdbData.TryGet(CategoryForL, inst2, out string t) ? t : "$" + CategoryForL + ":" + inst2 + "$");
                    break;
                }
                default: // unknown type: give up on the params, the template alone is still readable
                    return args;
            }
        }

        return args;
    }

    /// <summary>Printf-style substitution; on any placeholder/param mismatch the template is returned as-is
    /// (the server sends params for templates without placeholders now and then).</summary>
    private static string Substitute(string template, List<object> args)
    {
        StringBuilder sb = new StringBuilder(template.Length + 32);
        int next = 0;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c != '%')
            {
                sb.Append(c);
                continue;
            }

            int p = i + 1;
            bool zeroPad = false;
            if (p < template.Length && template[p] == '0') { zeroPad = true; p++; }
            int width = 0;
            while (p < template.Length && template[p] >= '0' && template[p] <= '9')
                width = width * 10 + (template[p++] - '0');
            int? precision = null;
            if (p < template.Length && template[p] == '.')
            {
                p++;
                precision = 0;
                while (p < template.Length && template[p] >= '0' && template[p] <= '9')
                    precision = precision * 10 + (template[p++] - '0');
            }

            if (p >= template.Length)
            {
                sb.Append(c);
                break;
            }

            char conv = template[p];
            if (conv == '%')
            {
                sb.Append('%');
                i = p;
                continue;
            }

            if (conv != 's' && conv != 'd' && conv != 'i' && conv != 'u' && conv != 'f')
            {
                sb.Append('%'); // literal percent, e.g. "50%Strength": kept, no param consumed
                continue;
            }

            if (next >= args.Count)
                return template; // no param for this placeholder: leave the template whole

            string text = conv switch
            {
                's' => ParamAsString(args[next++]),
                'd' or 'i' or 'u' => Pad(Number(args[next++]), width, zeroPad),
                'f' => Float(args[next++], precision),
                _ => null,
            };
            if (text == null)
                return template; // no param for this placeholder: leave the template whole
            sb.Append(text);
            i = p;
        }

        return sb.ToString();
    }

    private static string ParamAsString(object arg) => arg as string ?? Convert.ToString(arg);

    private static long Number(object arg) => arg is string ? 0 : Convert.ToInt64(arg);

    private static string Float(object arg, int? precision)
    {
        float value = arg is float f ? f : (float)Number(arg);
        return precision.HasValue ? value.ToString("F" + precision.Value) : value.ToString();
    }

    private static string Pad(long value, int width, bool zeroPad) =>
        zeroPad && width > 0 ? value.ToString().PadLeft(width, '0') : value.ToString();

    private static long Base85(string s, int p)
    {
        long v = 0;
        for (int i = 0; i < 5; i++)
        {
            if (p + i >= s.Length) return -1;
            int c = s[p + i] - 33;
            if (c < 0 || c > 84) return -1;
            v = v * 85 + c;
        }

        return v;
    }
}
