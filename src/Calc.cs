using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Calculator
{
    // What the player has unlocked, by the quizzes they have solved.
    internal sealed class Features
    {
        internal bool Open, MultiplyDivide, Fractions, PowerLog;
        internal bool Squares, SquareRoot, CubeRoot, Trig, E, Pi, Chance;
        // The game's scientific form, after the quiz that follows its lesson.
        internal bool Scientific;

        internal bool Allows(Op op) => op switch
        {
            Op.Plus or Op.Minus => Open,
            Op.Multiply or Op.Divide => MultiplyDivide,
            _ => PowerLog,
        };

        internal bool Allows(Func f) => f switch
        {
            Func.Square or Func.Cube => Squares,
            Func.SquareRoot => SquareRoot,
            Func.CubeRoot => CubeRoot,
            _ => Trig,
        };

        internal bool Allows(Constant c) => c == Constant.E ? E : Pi;

        // The approximately-equal sign, after the quiz that follows its lesson.
        internal bool Approximate;
    }

    // The calculator's expression, in the order entered: numbers, operations between them,
    // prefix functions before a number, and postfix functions after one.
    internal sealed class Calc
    {
        private const int MaxFractionDigits = 3;
        private static readonly Random random = new Random();

        private sealed class Entry
        {
            internal bool Negative;
            internal readonly List<int> Whole = new List<int>();
            internal bool Point;
            internal readonly List<int> Fraction = new List<int>();
            // The zero places between the fraction digits and the repeating digits, or -1 when
            // the number does not repeat.
            internal int Zeros = -1;
            internal readonly List<int> Repeating = new List<int>();
            // A value not typed as digits: a constant, a random value, or a carried result.
            internal Num? Literal;
            internal Constant? Constant;
            // A result carried over from the last evaluation; typing a digit starts afresh.
            internal bool Carried;

            internal bool Typed => !Literal.HasValue && !Constant.HasValue;

            internal Num Value
            {
                get
                {
                    Num v;
                    if (Constant.HasValue)
                    {
                        v = Num.Of(Constant.Value == Calculator.Constant.E ? Math.E : Math.PI);
                    }
                    else if (Literal.HasValue)
                    {
                        v = Literal.Value;
                    }
                    else
                    {
                        BigInteger n = 0;
                        foreach (var digit in Whole.Concat(Fraction))
                        {
                            n = n * 7 + digit;
                        }
                        var places = Fraction.Count;
                        if (Zeros >= 0 && Repeating.Count > 0)
                        {
                            // n.f 0..0 (r)(r)... = (n.f 0..0 * (7^k - 1) + r) / (7^places * (7^k - 1))
                            places += Zeros;
                            n *= BigInteger.Pow(7, Zeros);
                            var period = BigInteger.Pow(7, Repeating.Count) - 1;
                            BigInteger r = 0;
                            foreach (var digit in Repeating)
                            {
                                r = r * 7 + digit;
                            }
                            v = Num.Of(n * period + r, BigInteger.Pow(7, places) * period);
                        }
                        else
                        {
                            v = Num.Of(n, BigInteger.Pow(7, places));
                        }
                    }
                    return Negative ? v.Negate() : v;
                }
            }

            internal bool Empty => Typed && Whole.Count == 0 && !Point;
        }

        private sealed class Prefix
        {
            internal Func Func;
        }

        private sealed class Postfix
        {
            internal Func Func;
        }

        // Entries, Ops, Prefixes, and Postfixes.
        private readonly List<object> items = new List<object>();

        private object LastItem => items.Count > 0 ? items[items.Count - 1] : null;
        private Entry Last => LastItem as Entry;

        // Whether a new number may start here: at the start, or after an operation or prefix.
        internal bool AwaitsNumber => LastItem == null || LastItem is Op || LastItem is Prefix;

        // A typed entry to add to, starting one where a number may start. Null if the
        // expression ends in something digits cannot follow.
        private Entry TypedEntry()
        {
            var e = Last;
            if (e != null && e.Carried)
            {
                Reset();
                e = null;
            }
            if (e != null)
            {
                return e.Typed ? e : null;
            }
            if (!AwaitsNumber)
            {
                return null;
            }
            e = new Entry();
            items.Add(e);
            return e;
        }

        internal void Digit(int digit, Features f, int maxWholeDigits)
        {
            var e = TypedEntry();
            if (e == null)
            {
                return;
            }
            if (e.Zeros >= 0)
            {
                if (e.Repeating.Count < Calculator.Glyphs.MaxRepeat)
                {
                    e.Repeating.Add(digit);
                }
            }
            else if (e.Point)
            {
                if (f.Fractions && e.Fraction.Count < MaxFractionDigits)
                {
                    e.Fraction.Add(digit);
                }
            }
            else if (e.Whole.Count == 1 && e.Whole[0] == 0)
            {
                e.Whole[0] = digit;
            }
            else if (e.Whole.Count < maxWholeDigits)
            {
                e.Whole.Add(digit);
            }
        }

        internal void Point(Features f)
        {
            if (f.Fractions)
            {
                var e = TypedEntry();
                if (e != null)
                {
                    e.Point = true;
                }
            }
        }

        // Starts the repeating digits of the number being typed, or moves them one more zero
        // place along, cycling from the last repeat glyph back to the first.
        internal void Repeat(Features f)
        {
            if (!f.Fractions)
            {
                return;
            }
            var e = TypedEntry();
            if (e == null)
            {
                return;
            }
            if (e.Zeros < 0)
            {
                // Zeros typed at the end of the fraction become the repeat's zero places.
                var zeros = 0;
                while (e.Fraction.Count > 0 && e.Fraction[e.Fraction.Count - 1] == 0)
                {
                    e.Fraction.RemoveAt(e.Fraction.Count - 1);
                    zeros++;
                }
                e.Point = true;
                e.Zeros = zeros;
            }
            else
            {
                e.Zeros = (e.Zeros + 1) % Calculator.Glyphs.Repeat.Length;
            }
        }

        internal void Negate()
        {
            var e = Last;
            if (e != null)
            {
                e.Negative = !e.Negative;
                if (e.Empty && !e.Negative)
                {
                    items.RemoveAt(items.Count - 1);
                }
            }
            else if (AwaitsNumber)
            {
                items.Add(new Entry { Negative = true });
            }
        }

        // A constant or random value: replaces the number being typed, or starts a new one.
        private void Literal(Entry literal)
        {
            var e = Last;
            if (e != null && e.Carried)
            {
                Reset();
            }
            else if (e != null)
            {
                literal.Negative = e.Negative;
                items.RemoveAt(items.Count - 1);
            }
            else if (!AwaitsNumber)
            {
                return;
            }
            items.Add(literal);
        }

        internal void Constant(Constant c, Features f)
        {
            if (f.Allows(c))
            {
                Literal(new Entry { Constant = c });
            }
        }

        // A random value from 0 to 1, to the smallest place.
        internal void Random(Features f)
        {
            if (f.Chance)
            {
                Literal(new Entry { Literal = Num.Of(random.Next(343), 343) });
            }
        }

        internal void Operation(Op op, Features f)
        {
            // Only after a whole value: not at the start, after another operation, or after a
            // sign with no number yet.
            if (f.Allows(op) && Complete)
            {
                Uncarry();
                items.Add(op);
            }
        }

        internal void Function(Func func, Features f)
        {
            if (!f.Allows(func))
            {
                return;
            }
            if (Keys.IsPrefix(func))
            {
                if (Last != null && Last.Carried)
                {
                    Reset();
                }
                if (AwaitsNumber)
                {
                    items.Add(new Prefix { Func = func });
                }
            }
            else if (Complete)
            {
                Uncarry();
                items.Add(new Postfix { Func = func });
            }
        }

        // A carried result followed by more becomes part of the new expression.
        private void Uncarry()
        {
            if (Last != null)
            {
                Last.Carried = false;
            }
        }

        // Whether the expression ends in a whole value.
        private bool Complete => (Last != null && !Last.Empty) || LastItem is Postfix;

        // Whether each key would change the expression, mirroring the key handlers above.

        // A typed entry could be added to, or one started, as by TypedEntry.
        private bool CanType => Last == null ? AwaitsNumber : Last.Carried || Last.Typed;

        internal bool CanDigit(Features f, int maxWholeDigits)
        {
            var e = Last;
            if (e == null || e.Carried)
            {
                return CanType;
            }
            if (!e.Typed)
            {
                return false;
            }
            if (e.Zeros >= 0)
            {
                return e.Repeating.Count < Calculator.Glyphs.MaxRepeat;
            }
            if (e.Point)
            {
                return f.Fractions && e.Fraction.Count < MaxFractionDigits;
            }
            return (e.Whole.Count == 1 && e.Whole[0] == 0) || e.Whole.Count < maxWholeDigits;
        }

        internal bool CanPoint(Features f) => f.Fractions && CanType && (Last == null || Last.Carried || !Last.Point);

        internal bool CanRepeat(Features f) => f.Fractions && CanType;

        internal bool CanNegate => Last != null || AwaitsNumber;

        internal bool CanOperate(Op op, Features f) => f.Allows(op) && Complete;

        internal bool CanFunction(Func func, Features f) =>
            f.Allows(func) && (Keys.IsPrefix(func) ? (Last != null && Last.Carried) || AwaitsNumber : Complete);

        // A constant or random value, as by Literal.
        internal bool CanLiteral => Last != null || AwaitsNumber;

        internal bool CanBack => items.Count > 0;

        // Only a carried result, which [=] would leave as it is.
        internal bool OnlyCarried => items.Count == 1 && Last != null && Last.Carried;

        internal void Back()
        {
            var item = LastItem;
            if (item == null)
            {
                return;
            }
            if (!(item is Entry e))
            {
                items.RemoveAt(items.Count - 1);
                return;
            }
            if (e.Carried)
            {
                Reset();
                return;
            }
            if (!e.Typed)
            {
                items.RemoveAt(items.Count - 1);
                return;
            }
            if (e.Repeating.Count > 0)
            {
                e.Repeating.RemoveAt(e.Repeating.Count - 1);
            }
            else if (e.Zeros >= 0)
            {
                e.Zeros = -1;
            }
            else if (e.Fraction.Count > 0)
            {
                e.Fraction.RemoveAt(e.Fraction.Count - 1);
            }
            else if (e.Point)
            {
                e.Point = false;
            }
            else if (e.Whole.Count > 0)
            {
                e.Whole.RemoveAt(e.Whole.Count - 1);
            }
            else
            {
                e.Negative = false;
            }
            if (e.Empty && !e.Negative)
            {
                items.RemoveAt(items.Count - 1);
            }
        }

        internal void Clear() => Reset();

        // Whether a carried result was approximate, which makes everything after it so.
        internal bool Approximate { get; private set; }

        // Replace the expression with its result, to carry on from it.
        internal void Carry(Num value, bool approximate)
        {
            var wasApproximate = Approximate;
            Reset();
            Approximate = wasApproximate || approximate;
            items.Add(new Entry { Literal = value, Carried = true });
        }

        private void Reset()
        {
            items.Clear();
            Approximate = false;
        }

        internal readonly struct Outcome
        {
            // Null when the expression has no value, such as for division by zero.
            internal readonly Num? Value;
            // The remainder, for a single division shown with one.
            internal readonly Num? Remainder;
            // False when there is nothing to evaluate yet.
            internal readonly bool Any;

            internal Outcome(bool any, Num? value, Num? remainder = null)
            {
                Any = any;
                Value = value;
                Remainder = remainder;
            }
        }

        internal Outcome Evaluate(Features f, DivisionMode mode)
        {
            if (!Complete)
            {
                return default;
            }
            // Until fractions are unlocked, division always shows a remainder.
            var whole = !f.Fractions || mode == DivisionMode.Remainder;
            if (whole && items.Count == 3 && items[1] is Op op && op == Op.Divide && items[0] is Entry a && items[2] is Entry b)
            {
                return Num.FloorDivide(a.Value, b.Value, out var q, out var r)
                    ? new Outcome(true, q, r)
                    : new Outcome(true, null);
            }
            var parser = new Parser(items, whole);
            var v = parser.Expression();
            return new Outcome(true, parser.Done ? v : null);
        }

        // Operations follow the game's lesson on evaluation order: postfix functions apply first,
        // then every two-value operation applies from left to right with equal precedence. A
        // prefix function applies to the rest of the expression after it.
        private sealed class Parser
        {
            private readonly List<object> items;
            private readonly bool whole;
            private int pos;

            internal Parser(List<object> items, bool whole)
            {
                this.items = items;
                this.whole = whole;
            }

            internal bool Done => pos == items.Count;

            private object Peek => pos < items.Count ? items[pos] : null;

            internal Num? Expression()
            {
                if (Peek is Prefix)
                {
                    return Prefixed();
                }
                var left = Postfixed();
                while (left.HasValue && Peek is Op op)
                {
                    pos++;
                    var right = Peek is Prefix ? Prefixed() : Postfixed();
                    if (!right.HasValue)
                    {
                        return null;
                    }
                    left = Apply(op, left.Value, right.Value);
                }
                return left;
            }

            private Num? Prefixed()
            {
                var p = (Prefix)items[pos++];
                var arg = Expression();
                if (!arg.HasValue)
                {
                    return null;
                }
                return p.Func == Func.Sine ? Num.Sin(arg.Value) : Num.Cos(arg.Value);
            }

            private Num? Apply(Op op, Num left, Num right)
            {
                switch (op)
                {
                    case Op.Plus:
                        return left + right;
                    case Op.Minus:
                        return left - right;
                    case Op.Multiply:
                        return left * right;
                    case Op.Divide:
                        if (whole)
                        {
                            return Num.FloorDivide(left, right, out var q, out _) ? q : (Num?)null;
                        }
                        return Num.Divide(left, right);
                    case Op.Exponent:
                        return Num.Power(left, right);
                    default:
                        return Num.Log(left, right);
                }
            }

            private Num? Postfixed()
            {
                if (!(Peek is Entry e))
                {
                    return null;
                }
                pos++;
                Num? v = e.Value;
                while (v.HasValue && Peek is Postfix p)
                {
                    pos++;
                    v = p.Func switch
                    {
                        Func.Square => Num.Power(v.Value, Num.Of(new BigInteger(2))),
                        Func.Cube => Num.Power(v.Value, Num.Of(new BigInteger(3))),
                        Func.SquareRoot => Num.Root(v.Value, 2),
                        _ => Num.Root(v.Value, 3),
                    };
                }
                return v;
            }
        }

        // The expression's glyphs: typed numbers with places up to 7^entryPlace, other values up
        // to 7^valuePlace, beyond which they use the scientific form if allowed.
        internal List<string> Glyphs(int entryPlace, int valuePlace, bool scientific)
        {
            var list = new List<string>();
            foreach (var item in items)
            {
                switch (item)
                {
                    case Op op:
                        list.Add(Keys.Word(op));
                        break;
                    case Prefix p:
                        list.Add(Keys.Word(p.Func));
                        list.Add(Keys.On);
                        break;
                    case Postfix p:
                        list.Add(Keys.Word(p.Func));
                        break;
                    case Entry e:
                        list.AddRange(EntryGlyphs(e, entryPlace, valuePlace, scientific));
                        break;
                }
            }
            return list;
        }

        private static IEnumerable<string> EntryGlyphs(Entry e, int entryPlace, int valuePlace, bool scientific)
        {
            var list = new List<string>();
            if (e.Constant.HasValue)
            {
                if (e.Negative)
                {
                    list.Add(Keys.Negative);
                }
                list.Add(Keys.Word(e.Constant.Value));
                return list;
            }
            if (e.Literal.HasValue)
            {
                return Calculator.Glyphs.Write(e.Value, out _, valuePlace, scientific) ?? new List<string> { Keys.NoValue };
            }
            if (e.Negative)
            {
                list.Add(Keys.Negative);
            }
            if (e.Zeros >= 0)
            {
                // Written as typed, before the repeating digits are simplified, so that what
                // was typed stays visible.
                BigInteger n = 0;
                foreach (var digit in e.Whole.Concat(e.Fraction))
                {
                    n = n * 7 + digit;
                }
                var start = Calculator.Glyphs.Write(Num.Of(n, BigInteger.Pow(7, e.Fraction.Count)), out _, entryPlace, scientific);
                if (start != null)
                {
                    list.AddRange(start);
                }
                list.Add(Calculator.Glyphs.Repeat[e.Zeros]);
                list.AddRange(e.Repeating.Select(d => d.ToString()));
                return list;
            }
            if (e.Whole.Count > 0 || e.Point)
            {
                var v = e.Negative ? e.Value.Negate() : e.Value;
                var written = Calculator.Glyphs.Write(v, out _, entryPlace, scientific);
                if (written != null)
                {
                    list.AddRange(written);
                }
            }
            if (e.Point && e.Fraction.All(d => d == 0))
            {
                // The point has no glyph in the game's numbers, and zero digits after it show
                // nothing, so mark that the next digits go after the point.
                list.Add(Keys.Point);
            }
            return list;
        }
    }
}
