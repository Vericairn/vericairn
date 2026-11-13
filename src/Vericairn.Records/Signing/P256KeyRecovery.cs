// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Vericairn.Records.Signing;

/// <summary>
/// The public keys an ECDSA P-256 signature over a digest verifies under, recovered from the signature itself (FIX-121), as 64-byte
/// big-endian X‖Y affine coordinates.
/// <para>
/// For a public key Q, a point other than infinity on the curve, and r and s in [1, n-1], verification accepts exactly when
/// X = e·s⁻¹·G + r·s⁻¹·Q is not infinity and x(X) mod n = r, where e is the digest reduced mod n. If it accepts, x(X) is r or r + n,
/// since x(X) &lt; p &lt; 2n, so X is one of the at most four points with such an x, and solving for Q gives Q = r⁻¹·(s·X - e·G). Conversely,
/// for each such point X, the key Q = r⁻¹·(s·X - e·G), when it is not infinity, makes the verification equation give exactly X, whose x
/// mod n is r. So the keys recovered here are exactly the keys the signature verifies under: at most four, and at most two when
/// r ≥ p - n, which is every signature a key holder makes in practice.
/// </para>
/// <para>
/// The arithmetic is the minimum this needs: 256-bit integers in four 64-bit limbs; Montgomery multiplication, addition and subtraction
/// modulo p and modulo n; exponentiation by the fixed public exponents p - 2 (inverse), (p + 1)/4 (square root, p being 3 mod 4) and
/// n - 2; points in Jacobian coordinates with the a = -3 doubling (dbl-2001-b) and the general addition (add-2007-bl) of the
/// Explicit-Formulas Database, each case of infinity, equal and opposite points handled explicitly; and scalar multiplication by 4-bit
/// windows, with a comb of the generator built once. Every input is public, so nothing here is constant time. A caller confirms what it
/// relies on with the platform's verification (<see cref="RecoveredKeys"/>).
/// </para>
/// </summary>
internal static class P256KeyRecovery
{
    /// <summary>The length of a recovered key: X then Y, 32 big-endian bytes each.</summary>
    internal const int PointBytes = 64;

    // FIPS 186-5 / SEC 2 parameters of P-256: the field prime p, the group order n, the curve's b and the generator G.
    private static readonly Modulus Field = new(Number.Parse("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"));
    private static readonly Modulus Order = new(Number.Parse("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"));
    private static readonly Number B = Field.ToMontgomery(Number.Parse("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B"));
    private static readonly Number Three = Field.ToMontgomery(new Number(3, 0, 0, 0));
    private static readonly Number FieldInverse = Number.Subtract(Field.M, new Number(2, 0, 0, 0), out _);
    private static readonly Number OrderInverse = Number.Subtract(Order.M, new Number(2, 0, 0, 0), out _);
    private static readonly Number SquareRoot = Number.Parse("3FFFFFFFC0000000400000000000000000000000400000000000000000000000");
    private static readonly Number SecondX = Number.Subtract(Field.M, Order.M, out _);
    private static readonly Point Infinity = new(Field.One, Field.One, default);
    private static readonly Point Generator = new(
        Field.ToMontgomery(Number.Parse("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296")),
        Field.ToMontgomery(Number.Parse("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5")), Field.One);
    private static readonly Point[][] GeneratorComb = Comb(Generator);

    /// <summary>
    /// The distinct keys a 64-byte IEEE P1363 signature (r then s) over a 32-byte digest verifies under, at most four; null when r or s is
    /// not in [1, n-1], where the arithmetic does not apply and the platform's verification answers.
    /// </summary>
    internal static byte[][]? Candidates(ReadOnlySpan<byte> digest, ReadOnlySpan<byte> signature)
    {
        if (digest.Length != 32) throw new ArgumentException("A P-256 digest is 32 bytes.", nameof(digest));
        if (signature.Length != RecordSigning.SignatureBytes) throw new ArgumentException("A P-256 signature is 64 bytes.", nameof(signature));
        Number r = Number.Read(signature[..32]), s = Number.Read(signature[32..]);
        if (r.IsZero || s.IsZero || !Number.Less(r, Order.M) || !Number.Less(s, Order.M)) return null;
        Number e = Number.Read(digest);
        if (!Number.Less(e, Order.M)) e = Number.Subtract(e, Order.M, out _);

        // u1 = s·r⁻¹ and u2 = -e·r⁻¹ mod n, so that each key is u1·X + u2·G.
        Number rInverse = Order.Power(Order.ToMontgomery(r), OrderInverse);
        Number u1 = Order.FromMontgomery(Order.Multiply(rInverse, Order.ToMontgomery(s)));
        Number u2 = Order.FromMontgomery(Order.Negate(Order.Multiply(rInverse, Order.ToMontgomery(e))));
        Point g = MultiplyGenerator(u2);
        var keys = new List<byte[]>(4);
        for (int k = 0; k < 2; k++)
        {
            if (k == 1 && !Number.Less(r, SecondX)) break;
            Number x = Field.ToMontgomery(k == 0 ? r : Number.Add(r, Order.M, out _));
            Number alpha = Field.Add(Field.Subtract(Field.Multiply(Field.Multiply(x, x), x), Field.Multiply(Three, x)), B);
            Number beta = Field.Power(alpha, SquareRoot);
            if (!Field.Multiply(beta, beta).Same(alpha)) continue;
            // X and -X give u1·X + u2·G and -(u1·X) + u2·G.
            Point a = Multiply(u1, Window(new Point(x, beta, Field.One)));
            Keep(keys, Add(a, g));
            Keep(keys, Add(Negate(a), g));
        }

        return [.. keys];

        // A key is a point other than infinity, kept once.
        static void Keep(List<byte[]> keys, in Point key)
        {
            if (Affine(key) is { } bytes && !keys.Exists(known => known.AsSpan().SequenceEqual(bytes))) keys.Add(bytes);
        }
    }

    /// <summary>
    /// The most keys a signature can verify under, from r alone: four when r &lt; p - n, so that r + n is also a coordinate, and two
    /// otherwise. A set of that many distinct keys the signature verifies under is every such key.
    /// </summary>
    internal static int MostKeys(ReadOnlySpan<byte> signature) => Number.Less(Number.Read(signature[..32]), SecondX) ? 4 : 2;

    /// <summary>Whether 64 bytes X‖Y are the affine coordinates of a point on the curve.</summary>
    internal static bool OnCurve(ReadOnlySpan<byte> point)
    {
        if (point.Length != PointBytes) return false;
        Number x = Number.Read(point[..32]), y = Number.Read(point[32..]);
        if (!Number.Less(x, Field.M) || !Number.Less(y, Field.M)) return false;
        Number xm = Field.ToMontgomery(x), ym = Field.ToMontgomery(y);
        return Field.Multiply(ym, ym).Same(Field.Add(Field.Subtract(Field.Multiply(Field.Multiply(xm, xm), xm), Field.Multiply(Three, xm)), B));
    }

    /// <summary>k·G for a 32-byte big-endian k, as X‖Y, or null for infinity. For tests of the arithmetic against the platform.</summary>
    internal static byte[]? MultiplyGenerator(ReadOnlySpan<byte> scalar) => Affine(MultiplyGenerator(Number.Read(scalar)));

    /// <summary>k·P for a point X‖Y on the curve and a 32-byte big-endian k, as X‖Y, or null for infinity. For tests of the arithmetic.</summary>
    internal static byte[]? Multiply(ReadOnlySpan<byte> point, ReadOnlySpan<byte> scalar)
    {
        if (!OnCurve(point)) throw new ArgumentException("Not a point on the curve.", nameof(point));
        var p = new Point(Field.ToMontgomery(Number.Read(point[..32])), Field.ToMontgomery(Number.Read(point[32..])), Field.One);
        return Affine(Multiply(Number.Read(scalar), Window(p)));
    }

    private static Point Double(in Point p)
    {
        if (p.IsInfinity || p.Y.IsZero) return Infinity;
        Number delta = Field.Multiply(p.Z, p.Z), gamma = Field.Multiply(p.Y, p.Y), beta = Field.Multiply(p.X, gamma);
        Number alpha = Field.Multiply(Field.Subtract(p.X, delta), Field.Add(p.X, delta));
        alpha = Field.Add(Field.Add(alpha, alpha), alpha);
        Number beta4 = Field.Add(beta, beta);
        beta4 = Field.Add(beta4, beta4);
        Number x3 = Field.Subtract(Field.Multiply(alpha, alpha), Field.Add(beta4, beta4));
        Number yz = Field.Add(p.Y, p.Z);
        Number z3 = Field.Subtract(Field.Subtract(Field.Multiply(yz, yz), gamma), delta);
        Number gamma8 = Field.Multiply(gamma, gamma);
        gamma8 = Field.Add(gamma8, gamma8);
        gamma8 = Field.Add(gamma8, gamma8);
        gamma8 = Field.Add(gamma8, gamma8);
        Number y3 = Field.Subtract(Field.Multiply(alpha, Field.Subtract(beta4, x3)), gamma8);
        return new(x3, y3, z3);
    }

    private static Point Add(in Point a, in Point b)
    {
        if (a.IsInfinity) return b;
        if (b.IsInfinity) return a;
        Number z1z1 = Field.Multiply(a.Z, a.Z), z2z2 = Field.Multiply(b.Z, b.Z);
        Number u1 = Field.Multiply(a.X, z2z2), u2 = Field.Multiply(b.X, z1z1);
        Number s1 = Field.Multiply(a.Y, Field.Multiply(b.Z, z2z2)), s2 = Field.Multiply(b.Y, Field.Multiply(a.Z, z1z1));
        Number h = Field.Subtract(u2, u1), r = Field.Subtract(s2, s1);
        // The same x: the same point, which is doubled, or opposite points, whose sum is infinity.
        if (h.IsZero) return r.IsZero ? Double(a) : Infinity;
        r = Field.Add(r, r);
        Number h2 = Field.Add(h, h);
        Number i = Field.Multiply(h2, h2), j = Field.Multiply(h, i), v = Field.Multiply(u1, i);
        Number x3 = Field.Subtract(Field.Subtract(Field.Multiply(r, r), j), Field.Add(v, v));
        Number s1j = Field.Multiply(s1, j);
        Number y3 = Field.Subtract(Field.Multiply(r, Field.Subtract(v, x3)), Field.Add(s1j, s1j));
        Number zz = Field.Add(a.Z, b.Z);
        Number z3 = Field.Multiply(Field.Subtract(Field.Subtract(Field.Multiply(zz, zz), z1z1), z2z2), h);
        return new(x3, y3, z3);
    }

    private static Point Negate(in Point p) => p.IsInfinity ? p : new(p.X, Field.Negate(p.Y), p.Z);

    /// <summary>0·P to 15·P.</summary>
    private static Point[] Window(in Point p)
    {
        var table = new Point[16];
        table[0] = Infinity;
        table[1] = p;
        for (int i = 2; i < 16; i++) table[i] = Add(table[i - 1], p);
        return table;
    }

    /// <summary>For each of the 64 nibbles of a scalar, 0 to 15 times 16^i·P.</summary>
    private static Point[][] Comb(in Point p)
    {
        var comb = new Point[64][];
        Point power = p;
        for (int i = 0; i < 64; i++)
        {
            comb[i] = Window(power);
            power = Double(Double(Double(Double(power))));
        }

        return comb;
    }

    /// <summary>k·P from P's window, most significant nibble first.</summary>
    private static Point Multiply(Number k, Point[] window)
    {
        Point sum = Infinity;
        for (int i = 63; i >= 0; i--)
        {
            sum = Double(Double(Double(Double(sum))));
            sum = Add(sum, window[k.Nibble(i)]);
        }

        return sum;
    }

    private static Point MultiplyGenerator(Number k)
    {
        Point sum = Infinity;
        for (int i = 0; i < 64; i++) sum = Add(sum, GeneratorComb[i][k.Nibble(i)]);
        return sum;
    }

    private static byte[]? Affine(in Point p)
    {
        if (p.IsInfinity) return null;
        Number inverse = Field.Power(p.Z, FieldInverse), inverse2 = Field.Multiply(inverse, inverse);
        var bytes = new byte[PointBytes];
        Field.FromMontgomery(Field.Multiply(p.X, inverse2)).Write(bytes.AsSpan(0, 32));
        Field.FromMontgomery(Field.Multiply(p.Y, Field.Multiply(inverse2, inverse))).Write(bytes.AsSpan(32, 32));
        return bytes;
    }

    /// <summary>A point in Jacobian coordinates, x = X/Z² and y = Y/Z³, each in Montgomery form; Z = 0 is infinity.</summary>
    private readonly struct Point(Number x, Number y, Number z)
    {
        internal readonly Number X = x, Y = y, Z = z;

        internal bool IsInfinity => Z.IsZero;
    }

    /// <summary>A 256-bit unsigned integer in four 64-bit limbs, least significant first.</summary>
    private readonly struct Number(ulong w0, ulong w1, ulong w2, ulong w3)
    {
        internal readonly ulong W0 = w0, W1 = w1, W2 = w2, W3 = w3;

        internal bool IsZero => (W0 | W1 | W2 | W3) == 0;

        internal bool Same(in Number other) => W0 == other.W0 && W1 == other.W1 && W2 == other.W2 && W3 == other.W3;

        internal static Number Read(ReadOnlySpan<byte> bigEndian) => new(
            BinaryPrimitives.ReadUInt64BigEndian(bigEndian[24..]), BinaryPrimitives.ReadUInt64BigEndian(bigEndian[16..]),
            BinaryPrimitives.ReadUInt64BigEndian(bigEndian[8..]), BinaryPrimitives.ReadUInt64BigEndian(bigEndian));

        internal void Write(Span<byte> bigEndian)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bigEndian, W3);
            BinaryPrimitives.WriteUInt64BigEndian(bigEndian[8..], W2);
            BinaryPrimitives.WriteUInt64BigEndian(bigEndian[16..], W1);
            BinaryPrimitives.WriteUInt64BigEndian(bigEndian[24..], W0);
        }

        internal static Number Parse(string hex) => Read(Convert.FromHexString(hex));

        /// <summary>The i-th 4-bit digit, 0 the least significant.</summary>
        internal int Nibble(int i) => (int)(((i >> 4) switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 }) >> (4 * (i & 15)) & 0xF);

        /// <summary>The i-th bit, 0 the least significant.</summary>
        internal bool Bit(int i) => (((i >> 6) switch { 0 => W0, 1 => W1, 2 => W2, _ => W3 }) >> (i & 63) & 1) != 0;

        internal static bool Less(in Number a, in Number b)
        {
            Subtract(a, b, out ulong borrow);
            return borrow != 0;
        }

        internal static Number Add(in Number a, in Number b, out ulong carry)
        {
            ulong c = 0;
            ulong r0 = AddCarry(a.W0, b.W0, ref c), r1 = AddCarry(a.W1, b.W1, ref c), r2 = AddCarry(a.W2, b.W2, ref c), r3 = AddCarry(a.W3, b.W3, ref c);
            carry = c;
            return new(r0, r1, r2, r3);
        }

        internal static Number Subtract(in Number a, in Number b, out ulong borrow)
        {
            ulong c = 0;
            ulong r0 = SubtractBorrow(a.W0, b.W0, ref c), r1 = SubtractBorrow(a.W1, b.W1, ref c), r2 = SubtractBorrow(a.W2, b.W2, ref c), r3 = SubtractBorrow(a.W3, b.W3, ref c);
            borrow = c;
            return new(r0, r1, r2, r3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AddCarry(ulong a, ulong b, ref ulong carry)
        {
            ulong sum = a + b;
            ulong high = sum < a ? 1UL : 0UL;
            ulong result = sum + carry;
            carry = high + (result < sum ? 1UL : 0UL);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SubtractBorrow(ulong a, ulong b, ref ulong borrow)
        {
            ulong difference = a - b;
            ulong high = a < b ? 1UL : 0UL;
            ulong result = difference - borrow;
            borrow = high + (difference < borrow ? 1UL : 0UL);
            return result;
        }
    }

    /// <summary>
    /// Arithmetic modulo an odd M between 2^255 and 2^256 (p or n) in Montgomery form, a·2^256 mod M: CIOS multiplication, and addition
    /// and subtraction by one conditional subtraction or addition of M. Every operand and result is below M.
    /// </summary>
    private sealed class Modulus
    {
        internal readonly Number M;
        internal readonly Number One;
        private readonly Number squared;
        private readonly ulong inverse;

        internal Modulus(Number m)
        {
            M = m;
            // -M⁻¹ mod 2^64 by Newton's iteration, each step doubling the correct low bits of M⁻¹.
            ulong x = 1;
            for (int i = 0; i < 6; i++) x *= 2 - m.W0 * x;
            inverse = 0 - x;
            // 2^256 mod M is 2^256 - M, since M > 2^255; 2^512 mod M by 256 doublings of it.
            One = Number.Subtract(default, m, out _);
            Number r = One;
            for (int i = 0; i < 256; i++) r = Add(r, r);
            squared = r;
        }

        internal Number Add(in Number a, in Number b)
        {
            Number sum = Number.Add(a, b, out ulong carry);
            Number reduced = Number.Subtract(sum, M, out ulong borrow);
            return carry != 0 || borrow == 0 ? reduced : sum;
        }

        internal Number Subtract(in Number a, in Number b)
        {
            Number difference = Number.Subtract(a, b, out ulong borrow);
            return borrow != 0 ? Number.Add(difference, M, out _) : difference;
        }

        internal Number Negate(in Number a) => Subtract(default, a);

        /// <summary>a·b·2^-256 mod M, for a below 2^256 and b below M.</summary>
        internal Number Multiply(in Number a, in Number b)
        {
            ulong t0 = 0, t1 = 0, t2 = 0, t3 = 0, t4 = 0;
            Step(a, b.W0, ref t0, ref t1, ref t2, ref t3, ref t4);
            Step(a, b.W1, ref t0, ref t1, ref t2, ref t3, ref t4);
            Step(a, b.W2, ref t0, ref t1, ref t2, ref t3, ref t4);
            Step(a, b.W3, ref t0, ref t1, ref t2, ref t3, ref t4);
            // Below 2M: one subtraction of M at most.
            var t = new Number(t0, t1, t2, t3);
            Number reduced = Number.Subtract(t, M, out ulong borrow);
            return t4 != 0 || borrow == 0 ? reduced : t;
        }

        internal Number ToMontgomery(in Number a) => Multiply(a, squared);

        internal Number FromMontgomery(in Number a) => Multiply(a, new Number(1, 0, 0, 0));

        /// <summary>a^e for a in Montgomery form and a plain exponent, by squaring and multiplying from the top bit.</summary>
        internal Number Power(in Number a, in Number e)
        {
            Number result = One;
            for (int i = 255; i >= 0; i--)
            {
                result = Multiply(result, result);
                if (e.Bit(i)) result = Multiply(result, a);
            }

            return result;
        }

        /// <summary>One word of CIOS: t += a·word, then t += m·M with m chosen so the low word is zero, then t is shifted down a word.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Step(in Number a, ulong word, ref ulong t0, ref ulong t1, ref ulong t2, ref ulong t3, ref ulong t4)
        {
            ulong c = MultiplyAdd(a.W0, word, t0, 0, out t0);
            c = MultiplyAdd(a.W1, word, t1, c, out t1);
            c = MultiplyAdd(a.W2, word, t2, c, out t2);
            c = MultiplyAdd(a.W3, word, t3, c, out t3);
            ulong top = t4 + c;
            ulong t5 = top < c ? 1UL : 0UL;
            t4 = top;
            ulong m = t0 * inverse;
            c = MultiplyAdd(m, M.W0, t0, 0, out _);
            c = MultiplyAdd(m, M.W1, t1, c, out t0);
            c = MultiplyAdd(m, M.W2, t2, c, out t1);
            c = MultiplyAdd(m, M.W3, t3, c, out t2);
            top = t4 + c;
            t3 = top;
            t4 = t5 + (top < c ? 1UL : 0UL);
        }

        /// <summary>a·b + c + d, which fits in 128 bits: the high word returned, the low word out.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong MultiplyAdd(ulong a, ulong b, ulong c, ulong d, out ulong low)
        {
            ulong high = Math.BigMul(a, b, out low);
            low += c;
            high += low < c ? 1UL : 0UL;
            low += d;
            high += low < d ? 1UL : 0UL;
            return high;
        }
    }
}
