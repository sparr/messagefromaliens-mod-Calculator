using System.Reflection;
using Calculator;

// Unlock messages for Mod Settings, one per gate: the gate's token, then the words for what the
// gate opens and where. Mod Settings builds the sentences around them, in the chapter's
// explanation and on the page shown when the chapter's wins reach the gate's count. Each phrase
// fits in one left panel row after its explanation prefix, except the scientific one.
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Open + " " + Keys.Title)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.MultiplyDivide + " multiply and divide " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Fractions + " " + Keys.Point + " " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Squares + " exponent2 and exponent3 " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.PowerLog + " exponent and log " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Scientific + " squareroot and " + Keys.Approximately + " and exp7 " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Pi + " pi " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.CubeRoot + " root3 " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Trig + " sin and cos " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.E + " e " + Gates.FromCalculator)]
[assembly: AssemblyMetadata(Gates.UnlockKey, Gates.Chance + " " + Keys.Chance + " " + Gates.FromCalculator)]

namespace Calculator
{
    // When each feature unlocks, as gate tokens like those the game's menu lines start with. A
    // token that names a chapter's quizzes and ends in a whole number n passes once n of the
    // chapter's quizzes are won, whichever they are, as Global.GetWin counts them. n is the
    // number of the quiz that first tests the feature, so a player who wins the quizzes in order
    // unlocks it with that quiz. Each setting's Mod Settings gate is its feature's token, so it
    // is listed exactly when the feature unlocks.
    internal static class Gates
    {
        internal const string UnlockKey = "MessageFromAliens.Unlock";

        // What follows a chapter's name in its quiz names, before the quiz number.
        private const string QuizInfix = "-riddle-";
        private const string Arithmetic = "[arithmetic" + QuizInfix;
        private const string Geometry = "[geometry" + QuizInfix;
        private const string Advanced = "[advanced-arithmetic" + QuizInfix;

        // The calculator, with the digits, the sign key, addition, and subtraction.
        internal const string Open = Arithmetic + "2]";
        // Multiplication, and division with a remainder.
        internal const string MultiplyDivide = Arithmetic + "3]";
        // Digits after the point, repeating digits, fraction results, and the division setting.
        internal const string Fractions = Arithmetic + "4]";
        internal const string Squares = Arithmetic + "9]";
        internal const string PowerLog = Arithmetic + "10]";
        // Square roots, approximate results, the scientific form and its key, and the
        // significant-figures setting.
        internal const string Scientific = Arithmetic + "11]";
        internal const string Pi = Geometry + "1]";
        internal const string CubeRoot = Geometry + "7]";
        // Sine and cosine.
        internal const string Trig = Advanced + "1]";
        internal const string E = Advanced + "2]";
        internal const string Chance = Advanced + "4]";

        // "in" the calculator, as the game's own unlock lines name where a feature is.
        internal const string FromCalculator = "in " + Keys.Title;

        // Whether a gate token passes, as the game checks a menu line's gate.
        internal static bool Passed(string gate) =>
            Global.savedata?.win != null && Global.GetWin(gate.Substring(1, gate.Length - 2));
    }
}
