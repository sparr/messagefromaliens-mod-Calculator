using System;
using System.Collections.Generic;
using System.Numerics;

namespace Calculator
{
    // A value: an exact fraction when every step so far was exact, otherwise a double.
    internal readonly struct Num
    {
        internal readonly bool Exact;
        // Exact values: N / D in lowest terms, D > 0.
        internal readonly BigInteger N, D;
        internal readonly double F;

        private Num(BigInteger n, BigInteger d)
        {
            if (d.Sign < 0)
            {
                n = -n;
                d = -d;
            }
            var g = BigInteger.GreatestCommonDivisor(n, d);
            if (!g.IsZero && !g.IsOne)
            {
                n /= g;
                d /= g;
            }
            Exact = true;
            N = n;
            D = d;
            F = 0;
        }

        private Num(double f)
        {
            Exact = false;
            N = BigInteger.Zero;
            D = BigInteger.One;
            F = f;
        }

        internal static Num Of(BigInteger n) => new Num(n, BigInteger.One);
        internal static Num Of(BigInteger n, BigInteger d) => new Num(n, d);
        internal static Num Of(double f) => new Num(f);

        internal bool IsInteger => Exact && D.IsOne;
        internal bool IsZero => Exact ? N.IsZero : F == 0;
        // Zero for NaN, which Math.Sign would throw on.
        internal int Sign => Exact ? N.Sign : double.IsNaN(F) ? 0 : Math.Sign(F);
        internal bool Finite => Exact || (!double.IsNaN(F) && !double.IsInfinity(F));

        internal double ToDouble() => Exact ? Ratio(N, D) : F;

        // n / d to a double's precision, for any sizes: a quotient of about 62 bits, scaled by the
        // power of two it was shifted by. Overflows to infinity and underflows to zero as a
        // double would.
        private static double Ratio(BigInteger n, BigInteger d)
        {
            if (n.IsZero)
            {
                return 0;
            }
            var negative = n.Sign < 0;
            n = BigInteger.Abs(n);
            var e = (long)Math.Floor(BigInteger.Log(n, 2)) - (long)Math.Floor(BigInteger.Log(d, 2));
            var shift = 62 - e;
            var q = shift >= 0 ? (n << (int)Math.Min(shift, int.MaxValue)) / d : n / (d << (int)Math.Min(-shift, int.MaxValue));
            var f = (double)q * Math.Pow(2, -shift);
            if (double.IsNaN(f))
            {
                // A power of two out of range times a nonzero quotient.
                f = shift < 0 ? double.PositiveInfinity : 0;
            }
            return negative ? -f : f;
        }

        // The natural log of a positive value, from its exact parts when it has them, so values
        // beyond a double's range still have one.
        private static double Ln(Num a) =>
            a.Exact ? BigInteger.Log(BigInteger.Abs(a.N)) - BigInteger.Log(a.D) : Math.Log(a.F);

        // A sum or difference of doubles that cancels to within their rounding error is zero, as
        // 0.1 + 0.2 - 0.3 should be.
        private static Num Cancel(double result, double a, double b) =>
            Of(Math.Abs(result) <= 1e-12 * Math.Max(Math.Abs(a), Math.Abs(b)) ? 0 : result);

        internal Num Negate() => Exact ? Of(-N, D) : Of(-F);

        public static Num operator +(Num a, Num b)
        {
            if (a.Exact && b.Exact)
            {
                return Of(a.N * b.D + b.N * a.D, a.D * b.D);
            }
            double x = a.ToDouble(), y = b.ToDouble();
            return Cancel(x + y, x, y);
        }

        public static Num operator -(Num a, Num b) => a + b.Negate();

        public static Num operator *(Num a, Num b) =>
            a.Exact && b.Exact ? Of(a.N * b.N, a.D * b.D) : Of(a.ToDouble() * b.ToDouble());

        // Null for division by zero.
        internal static Num? Divide(Num a, Num b)
        {
            if (b.IsZero)
            {
                return null;
            }
            return a.Exact && b.Exact ? Of(a.N * b.D, a.D * b.N) : Of(a.ToDouble() / b.ToDouble());
        }

        // Division as the game teaches it with a remainder: the quotient is the whole number
        // rounded down, and the remainder is what is left, so -13 / 6 = -3 remainder 5 and
        // 5 / -2 = -3 remainder -1. False for division by zero.
        internal static bool FloorDivide(Num a, Num b, out Num quotient, out Num remainder)
        {
            quotient = remainder = default;
            if (b.IsZero)
            {
                return false;
            }
            if (a.Exact && b.Exact)
            {
                var n = a.N * b.D;
                var d = a.D * b.N;
                if (d.Sign < 0)
                {
                    n = -n;
                    d = -d;
                }
                var q = BigInteger.DivRem(n, d, out var r);
                if (r.Sign < 0)
                {
                    q -= 1;
                }
                quotient = Of(q);
            }
            else
            {
                // A quotient within rounding error of a simple fraction, such as 3 shown for
                // 2.9999999999999996, is floored as that fraction.
                var q = Glyphs.Snap(Of(a.ToDouble() / b.ToDouble()));
                quotient = q.Exact ? Of(Floor(q.N, q.D)) : Of(Math.Floor(q.F));
            }
            remainder = a - b * quotient;
            return true;
        }

        // The whole number at or below n / d, for d > 0.
        private static BigInteger Floor(BigInteger n, BigInteger d)
        {
            var q = BigInteger.DivRem(n, d, out var r);
            return r.Sign < 0 ? q - 1 : q;
        }

        // The nth root: exact when the value is an exact nth power. Null for an even root of a
        // negative value.
        internal static Num? Root(Num a, int n)
        {
            if (a.Sign < 0 && n % 2 == 0)
            {
                return null;
            }
            if (a.Exact)
            {
                var top = IntegerRoot(BigInteger.Abs(a.N), n);
                var bottom = IntegerRoot(a.D, n);
                if (top.HasValue && bottom.HasValue)
                {
                    return Of(a.N.Sign < 0 ? -top.Value : top.Value, bottom.Value);
                }
            }
            var f = a.ToDouble();
            return Of(n == 2 ? Math.Sqrt(f) : n == 3 ? Math.Cbrt(f) : Math.Sign(f) * Math.Pow(Math.Abs(f), 1.0 / n));
        }

        // The exact nth root of x, if x is an nth power: the integer root by Newton's method,
        // from an estimate above it, then checked.
        private static BigInteger? IntegerRoot(BigInteger x, int n)
        {
            if (x.Sign <= 0)
            {
                return x.IsZero ? BigInteger.Zero : (BigInteger?)null;
            }
            var bits = (int)Math.Floor(BigInteger.Log(x, 2)) + 1;
            var r = BigInteger.One << ((bits + n - 1) / n);
            while (true)
            {
                var next = ((n - 1) * r + x / BigInteger.Pow(r, n - 1)) / n;
                if (next >= r)
                {
                    break;
                }
                r = next;
            }
            return BigInteger.Pow(r, n) == x ? r : (BigInteger?)null;
        }

        internal static Num Sin(Num a) => Trig(Math.Sin, a.ToDouble());
        internal static Num Cos(Num a) => Trig(Math.Cos, a.ToDouble());

        // Zero where the true value is zero but pi's rounding leaves a tiny remainder, as for the
        // sine of pi. The remainder grows with the argument.
        private static Num Trig(System.Func<double, double> f, double x)
        {
            var r = f(x);
            return Of(Math.Abs(x) >= 1 && Math.Abs(r) <= 1e-15 * Math.Abs(x) ? 0 : r);
        }

        // Exact for an integer power of an exact value, unless the result would be huge.
        internal static Num? Power(Num a, Num b)
        {
            if (a.Exact && b.IsInteger && BigInteger.Abs(b.N) <= 4096)
            {
                var e = (int)b.N;
                if (a.IsZero && e < 0)
                {
                    return null;
                }
                var bits = (BigInteger.Log(BigInteger.Abs(a.N) + 1, 2) + BigInteger.Log(a.D, 2)) * Math.Abs(e);
                if (bits <= 8192)
                {
                    var n = BigInteger.Pow(a.N, Math.Abs(e));
                    var d = BigInteger.Pow(a.D, Math.Abs(e));
                    return e >= 0 ? Of(n, d) : Of(d, n);
                }
            }
            var f = Math.Pow(a.ToDouble(), b.ToDouble());
            return double.IsNaN(f) ? (Num?)null : Of(f);
        }

        // The game writes "x log b" for the logarithm of x in base b: 4 log 2 = 2. Exact when
        // the answer is an integer.
        internal static Num? Log(Num x, Num b)
        {
            if (x.Sign <= 0 || b.Sign <= 0 || (b.Exact && b.N == b.D) || (!b.Exact && b.F == 1))
            {
                return null;
            }
            var f = Ln(x) / Ln(b);
            if (double.IsNaN(f) || double.IsInfinity(f))
            {
                return null;
            }
            if (x.Exact && b.Exact && Math.Abs(f) < 4096)
            {
                var k = Of(new BigInteger(Math.Round(f)));
                var p = Power(b, k);
                if (p.HasValue && p.Value.Exact && p.Value.N == x.N && p.Value.D == x.D)
                {
                    return k;
                }
            }
            return Of(f);
        }
    }

    // Writes values in the game's glyphs. The game writes numbers in base 7 with one glyph per
    // nonzero digit, each marked with its place: "2e1 3" is 2 sevens and 3, "4e-1" is 4/7.
    // A fraction that repeats ends with a repeat glyph and the repeating digits.
    internal static class Glyphs
    {
        // The smallest place the game's own text uses. Callers choose the largest, and values
        // beyond it are written in the game's scientific form.
        private const int MinPlace = -3;
        // Longest repeating part written out.
        internal const int MaxRepeat = 6;
        // Repeat glyphs by the number of zero digits between the written digits and the repeat.
        internal static readonly string[] Repeat = { "loop", "loop1", "loop2", "loop3" };
        private const string Infinite = "infbig";

        private static readonly BigInteger Seven = 7;

        // The glyphs for a value, and whether they are exact, using places up to 7^maxPlace and in
        // at most maxCells glyphs: longer exact forms are rounded instead, and larger values use
        // the scientific form if allowed. Null if the value cannot be written.
        internal static List<string> Write(Num v, out bool exact, int maxPlace, bool scientific, int maxCells = int.MaxValue)
        {
            exact = false;
            if (!v.Finite)
            {
                return null;
            }
            if (!v.Exact && Simple(v.F) is Num simple)
            {
                v = simple;
            }
            if (v.Exact)
            {
                var written = WriteExact(v.N, v.D, maxPlace);
                if (written != null && written.Count <= maxCells)
                {
                    exact = true;
                    return written;
                }
            }
            var f = v.ToDouble();
            // The game's writer for the scientific form overflows past about 4.5e307.
            if (double.IsInfinity(f) || Math.Abs(f) > 4e307)
            {
                if (!scientific)
                {
                    return null;
                }
                // Beyond what a double holds, which the game's scientific form needs.
                return f < 0 ? new List<string> { Keys.Negative, Infinite } : new List<string> { Infinite };
            }
            // Rounded to the smallest place, unless that keeps fewer than three significant digits
            // and the scientific form can keep them.
            var rounded = scientific && Math.Abs(f) < Math.Pow(7, MinPlace + 2) ? null : Round(f, maxPlace);
            if (rounded != null)
            {
                var written = WriteExact(rounded.Value.N, rounded.Value.D, maxPlace);
                if (written != null && written.Count <= maxCells)
                {
                    return written;
                }
            }
            if (!scientific)
            {
                // Too big to write, or too small for the smallest place, which rounds to zero.
                return Math.Abs(f) < 1 ? new List<string> { "0" } : null;
            }
            exact = ThreeDigits(v);
            return Scientific(f);
        }

        // The value as the simple fraction it is within rounding error of, or itself. Results
        // are computed and carried as this, so what is shown is what is used.
        internal static Num Snap(Num v) => !v.Exact && Simple(v.F) is Num s ? s : v;

        // A fraction with a small denominator that a double differs from only by rounding error,
        // such as the sine of pi/6, which a double puts just below 1/2. Zero is the exception:
        // a value near zero is small, not wrong, so it is never taken for zero.
        private static Num? Simple(double f)
        {
            const int MaxDenominator = 2401;
            if (double.IsNaN(f) || double.IsInfinity(f) || Math.Abs(f) > 1e12)
            {
                return null;
            }
            // Continued fraction convergents h/k.
            double x = Math.Abs(f), h0 = 1, h1 = Math.Floor(x), k0 = 0, k1 = 1;
            var rest = x - h1;
            while (true)
            {
                if (Math.Abs(x - h1 / k1) <= 1e-12 * x)
                {
                    var n = new BigInteger(h1);
                    return Num.Of(f < 0 ? -n : n, new BigInteger(k1));
                }
                if (rest < 1e-15)
                {
                    return null;
                }
                var a = Math.Floor(1 / rest);
                rest = 1 / rest - a;
                var h2 = a * h1 + h0;
                var k2 = a * k1 + k0;
                if (k2 > MaxDenominator)
                {
                    return null;
                }
                h0 = h1;
                h1 = h2;
                k0 = k1;
                k1 = k2;
            }
        }

        // Whether an exact value has at most the three base-7 digits of the scientific form.
        private static bool ThreeDigits(Num v)
        {
            if (!v.Exact || v.IsZero)
            {
                return v.Exact;
            }
            var n = BigInteger.Abs(v.N);
            var k = (int)Math.Floor((BigInteger.Log(n) - BigInteger.Log(v.D)) / Math.Log(7));
            // Correct the estimate so that 7^k <= |v| < 7^(k+1).
            while (Compare(n, v.D, k) < 0)
            {
                k--;
            }
            while (Compare(n, v.D, k + 1) >= 0)
            {
                k++;
            }
            var shift = 2 - k;
            var num = shift >= 0 ? n * BigInteger.Pow(Seven, shift) : n;
            var den = shift >= 0 ? v.D : v.D * BigInteger.Pow(Seven, -shift);
            return (num % den).IsZero;
        }

        // The sign of n/d - 7^k.
        private static int Compare(BigInteger n, BigInteger d, int k) =>
            k >= 0 ? n.CompareTo(d * BigInteger.Pow(Seven, k)) : (n * BigInteger.Pow(Seven, -k)).CompareTo(d);

        // Rounded to the smallest place as the game rounds: the last digit goes up by one only
        // when the next digit is 4 or more. Null if out of range, or if that rounds to zero.
        private static Num? Round(double f, int maxPlace)
        {
            var scale = Math.Pow(7, -MinPlace);
            var a = Math.Abs(f) * scale;
            var t = Math.Floor(a);
            if (Math.Floor((a - t) * 7) >= 4)
            {
                t++;
            }
            if (Math.Abs(f) >= Math.Pow(7, maxPlace + 1) || (t == 0 && f != 0))
            {
                return null;
            }
            return Num.Of(new BigInteger(f < 0 ? -t : t), new BigInteger(scale));
        }

        // The game's own scientific form, for values outside the places it draws.
        private static List<string> Scientific(double f) => Global.ToBase7Exp3Digit(f);

        private static List<string> WriteExact(BigInteger n, BigInteger d, int maxPlace)
        {
            var list = new List<string>();
            if (n.Sign < 0)
            {
                list.Add(Keys.Negative);
                n = -n;
            }
            var whole = BigInteger.DivRem(n, d, out var rest);
            if (whole >= BigInteger.Pow(Seven, maxPlace + 1))
            {
                return null;
            }
            list.AddRange(Whole(whole, rest.IsZero));
            if (rest.IsZero)
            {
                return list;
            }
            // Long division, noting where each remainder first appears to find the repeat. The repeat
        // glyph says how many zero places come between the last written digit (or the point)
        // and the repeating digits, from none to three; the game has no glyph for more.
            var digits = new List<int>();
            var seen = new Dictionary<BigInteger, int>();
            while (!rest.IsZero && !seen.ContainsKey(rest))
            {
                seen[rest] = digits.Count;
                rest *= 7;
                digits.Add((int)BigInteger.DivRem(rest, d, out rest));
                if (digits.Count > -MinPlace + MaxRepeat + Repeat.Length)
                {
                    return null;
                }
            }
            var start = rest.IsZero ? digits.Count : seen[rest];
            var written = 0;
            for (var i = 0; i < start; i++)
            {
                if (digits[i] != 0)
                {
                    list.Add(digits[i] + "e" + -(i + 1));
                    written = i + 1;
                }
            }
            if (written > -MinPlace)
            {
                return null;
            }
            if (rest.IsZero)
            {
                return list;
            }
            var zeros = start - written;
            var cycle = digits.Count - start;
            if (zeros >= Repeat.Length || cycle > MaxRepeat)
            {
                return null;
            }
            if (whole.IsZero && written == 0)
            {
                list.Add("0");
            }
            list.Add(Repeat[zeros]);
            for (var i = start; i < digits.Count; i++)
            {
                list.Add(digits[i].ToString());
            }
            return list;
        }

        // A whole number's glyphs; zero is written only when nothing else is.
        private static List<string> Whole(BigInteger n, bool alone)
        {
            var list = new List<string>();
            if (n.IsZero)
            {
                if (alone)
                {
                    list.Add("0");
                }
                return list;
            }
            var place = 0;
            var unit = BigInteger.One;
            while (unit * 7 <= n)
            {
                unit *= 7;
                place++;
            }
            for (; place >= 0; place--, unit /= 7)
            {
                var digit = (int)BigInteger.DivRem(n, unit, out n);
                if (digit != 0)
                {
                    list.Add(place == 0 ? digit.ToString() : digit + "e" + place);
                }
            }
            return list;
        }

    }
}
