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
            Op.Scientific => Scientific,
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
            // Zeros typed at the end of the fraction that starting the repeat turned into zero
            // places, given back if the point key cycles back to a plain fraction.
            internal int Moved;
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

            internal Entry Clone()
            {
                var e = new Entry { Negative = Negative, Point = Point, Zeros = Zeros, Moved = Moved, Literal = Literal, Constant = Constant, Carried = Carried };
                e.Whole.AddRange(Whole);
                e.Fraction.AddRange(Fraction);
                e.Repeating.AddRange(Repeating);
                return e;
            }

            internal string Signature()
            {
                var lit = Literal.HasValue ? (Literal.Value.Exact ? Literal.Value.N + "/" + Literal.Value.D : Literal.Value.F.ToString("R")) : "";
                return $"{Negative}|{string.Concat(Whole)}|{Point}|{string.Concat(Fraction)}|{Zeros}|{Moved}|{string.Concat(Repeating)}|{lit}|{Constant}|{Carried}";
            }
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
        private List<object> items = new List<object>();

        // The longest expression, so that evaluating and drawing it stays cheap.
        private const int MaxItems = 100;
        private const int MaxHistory = 200;

        // The state before each key press that changed the expression, for the back key.
        private readonly List<(List<object> Items, bool Approximate)> history = new List<(List<object>, bool)>();

        private List<object> CloneItems() =>
            items.Select(i => i is Entry e ? e.Clone() : i is Prefix p ? new Prefix { Func = p.Func } : i is Postfix q ? new Postfix { Func = q.Func } : i).ToList();

        private string Signature() =>
            Approximate + ";" + string.Join(";", items.Select(i => i is Entry e ? "N" + e.Signature() : i is Prefix p ? "P" + p.Func : i is Postfix q ? "Q" + q.Func : "O" + i));

        // Runs a key press, and records the state before it if it changed anything. A press that
        // would make the expression too long is undone.
        internal void Record(Action press)
        {
            var before = (CloneItems(), Approximate);
            var signature = Signature();
            undid = false;
            press();
            if (undid)
            {
                return;
            }
            if (items.Count > MaxItems)
            {
                (items, Approximate) = before;
                return;
            }
            if (Signature() != signature)
            {
                history.Add(before);
                if (history.Count > MaxHistory)
                {
                    history.RemoveAt(0);
                }
            }
        }

        // The back key: returns to the state before the last key press that changed anything.
        internal void Back()
        {
            if (history.Count > 0)
            {
                (items, Approximate) = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                undid = true;
            }
        }

        // Set by Back, so that Record does not record the undo itself.
        private bool undid;

        // Forgets the expression and its history, as for another save slot.
        internal void Forget()
        {
            Reset();
            history.Clear();
        }

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

        // Starts the digits after the point. Pressed again, starts the repeating digits, then
        // moves them one more zero place along for each press, and after the last repeat glyph
        // returns to a plain fraction, unless repeating digits have been typed.
        internal void Point(Features f)
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
            if (!e.Point)
            {
                e.Point = true;
            }
            else if (e.Zeros < 0)
            {
                // Zeros typed at the end of the fraction become the repeat's zero places.
                var zeros = 0;
                while (e.Fraction.Count > 0 && e.Fraction[e.Fraction.Count - 1] == 0)
                {
                    e.Fraction.RemoveAt(e.Fraction.Count - 1);
                    zeros++;
                }
                e.Zeros = zeros;
                e.Moved = zeros;
            }
            else if (e.Zeros + 1 < Calculator.Glyphs.Repeat.Length)
            {
                e.Zeros++;
            }
            else if (e.Repeating.Count > 0)
            {
                e.Zeros = 0;
            }
            else
            {
                e.Zeros = -1;
                e.Fraction.AddRange(Enumerable.Repeat(0, e.Moved));
                e.Moved = 0;
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
            if (CanOperate(op, f))
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
                if (AwaitsNumber && !AfterScientific)
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

        internal bool CanPoint(Features f) => f.Fractions && CanType;

        internal bool CanNegate => Last != null || AwaitsNumber;

        // The scientific form joins numbers, so it needs a number right before it.
        internal bool CanOperate(Op op, Features f) =>
            f.Allows(op) && Complete && (op != Op.Scientific || LastItem is Entry);

        // The number after the scientific form, which a function cannot start.
        private bool AfterScientific => LastItem is Op op && op == Op.Scientific;

        internal bool CanFunction(Func func, Features f) =>
            f.Allows(func) && (Keys.IsPrefix(func) ? (Last != null && Last.Carried) || (AwaitsNumber && !AfterScientific) : Complete);

        // A constant or random value, as by Literal.
        internal bool CanLiteral => Last != null || AwaitsNumber;

        internal bool CanBack => history.Count > 0;

        internal bool CanClear => items.Count > 0 || Approximate;

        // Only a carried result, which [=] would leave as it is.
        internal bool OnlyCarried => items.Count == 1 && Last != null && Last.Carried;

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
            // A division inside a longer expression dropped a remainder, so the value is not
            // exactly what the expression says.
            internal readonly bool Dropped;

            internal Outcome(bool any, Num? value, Num? remainder = null, bool dropped = false)
            {
                Any = any;
                Value = value;
                Remainder = remainder;
                Dropped = dropped;
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
            var parser = new Parser(items, whole);
            var v = parser.Expression();
            if (!v.HasValue)
            {
                return new Outcome(true, null);
            }
            // A single division, of values that may be joined by E or followed by functions,
            // shows its remainder.
            if (whole && parser.Ops == 1 && parser.LastOp == Op.Divide && !parser.Prefixed)
            {
                return new Outcome(true, v, parser.LastRemainder);
            }
            return new Outcome(true, v, dropped: parser.Dropped);
        }

        // The result row, and the value [=] would carry with whether carrying it is approximate.
        // The value is null when there is none to carry, or when it would be approximate before
        // the approximately-equal sign is unlocked. A remainder of zero is not shown. width is the
        // row's width in cells.
        internal List<string> ResultRow(Features f, DivisionMode mode, int place, int width, out Num? carry, out bool approximate)
        {
            carry = null;
            approximate = false;
            var list = new List<string>();
            var outcome = Evaluate(f, mode);
            if (!outcome.Any)
            {
                return list;
            }
            var exact = false;
            var value = outcome.Value.HasValue ? Calculator.Glyphs.Write(outcome.Value.Value, out exact, place, f.Scientific, width - 1) : null;
            List<string> remainder = null;
            var remainderExact = true;
            var hasRemainder = outcome.Remainder is Num r && !r.IsZero;
            if (value != null && hasRemainder)
            {
                remainder = Calculator.Glyphs.Write(outcome.Remainder.Value, out remainderExact, place, f.Scientific);
                if (remainder != null && value.Count + 2 + remainder.Count > width)
                {
                    remainder = null;
                }
            }
            // What the row shows is approximate after an approximate carry, for a rounded value,
            // for a remainder that is rounded or does not fit, or when a remainder was dropped.
            var shownApproximate = Approximate || !exact || outcome.Dropped || (hasRemainder && (remainder == null || !remainderExact));
            if (value == null || (shownApproximate && !f.Approximate))
            {
                list.Add(Keys.Equal);
                list.Add(Keys.NoValue);
                return list;
            }
            list.Add(shownApproximate ? Keys.Approximately : Keys.Equal);
            list.AddRange(value);
            if (remainder != null)
            {
                list.Add(Keys.Remainder);
                list.AddRange(remainder);
            }
            // Carrying the quotient alone drops the remainder.
            approximate = shownApproximate || hasRemainder;
            if (!approximate || f.Approximate)
            {
                // The value as shown: a double within rounding error of a simple fraction is
                // carried as that fraction.
                carry = Calculator.Glyphs.Snap(outcome.Value.Value);
            }
            return list;
        }

        // Operations follow the game's lesson on evaluation order: postfix functions apply first,
        // then every two-value operation applies from left to right with equal precedence. A
        // prefix function applies to the rest of the expression after it.
        private sealed class Parser
        {
            private readonly List<object> items;
            private readonly bool whole;
            private int pos;

            // The two-value operations applied, other than E, and the last of them.
            internal int Ops;
            internal Op LastOp;
            internal bool Prefixed;
            // The remainder of the last division, and whether any division dropped one.
            internal Num? LastRemainder;
            internal bool Dropped;

            internal Parser(List<object> items, bool whole)
            {
                this.items = items;
                this.whole = whole;
            }

            private object Peek => pos < items.Count ? items[pos] : null;

            // An operation that gave NaN, such as 0 times infinity, has no value.
            private static Num? Checked(Num? v) => v.HasValue && !v.Value.Exact && double.IsNaN(v.Value.F) ? null : v;

            internal Num? Expression()
            {
                if (Peek is Prefix)
                {
                    return Prefix();
                }
                var left = Postfixed();
                while (left.HasValue && Peek is Op op)
                {
                    pos++;
                    var right = Peek is Prefix ? Prefix() : Postfixed();
                    if (!right.HasValue)
                    {
                        return null;
                    }
                    Ops++;
                    LastOp = op;
                    left = Checked(Apply(op, left.Value, right.Value));
                }
                return left;
            }

            private Num? Prefix()
            {
                Prefixed = true;
                var p = (Prefix)items[pos++];
                var arg = Expression();
                if (!arg.HasValue)
                {
                    return null;
                }
                return Checked(p.Func == Func.Sine ? Num.Sin(arg.Value) : Num.Cos(arg.Value));
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
                            if (!Num.FloorDivide(left, right, out var q, out var r))
                            {
                                return null;
                            }
                            LastRemainder = r;
                            Dropped |= !r.IsZero;
                            return q;
                        }
                        return Num.Divide(left, right);
                    case Op.Exponent:
                        return Num.Power(left, right);
                    case Op.Scientific:
                        var scale = Num.Power(Num.Of(new BigInteger(7)), right);
                        return scale.HasValue ? left * scale.Value : (Num?)null;
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
                // The scientific form joins two numbers into one, before any function or
                // other operation applies.
                while (v.HasValue && Peek is Op op && op == Op.Scientific)
                {
                    pos++;
                    if (!(Peek is Entry power))
                    {
                        return null;
                    }
                    pos++;
                    v = Checked(Apply(Op.Scientific, v.Value, power.Value));
                }
                while (v.HasValue && Peek is Postfix p)
                {
                    pos++;
                    v = Checked(p.Func switch
                    {
                        Func.Square => Num.Power(v.Value, Num.Of(new BigInteger(2))),
                        Func.Cube => Num.Power(v.Value, Num.Of(new BigInteger(3))),
                        Func.SquareRoot => Num.Root(v.Value, 2),
                        _ => Num.Root(v.Value, 3),
                    });
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
