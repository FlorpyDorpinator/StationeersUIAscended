using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Assets.Scripts.Objects;
using StationeersUIMod.Core;

namespace StationeersUIMod.Features
{
    /// <summary>
    /// The <c>UIAP1</c> share codec (SmartStow redesign plan §9, FlorpyDorp Q7): one Stow Profile
    /// &lt;-&gt; one pasteable ASCII string, plus an 8-character Crockford fingerprint for saying
    /// "did you get the right code?" out loud.
    ///
    /// <code>
    /// UIAP1-F-&lt;base64url( [flags u8] [ payload | deflate(payload) ] )&gt;
    ///
    /// payload := u8   schema (1)
    ///            str  setName
    ///            str  description
    ///            var  profileCount
    ///              per profile: str name, str badge ("" = derive),
    ///                           var n, n x (str prefab,     svar priority)     <- Item rules
    ///                           var n, n x (str className,  svar priority)     <- UIA class rules
    ///                           var n, n x (str slotClass,  svar priority)     <- Slot.Class rules
    ///                           var n, n x (str category,   svar priority)     <- SortingClass rules
    ///            u16  checksum (FNV-1a 32 folded to 16, over everything above)
    ///
    ///   var  = unsigned LEB128     svar = zigzag LEB128     str = var length + ASCII bytes
    /// </code>
    ///
    /// <para><b>Q7: the code GENERATES the XML.</b> Import decodes to a <see cref="StowProfileDoc"/>
    /// and hands it to <see cref="StowProfileStore.ImportDoc"/>, which writes a real
    /// <c>StowProfiles/&lt;name&gt;.xml</c>. There is no second, code-shaped storage format; the
    /// pasted string and a dropped-in XML file converge on the same file on disk, and a dropped-in
    /// file needs no import step at all (the folder is enumerated on every listing).</para>
    ///
    /// <para><b>Three deliberate departures from plan §9.2</b>, each recorded in the B4 report:
    /// <list type="number">
    /// <item><b>Enums travel as NAMES, not ordinals.</b> The plan proposed one byte per enum value.
    /// But <see cref="UIAClassRule"/> stores its class as a NAME on purpose ("so the taxonomy can be
    /// reordered or extended without silently re-pointing every player's rules") and a wire format
    /// that re-introduced the ordinal would hand that footgun straight back. Names also fail soft the
    /// same way unknown prefabs do — an unknown NAME can be kept and flagged, an unknown ORDINAL can
    /// only be dropped — and after Deflate the shared prefixes cost almost nothing.</item>
    /// <item><b>A leading flags byte</b> selects deflated or raw. <see cref="DeflateStream"/> is a BCL
    /// type the game's Mono provides, but if it ever throws on some install the codec degrades to a
    /// longer raw code instead of losing the feature; the decoder handles both.</item>
    /// <item><b>No planId / rev / bag-mapping fields.</b> Reference and delta codes (kinds R and D)
    /// are not built, and the bag mapping was retired with Loadouts (FlorpyDorp Q4 — Bag Profiles are
    /// always mapped to containers by hand). The <c>schema</c> byte is the forward-compat hinge: a
    /// decoder refuses a schema it does not know rather than mis-reading it.</item>
    /// </list></para>
    ///
    /// <para>Fail-soft and data-only by construction: no file paths, no executable content, no
    /// assignments keyed to someone else's save. Every length is bounds-checked before it is used,
    /// decompression is capped, and the worst a malicious code can do is create one Stow Profile file
    /// full of junk rules that the player deletes. Holds no statics, so there is nothing to reset on
    /// hot reload.</para>
    /// </summary>
    public static class StowShareCodec
    {
        public const string Prefix = "UIAP1-F-";
        public const int PayloadSchema = 1;

        /// <summary>Hard ceiling on a DECODED payload. "By Printer" — the biggest thing we ship, 559
        /// item rules — is about 12 KB, so 64 KB is ~5x headroom and still small enough that a
        /// decompression bomb cannot hurt.</summary>
        public const int MaxPayloadBytes = 64 * 1024;
        /// <summary>Ceiling on the pasted text itself, before any decoding.</summary>
        public const int MaxCodeChars = 512 * 1024;

        /// <summary>Per-string wire cap. Generous on purpose: profile names are already capped at 40
        /// by <see cref="ProfileCapture.SanitizeName"/> and the longest prefab name in the game is
        /// under 40, so the only string that could ever approach this is a hand-written
        /// <c>&lt;Description&gt;</c> — and a code that silently truncated one would fail its own
        /// round-trip test for a reason nobody could see.</summary>
        private const int MaxStringChars = 512;
        private const string Crockford = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";  // no I L O U

        /// <summary>Where <c>stowprofiles export</c> and the F10 Export button drop a copy of the
        /// code, because a 5000-character clipboard is not something every setup survives.</summary>
        public static string ExportDir { get { return Path.Combine(StowProfileStore.Dir, "Export"); } }

        // ================= encode =================

        /// <summary>Encode one Stow Profile. Returns null (never throws) on failure.</summary>
        public static string Encode(StowProfileDoc doc, out string fingerprint)
        {
            fingerprint = null;
            if (doc == null) return null;
            try
            {
                byte[] payload = BuildPayload(doc);
                if (payload.Length > MaxPayloadBytes)
                {
                    UIALog.Warn("Stow Profile '" + doc.Name + "' is too large to share as a code ("
                        + payload.Length + " bytes).");
                    return null;
                }
                fingerprint = FingerprintOf(payload);

                byte[] deflated = TryDeflate(payload);
                byte[] container;
                if (deflated != null && deflated.Length < payload.Length)
                {
                    container = new byte[deflated.Length + 1];
                    container[0] = 1;
                    Buffer.BlockCopy(deflated, 0, container, 1, deflated.Length);
                }
                else
                {
                    container = new byte[payload.Length + 1];
                    container[0] = 0;
                    Buffer.BlockCopy(payload, 0, container, 1, payload.Length);
                }
                return Prefix + ToBase64Url(container);
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile encode failed: " + e.Message);
                return null;
            }
        }

        private static byte[] BuildPayload(StowProfileDoc doc)
        {
            using (var ms = new MemoryStream(1024))
            {
                ms.WriteByte(PayloadSchema);
                WriteString(ms, doc.Name);
                WriteString(ms, doc.Description);

                var profiles = doc.Profiles ?? new List<BagProfile>();
                int count = 0;
                for (int i = 0; i < profiles.Count; i++)
                    if (profiles[i] != null && !string.IsNullOrEmpty(profiles[i].Name)) count++;
                WriteVar(ms, (uint)count);

                for (int i = 0; i < profiles.Count; i++)
                {
                    BagProfile p = profiles[i];
                    if (p == null || string.IsNullOrEmpty(p.Name)) continue;
                    WriteString(ms, p.Name);
                    WriteString(ms, p.Badge);

                    WriteVar(ms, (uint)Count(p.Items));
                    for (int r = 0; r < p.Items.Count; r++)
                    {
                        ItemRule rule = p.Items[r];
                        if (rule == null) continue;
                        WriteString(ms, rule.Prefab);
                        WriteSVar(ms, rule.Priority);
                    }
                    WriteVar(ms, (uint)Count(p.UIAClasses));
                    for (int r = 0; r < p.UIAClasses.Count; r++)
                    {
                        UIAClassRule rule = p.UIAClasses[r];
                        if (rule == null) continue;
                        WriteString(ms, rule.Name);
                        WriteSVar(ms, rule.Priority);
                    }
                    WriteVar(ms, (uint)Count(p.SlotClasses));
                    for (int r = 0; r < p.SlotClasses.Count; r++)
                    {
                        SlotClassRule rule = p.SlotClasses[r];
                        if (rule == null) continue;
                        WriteString(ms, rule.Name);
                        WriteSVar(ms, rule.Priority);
                    }
                    WriteVar(ms, (uint)Count(p.Categories));
                    for (int r = 0; r < p.Categories.Count; r++)
                    {
                        CategoryRule rule = p.Categories[r];
                        if (rule == null) continue;
                        WriteString(ms, rule.Name);
                        WriteSVar(ms, rule.Priority);
                    }
                }

                byte[] body = ms.ToArray();
                ushort sum = Checksum16(body, body.Length);
                var full = new byte[body.Length + 2];
                Buffer.BlockCopy(body, 0, full, 0, body.Length);
                full[body.Length] = (byte)(sum & 0xFF);
                full[body.Length + 1] = (byte)((sum >> 8) & 0xFF);
                return full;
            }
        }

        private static int Count<T>(List<T> list) where T : class
        {
            if (list == null) return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) n++;
            return n;
        }

        // ================= decode =================

        /// <summary>Decode a pasted code into a document. Returns null with <paramref name="error"/>
        /// set on any failure. <paramref name="unknownRules"/> counts rules whose enum name this build
        /// does not know plus prefab names this game version does not have — those rules are KEPT
        /// (they may be a mod's, or a newer game's) and merely reported, per the fail-soft posture
        /// <c>HudDocument.Sanitize</c> established.</summary>
        public static StowProfileDoc Decode(string code, out string fingerprint, out string error,
            out int unknownRules)
        {
            fingerprint = null;
            error = null;
            unknownRules = 0;
            try
            {
                if (string.IsNullOrEmpty(code)) { error = "Paste a code first."; return null; }
                if (code.Length > MaxCodeChars) { error = "That text is far too long to be a share code."; return null; }

                string body = StripWhitespace(code);
                if (body.Length <= Prefix.Length
                    || !body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // Name the two other kinds explicitly: a player who was handed one should be told
                    // it is not supported, not told their code is corrupt.
                    if (body.StartsWith("UIAP1-R-", StringComparison.OrdinalIgnoreCase)
                        || body.StartsWith("UIAP1-D-", StringComparison.OrdinalIgnoreCase))
                        error = "That is a short/reference code - this build only reads full codes (UIAP1-F-...).";
                    else
                        error = "That does not look like a Stow Profile code (it should start with UIAP1-F-).";
                    return null;
                }

                byte[] container = FromBase64Url(body.Substring(Prefix.Length));
                if (container == null || container.Length < 2)
                {
                    error = "That code is damaged or incomplete - copy the whole thing and try again.";
                    return null;
                }

                byte[] payload;
                if (container[0] == 1)
                {
                    payload = TryInflate(container, 1, container.Length - 1);
                    if (payload == null) { error = "That code is damaged (it will not decompress)."; return null; }
                }
                else if (container[0] == 0)
                {
                    payload = new byte[container.Length - 1];
                    Buffer.BlockCopy(container, 1, payload, 0, payload.Length);
                }
                else
                {
                    error = "That code was made by a newer version of UI Ascended.";
                    return null;
                }

                if (payload.Length < 4 || payload.Length > MaxPayloadBytes)
                {
                    error = "That code is damaged or incomplete.";
                    return null;
                }
                ushort want = (ushort)(payload[payload.Length - 2] | (payload[payload.Length - 1] << 8));
                if (Checksum16(payload, payload.Length - 2) != want)
                {
                    // Truncation is by far the most common cause (a chat client ate the tail), but a
                    // single mangled character lands here too, so the message must cover both.
                    error = "That code is damaged or incomplete - copy the whole thing again and re-paste it.";
                    return null;
                }
                fingerprint = FingerprintOf(payload);

                int at = 0;
                int end = payload.Length - 2;
                byte schema = payload[at++];
                if (schema > PayloadSchema)
                {
                    error = "That code was made by a newer version of UI Ascended (format " + schema + ").";
                    return null;
                }

                var doc = new StowProfileDoc
                {
                    Name = ReadString(payload, ref at, end),
                    Description = ReadString(payload, ref at, end),
                    Schema = StowProfileStore.CurrentSchema,
                    Profiles = new List<BagProfile>(),
                };
                uint profileCount = ReadVar(payload, ref at, end);
                if (profileCount > 4096) { error = "That code is damaged (impossible profile count)."; return null; }

                HashSet<string> knownPrefabs = KnownPrefabs();
                for (uint i = 0; i < profileCount; i++)
                {
                    var p = new BagProfile
                    {
                        Name = ReadString(payload, ref at, end),
                        Badge = null,
                    };
                    string badge = ReadString(payload, ref at, end);
                    if (!string.IsNullOrEmpty(badge)) p.Badge = badge;

                    uint n = ReadVar(payload, ref at, end);
                    Bound(n, end - at);
                    for (uint r = 0; r < n; r++)
                    {
                        string prefab = ReadString(payload, ref at, end);
                        int priority = ReadSVar(payload, ref at, end);
                        p.Items.Add(new ItemRule { Prefab = prefab, Priority = priority });
                        if (knownPrefabs != null && !string.IsNullOrEmpty(prefab) && !knownPrefabs.Contains(prefab))
                            unknownRules++;
                    }
                    n = ReadVar(payload, ref at, end);
                    Bound(n, end - at);
                    for (uint r = 0; r < n; r++)
                    {
                        string name = ReadString(payload, ref at, end);
                        int priority = ReadSVar(payload, ref at, end);
                        var rule = new UIAClassRule { Name = name, Priority = priority };
                        p.UIAClasses.Add(rule);
                        UIAClass parsedClass;
                        if (!Enum.TryParse(name, true, out parsedClass)) unknownRules++;
                    }
                    n = ReadVar(payload, ref at, end);
                    Bound(n, end - at);
                    for (uint r = 0; r < n; r++)
                    {
                        string name = ReadString(payload, ref at, end);
                        int priority = ReadSVar(payload, ref at, end);
                        p.SlotClasses.Add(new SlotClassRule { Name = name, Priority = priority });
                        Slot.Class parsedSlot;
                        if (!Enum.TryParse(name, true, out parsedSlot)) unknownRules++;
                    }
                    n = ReadVar(payload, ref at, end);
                    Bound(n, end - at);
                    for (uint r = 0; r < n; r++)
                    {
                        string name = ReadString(payload, ref at, end);
                        int priority = ReadSVar(payload, ref at, end);
                        p.Categories.Add(new CategoryRule { Name = name, Priority = priority });
                        SortingClass parsedCat;
                        if (!Enum.TryParse(name, true, out parsedCat)) unknownRules++;
                    }

                    if (!string.IsNullOrEmpty(p.Name)) doc.Profiles.Add(p);
                }
                return doc;
            }
            catch (CodecException e)
            {
                error = e.Message;
                return null;
            }
            catch (Exception e)
            {
                UIALog.Warn("Stow Profile decode failed: " + e.Message);
                error = "That code could not be read (see the log).";
                return null;
            }
        }

        /// <summary>Every prefab name this game build knows, or null when no world has been loaded
        /// (F10 is reachable from the main menu, where "unknown prefab" would be a lie).</summary>
        private static HashSet<string> KnownPrefabs()
        {
            try
            {
                var list = DynamicThing.DynamicThingPrefabs;
                if (list == null || list.Count == 0) return null;
                var set = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (t == null) continue;
                    string n = null;
                    try { n = t.PrefabName; } catch { }
                    if (!string.IsNullOrEmpty(n)) set.Add(n);
                }
                return set.Count > 0 ? set : null;
            }
            catch { return null; }
        }

        // ================= round-trip self-test (the `stowprofiles roundtrip` command) =================

        /// <summary>Structural comparison used by the round-trip self-test. Returns true when the two
        /// documents carry the same content; otherwise <paramref name="difference"/> names the FIRST
        /// field that differs (a bare "they differ" would be useless in a bug report).
        ///
        /// <para><c>Schema</c> and <c>SourceFile</c> are deliberately NOT compared: neither travels in
        /// the code (schema is re-stamped on import, the file path is local).</para></summary>
        public static bool Equivalent(StowProfileDoc a, StowProfileDoc b, out string difference)
        {
            difference = null;
            if (a == null || b == null) { difference = "one document is null"; return false; }
            if (!Same(a.Name, b.Name)) { difference = "set name: '" + a.Name + "' vs '" + b.Name + "'"; return false; }
            if (!Same(a.Description, b.Description)) { difference = "description differs"; return false; }
            int an = a.Profiles != null ? a.Profiles.Count : 0;
            int bn = b.Profiles != null ? b.Profiles.Count : 0;
            if (an != bn) { difference = "profile count: " + an + " vs " + bn; return false; }
            for (int i = 0; i < an; i++)
            {
                BagProfile x = a.Profiles[i], y = b.Profiles[i];
                if (x == null || y == null) { difference = "profile " + i + " is null"; return false; }
                if (!Same(x.Name, y.Name)) { difference = "profile " + i + " name: '" + x.Name + "' vs '" + y.Name + "'"; return false; }
                if (!Same(x.Badge, y.Badge)) { difference = "profile '" + x.Name + "' badge: '" + x.Badge + "' vs '" + y.Badge + "'"; return false; }
                if (x.Items.Count != y.Items.Count) { difference = "profile '" + x.Name + "' item count"; return false; }
                for (int r = 0; r < x.Items.Count; r++)
                    if (!Same(x.Items[r].Prefab, y.Items[r].Prefab) || x.Items[r].Priority != y.Items[r].Priority)
                    { difference = "profile '" + x.Name + "' item rule " + r; return false; }
                if (x.UIAClasses.Count != y.UIAClasses.Count) { difference = "profile '" + x.Name + "' uia count"; return false; }
                for (int r = 0; r < x.UIAClasses.Count; r++)
                    if (!Same(x.UIAClasses[r].Name, y.UIAClasses[r].Name) || x.UIAClasses[r].Priority != y.UIAClasses[r].Priority)
                    { difference = "profile '" + x.Name + "' uia rule " + r; return false; }
                if (x.SlotClasses.Count != y.SlotClasses.Count) { difference = "profile '" + x.Name + "' slot count"; return false; }
                for (int r = 0; r < x.SlotClasses.Count; r++)
                    if (!Same(x.SlotClasses[r].Name, y.SlotClasses[r].Name) || x.SlotClasses[r].Priority != y.SlotClasses[r].Priority)
                    { difference = "profile '" + x.Name + "' slot rule " + r; return false; }
                if (x.Categories.Count != y.Categories.Count) { difference = "profile '" + x.Name + "' category count"; return false; }
                for (int r = 0; r < x.Categories.Count; r++)
                    if (!Same(x.Categories[r].Name, y.Categories[r].Name) || x.Categories[r].Priority != y.Categories[r].Priority)
                    { difference = "profile '" + x.Name + "' category rule " + r; return false; }
            }
            return true;
        }

        /// <summary>Empty and null are the SAME thing on the wire (a null string is written as
        /// length 0 and read back as ""), so the comparison must treat them that way too.</summary>
        private static bool Same(string a, string b)
        {
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        }

        // ================= fingerprint =================

        /// <summary>8 Crockford-Base32 characters (I/L/O/U excluded, so there is no 1/l or 0/O
        /// confusion when it is read aloud) taken from the top 40 bits of an FNV-1a-64 over the whole
        /// payload. Purely for human verification — it is not decodable and carries no data.</summary>
        public static string FingerprintOf(byte[] payload)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < payload.Length; i++)
            {
                h ^= payload[i];
                h *= 1099511628211UL;
            }
            ulong v = h >> 24;   // top 40 bits
            var chars = new char[9];
            for (int i = 7; i >= 0; i--)
            {
                chars[i >= 4 ? i + 1 : i] = Crockford[(int)(v & 31UL)];
                v >>= 5;
            }
            chars[4] = '-';
            return new string(chars);
        }

        // ================= primitives =================

        private sealed class CodecException : Exception
        {
            public CodecException(string message) : base(message) { }
        }

        private static void Bound(uint count, int bytesLeft)
        {
            // Every rule costs at least 2 bytes (a zero-length string + a priority varint), so a
            // count that could not possibly fit is a damaged code, not a 4-billion-rule profile.
            if (count > (uint)Math.Max(0, bytesLeft)) throw new CodecException("That code is damaged (rule count).");
        }

        private static void WriteVar(MemoryStream ms, uint v)
        {
            while (v >= 0x80) { ms.WriteByte((byte)(v | 0x80)); v >>= 7; }
            ms.WriteByte((byte)v);
        }

        private static void WriteSVar(MemoryStream ms, int v)
        {
            WriteVar(ms, (uint)((v << 1) ^ (v >> 31)));   // zigzag: hand-tuned negative priorities survive
        }

        private static uint ReadVar(byte[] b, ref int at, int end)
        {
            uint value = 0;
            int shift = 0;
            for (int i = 0; i < 5; i++)
            {
                if (at >= end) throw new CodecException("That code is incomplete.");
                byte x = b[at++];
                value |= (uint)(x & 0x7F) << shift;
                if ((x & 0x80) == 0) return value;
                shift += 7;
            }
            throw new CodecException("That code is damaged (bad number).");
        }

        private static int ReadSVar(byte[] b, ref int at, int end)
        {
            uint z = ReadVar(b, ref at, end);
            return (int)(z >> 1) ^ -(int)(z & 1);
        }

        /// <summary>Printable-ASCII only, capped — the same guarantee
        /// <see cref="ProfileCapture.SanitizeName"/> gives every name in this mod, applied at the wire
        /// boundary so a hand-built document can never emit bytes the decoder would have to guess at
        /// (and so nothing arriving from outside can tofu in our own TMP labels).</summary>
        private static void WriteString(MemoryStream ms, string s)
        {
            if (string.IsNullOrEmpty(s)) { WriteVar(ms, 0); return; }
            var sb = new StringBuilder(Math.Min(s.Length, MaxStringChars));
            for (int i = 0; i < s.Length && sb.Length < MaxStringChars; i++)
            {
                char c = s[i];
                if (c >= 0x20 && c <= 0x7E) sb.Append(c);
            }
            WriteVar(ms, (uint)sb.Length);
            for (int i = 0; i < sb.Length; i++) ms.WriteByte((byte)sb[i]);
        }

        private static string ReadString(byte[] b, ref int at, int end)
        {
            uint len = ReadVar(b, ref at, end);
            if (len == 0) return string.Empty;
            if (len > MaxStringChars || at + len > end) throw new CodecException("That code is damaged (bad text).");
            var sb = new StringBuilder((int)len);
            for (uint i = 0; i < len; i++)
            {
                byte c = b[at++];
                // Deliberately NOT trimmed: read must be the exact inverse of write, or a name that
                // happens to end in a space would come back different and the round-trip self-test
                // would report a "failure" that is really this method's opinion.
                sb.Append(c >= 0x20 && c <= 0x7E ? (char)c : ' ');
            }
            return sb.ToString();
        }

        private static ushort Checksum16(byte[] data, int length)
        {
            uint h = 2166136261u;
            for (int i = 0; i < length; i++)
            {
                h ^= data[i];
                h *= 16777619u;
            }
            return (ushort)((h >> 16) ^ (h & 0xFFFF));
        }

        private static byte[] TryDeflate(byte[] payload)
        {
            try
            {
                using (var outMs = new MemoryStream(payload.Length / 2 + 64))
                {
                    using (var deflate = new DeflateStream(outMs, CompressionMode.Compress, true))
                        deflate.Write(payload, 0, payload.Length);
                    return outMs.ToArray();
                }
            }
            catch (Exception e)
            {
                UIALog.Warn("Share-code compression unavailable (" + e.Message + ") - writing a longer raw code.");
                return null;
            }
        }

        /// <summary>Inflate with a hard output cap: a 200-byte code that expands to a gigabyte is the
        /// one shape of attack a data-only format still has.</summary>
        private static byte[] TryInflate(byte[] container, int offset, int count)
        {
            try
            {
                using (var inMs = new MemoryStream(container, offset, count, false))
                using (var deflate = new DeflateStream(inMs, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    var buffer = new byte[4096];
                    int total = 0;
                    while (true)
                    {
                        int read = deflate.Read(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        total += read;
                        if (total > MaxPayloadBytes) return null;
                        outMs.Write(buffer, 0, read);
                    }
                    return outMs.ToArray();
                }
            }
            catch { return null; }
        }

        private static string StripWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }

        /// <summary>RFC 4648 §5 with the padding removed — '+'/'/' become '-'/'_' so the code survives
        /// URLs, Discord's formatter and a double-click select.</summary>
        private static string ToBase64Url(byte[] data)
        {
            string s = Convert.ToBase64String(data);
            return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        private static byte[] FromBase64Url(string s)
        {
            try
            {
                string t = s.Replace('-', '+').Replace('_', '/');
                switch (t.Length % 4)
                {
                    case 2: t += "=="; break;
                    case 3: t += "="; break;
                    case 1: return null;   // never a valid base64 length
                }
                return Convert.FromBase64String(t);
            }
            catch { return null; }
        }

        // ================= file drop for long codes =================

        /// <summary>Write the code beside the Stow Profiles, because a 5000-character clipboard is
        /// not something every OS/remote-desktop setup carries intact. Returns the full path, or
        /// null.</summary>
        public static string WriteExportFile(string setName, string code, string fingerprint)
        {
            if (string.IsNullOrEmpty(code)) return null;
            try
            {
                Directory.CreateDirectory(ExportDir);
                string safe = setName ?? "stow-profile";
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                safe = safe.Trim();
                if (safe.Length == 0) safe = "stow-profile";
                string path = Path.Combine(ExportDir, safe + ".txt");
                var sb = new StringBuilder();
                sb.Append("Stationeers UI Ascended - Stow Profile share code").Append(Environment.NewLine);
                sb.Append("Set:         ").Append(setName ?? "?").Append(Environment.NewLine);
                sb.Append("Fingerprint: ").Append(fingerprint ?? "?").Append(Environment.NewLine);
                sb.Append("Length:      ").Append(code.Length).Append(" characters").Append(Environment.NewLine);
                sb.Append("Paste the single line below into F10 > Storage > Stow Profiles > Import.")
                  .Append(Environment.NewLine).Append(Environment.NewLine);
                sb.Append(code).Append(Environment.NewLine);
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch (Exception e)
            {
                UIALog.Warn("Could not write the share-code file: " + e.Message);
                return null;
            }
        }
    }
}
