namespace Calculator
{
    // Scientific multiplies by 7 to the power of its right side, as in the game's scientific form.
    internal enum Op { Plus, Minus, Multiply, Divide, Exponent, Log, Scientific }

    // Functions of one value: postfix ones follow it, as in "4 squareroot", and prefix ones come
    // before it with "on", as the game writes them.
    internal enum Func { Square, Cube, SquareRoot, CubeRoot, Sine, Cosine }

    internal enum Constant { E, Pi }

    // How division shows a result that is not a whole number.
    public enum DivisionMode { Fraction, Remainder }

    // The game words the calculator draws.
    internal static class Keys
    {
        // The left panel's menu button that opens and closes the calculator.
        internal const string Button = "calculate";
        // The calculator's title in the left panel, the same as the mod's name.
        internal const string Title = "number calculate";

        internal const string Point = "smallnumber";
        // The repeat key, which cycles through the repeat glyphs.
        internal static string Repeat => Glyphs.Repeat[0];
        internal const string Negative = "negative";
        internal const string Back = "anti";
        internal const string Clear = "erase";
        internal const string Equal = "equal";
        internal const string Approximately = "mostlyequal";
        internal const string Remainder = "remain";
        // Shown as the result when there is none, such as for division by zero.
        internal const string NoValue = "inexist";
        // Joins a prefix function to its value.
        internal const string On = "on";
        // Puts a random value from 0 to 1 in the expression.
        internal const string Chance = "chance";
        // The significant-figures counter's label, then its count.
        internal const string Figures = "num amount";

        internal static string Word(Op op) => op switch
        {
            Op.Plus => "plus",
            Op.Minus => "minus",
            Op.Multiply => "multiply",
            Op.Divide => "divide",
            Op.Exponent => "exponent",
            Op.Log => "log",
            _ => "exp7",
        };

        internal static string Word(Func f) => f switch
        {
            Func.Square => "exponent2",
            Func.Cube => "exponent3",
            Func.SquareRoot => "squareroot",
            Func.CubeRoot => "root3",
            Func.Sine => "sin",
            _ => "cos",
        };

        internal static bool IsPrefix(Func f) => f == Func.Sine || f == Func.Cosine;

        internal static string Word(Constant c) => c == Constant.E ? "e" : "pi";

        internal static string Word(DivisionMode m) => m == DivisionMode.Fraction ? Point : Remainder;

        internal static bool TryParse<T>(string word, System.Func<T, string> name, out T value) where T : System.Enum
        {
            foreach (T v in System.Enum.GetValues(typeof(T)))
            {
                if (name(v) == word)
                {
                    value = v;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }
}
