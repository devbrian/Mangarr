using System;
using System.Text;

namespace NzbDrone.Core.Indexers.Comix
{
    /// <summary>
    /// C# port of keiyoushi's <c>Hash.kt</c> at
    /// <c>src/en/comix/src/eu/kanade/tachiyomi/extension/en/comix/Hash.kt</c>
    /// (Apache-2.0; current as of 2026-05-08). comix.to's <c>/api/v1/manga/{hid}/chapters</c>
    /// endpoint requires a <c>_=</c> query-parameter token derived from the URL path via a
    /// 5-round RC4 + per-byte mutation transform. Without the token the endpoint returns
    /// <c>403 "Missing token."</c>; with the wrong shape we get the symptomatic <c>404 "page
    /// not found"</c> originally observed in the comix-indexer-404 debug session.
    ///
    /// <para>
    /// The 15 base64 keys (5 RC4 keys + 5 mutation keys + 5 prefix keys, one set per round)
    /// are public via the keiyoushi extension repo. Algorithm porting fidelity verified by
    /// reproducing keiyoushi's <c>Hash.generateHash("/manga/mr3m0/chapters")</c> result
    /// against this implementation.
    /// </para>
    ///
    /// <para>
    /// THREAT MODEL: this is NOT a cryptographic primitive — it is comix.to's anti-bot
    /// gate, equivalent to a CAPTCHA-replacement obfuscation layer. The keys are baked into
    /// every browser bundle; rotating them takes a coordinated frontend deploy. The keiyoushi
    /// extension community reuses these keys verbatim and the values have been stable for
    /// the lifetime of the extension. If comix.to rotates the keys, both keiyoushi and
    /// Mangarr break in lockstep — the daily soak workflow (Plan 03-06) will catch it.
    /// </para>
    /// </summary>
    internal static class ComixHash
    {
        // [RC4 key, mutKey, prefKey] × 5 rounds — verbatim from keiyoushi Hash.kt KEYS array.
        private static readonly string[] Keys =
        {
            "JxTcdyiA5GZxnbrmthXBQfU2IMTKcY1+3nNhbq98Sgo=", // 0  RC4 key  round 1
            "3PordjODbhqla382Cxapmo/1JiABJQcjiJj1+48gTJ4=", // 1  mutKey   round 1
            "OaKvnI5ARA==",                                 // 2  prefKey  round 1
            "MHNBHYWA7lvy867fXgvGcJwWDk79KqUJUVFsh3RwnnI=", // 3  RC4 key  round 2
            "8i0Cru/VJBSVB2Y1GcMDVpzx2WepOcfnWdd81yxICl4=", // 4  mutKey   round 2
            "Fyskubz8VvA=",                                 // 5  prefKey  round 2
            "B46L1x+UeWP+19cRpQ+OZvdLAK9EHID8g3mSgn57tew=", // 6  RC4 key  round 3
            "DTSTmUt6LpDUw9r1lSQqyb3YlFTzruT8tk8wUGkwehQ=", // 7  mutKey   round 3
            "vY/meeI=",                                     // 8  prefKey  round 3
            "7xWfIF5THL5LAnRgAARg+4mjWHPU9n3PQwvzbaMNi+Q=", // 9  RC4 key  round 4
            "bewtiTuV+HJk56xxkf2iCljLgruCpBmN9BgE8i6gc9M=", // 10 mutKey   round 4
            "/Xcb2zAu8AU=",                                 // 11 prefKey  round 4
            "WgeCQ3T8R51uTwVSiVa7Zy0dN6JOg6Z5JleMS+HV8Aw=", // 12 RC4 key  round 5
            "yXayUVFrrcW56jQCEfZzuCidjpnWKjTDUNT7XeX9i7k=", // 13 mutKey   round 5
            "tSLco2w=",                                     // 14 prefKey  round 5
        };

        private static int[] GetKeyBytes(int index)
        {
            if (index < 0 || index >= Keys.Length)
            {
                return Array.Empty<int>();
            }

            try
            {
                var raw = Convert.FromBase64String(Keys[index]);
                var ints = new int[raw.Length];
                for (var i = 0; i < raw.Length; i++)
                {
                    ints[i] = raw[i] & 0xFF;
                }

                return ints;
            }
            catch
            {
                return Array.Empty<int>();
            }
        }

        private static int[] Rc4(int[] key, int[] data)
        {
            if (key.Length == 0)
            {
                return data;
            }

            var s = new int[256];
            for (var i = 0; i < 256; i++)
            {
                s[i] = i;
            }

            var j = 0;
            for (var i = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) % 256;
                (s[i], s[j]) = (s[j], s[i]);
            }

            var ii = 0;
            j = 0;
            var output = new int[data.Length];
            for (var k = 0; k < data.Length; k++)
            {
                ii = (ii + 1) % 256;
                j = (j + s[ii]) % 256;
                (s[ii], s[j]) = (s[j], s[ii]);
                output[k] = data[k] ^ s[(s[ii] + s[j]) % 256];
            }

            return output;
        }

        private static int GetMutKey(int[] mk, int idx)
        {
            if (mk.Length == 0)
            {
                return 0;
            }

            return (idx % 32) < mk.Length ? mk[idx % 32] : 0;
        }

        // All Op* shift functions treat the input as an 8-bit unsigned value (the algorithm
        // operates on bytes 0-255). Casting to uint avoids sign-extension warnings (CS0675)
        // and the long-vs-int width mismatch (CS0266) under .NET 10's stricter type checks.
        private static int OpShiftRight7Left1(int e)
        {
            var u = (uint)(e & 0xFF);
            return (int)(((u >> 7) | (u << 1)) & 0xFF);
        }

        private static int OpShiftLeft1Right7(int e)
        {
            var u = (uint)(e & 0xFF);
            return (int)(((u << 1) | (u >> 7)) & 0xFF);
        }

        private static int OpShiftRight2Left6(int e)
        {
            var u = (uint)(e & 0xFF);
            return (int)(((u >> 2) | (u << 6)) & 0xFF);
        }

        private static int OpShiftLeft4Right4(int e)
        {
            var u = (uint)(e & 0xFF);
            return (int)(((u << 4) | (u >> 4)) & 0xFF);
        }

        private static int OpShiftRight4Left4(int e)
        {
            var u = (uint)(e & 0xFF);
            return (int)(((u >> 4) | (u << 4)) & 0xFF);
        }

        private static int[] Mutate(int[] data, int[] mutKey, int[] prefKey, int prefKeyLimit, int round)
        {
            var output = new System.Collections.Generic.List<int>(data.Length + prefKeyLimit);
            for (var o = 0; o < data.Length; o++)
            {
                if (o < prefKeyLimit && o < prefKey.Length)
                {
                    output.Add(prefKey[o]);
                }

                var n = data[o] ^ GetMutKey(mutKey, o);
                n = round switch
                {
                    1 => (o % 10) switch
                    {
                        0 => OpShiftRight7Left1(n),
                        1 => n ^ 37,
                        2 => n ^ 81,
                        3 => n ^ 147,
                        4 => OpShiftRight2Left6(n),
                        5 or 8 => OpShiftRight4Left4(n),
                        6 => n ^ 218,
                        7 => (n + 159) & 0xFF,
                        9 => n ^ 180,
                        _ => n,
                    },
                    2 => (o % 10) switch
                    {
                        0 or 9 => n ^ 180,
                        1 => OpShiftLeft1Right7(n),
                        2 => n ^ 147,
                        3 => OpShiftRight7Left1(n),
                        4 => OpShiftRight2Left6(n),
                        5 => OpShiftRight4Left4(n),
                        6 or 8 => (n + 159) & 0xFF,
                        7 => (n + 34) & 0xFF,
                        _ => n,
                    },
                    3 => (o % 10) switch
                    {
                        0 => n ^ 81,
                        1 => OpShiftRight4Left4(n),
                        2 or 9 => OpShiftLeft4Right4(n),
                        3 => n ^ 37,
                        4 => (n + 159) & 0xFF,
                        5 => OpShiftLeft1Right7(n),
                        6 => n ^ 180,
                        7 => (n + 34) & 0xFF,
                        8 => OpShiftRight2Left6(n),
                        _ => n,
                    },
                    4 => (o % 10) switch
                    {
                        0 or 7 => n ^ 218,
                        1 or 4 => OpShiftLeft1Right7(n),
                        2 => OpShiftRight7Left1(n),
                        3 => (n + 159) & 0xFF,
                        5 or 8 => n ^ 180,
                        6 => n ^ 147,
                        9 => n ^ 37,
                        _ => n,
                    },
                    5 => (o % 10) switch
                    {
                        0 => OpShiftLeft4Right4(n),
                        1 or 3 => n ^ 147,
                        2 => (n + 34) & 0xFF,
                        4 or 9 => n ^ 218,
                        5 or 7 => OpShiftLeft1Right7(n),
                        6 => n ^ 180,
                        8 => OpShiftRight2Left6(n),
                        _ => n,
                    },
                    _ => n,
                };
                output.Add(n & 0xFF);
            }

            return output.ToArray();
        }

        private static int[] Round1(int[] data) => Rc4(GetKeyBytes(0), Mutate(data, GetKeyBytes(1), GetKeyBytes(2), 7, 1));

        private static int[] Round2(int[] data) => Rc4(GetKeyBytes(3), Mutate(data, GetKeyBytes(4), GetKeyBytes(5), 8, 2));

        private static int[] Round3(int[] data) => Rc4(GetKeyBytes(6), Mutate(data, GetKeyBytes(7), GetKeyBytes(8), 5, 3));

        private static int[] Round4(int[] data) => Rc4(GetKeyBytes(9), Mutate(data, GetKeyBytes(10), GetKeyBytes(11), 8, 4));

        private static int[] Round5(int[] data) => Rc4(GetKeyBytes(12), Mutate(data, GetKeyBytes(13), GetKeyBytes(14), 5, 5));

        /// <summary>
        /// Generate the comix.to anti-bot token for a given API path. Mirrors keiyoushi's
        /// <c>Hash.generateHash(path)</c> verbatim (Apache-2.0).
        /// </summary>
        /// <param name="path">URL path WITHOUT the <c>/api/v1</c> prefix or query string,
        /// e.g. <c>"/manga/mr3m0/chapters"</c>.</param>
        /// <returns>URL-safe base64 token (no padding, no wrap) suitable for the <c>_=</c>
        /// query-parameter slot.</returns>
        public static string GenerateHash(string path)
        {
            // Java URLEncoder.encode("/manga/mr3m0/chapters", "UTF-8") produces
            // "%2Fmanga%2Fmr3m0%2Fchapters" — slashes ARE encoded. .NET Uri.EscapeDataString
            // matches that behavior. Then we apply keiyoushi's three normalization swaps
            // (+→%20 [for spaces — N/A here but required for parameter parity], *→%2A,
            // %7E→~) so the resulting bytes match keiyoushi's pre-RC4 input verbatim.
            var encoded = Uri.EscapeDataString(path)
                .Replace("+", "%20", StringComparison.Ordinal)
                .Replace("*", "%2A", StringComparison.Ordinal)
                .Replace("%7E", "~", StringComparison.Ordinal);

            var asciiBytes = Encoding.ASCII.GetBytes(encoded);
            var initial = new int[asciiBytes.Length];
            for (var i = 0; i < asciiBytes.Length; i++)
            {
                initial[i] = asciiBytes[i] & 0xFF;
            }

            var r5 = Round5(Round4(Round3(Round2(Round1(initial)))));

            var finalBytes = new byte[r5.Length];
            for (var i = 0; i < r5.Length; i++)
            {
                finalBytes[i] = (byte)r5[i];
            }

            // keiyoushi: Base64.URL_SAFE | NO_PADDING | NO_WRAP — matches .NET's
            // Convert.ToBase64String + manual + → -, / → _, = trim.
            var b64 = Convert.ToBase64String(finalBytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');

            return b64;
        }
    }
}
