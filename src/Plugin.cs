using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// The mod's name in glyphs, as game word IDs, for Mod Settings and any other mod that lists mods.
[assembly: AssemblyMetadata("MessageFromAliens.Name", "number calculate")]

namespace Calculator
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        // The English name stands in for the glyph name above where glyphs cannot be shown.
        public const string Guid = "sparr.messagefromaliens.calculator";
        public const string Name = "Calculator";
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Figures;
        internal static ConfigEntry<DivisionMode> Division;

        private bool failed;

        private void Awake()
        {
            Log = Logger;

            // Each setting is listed in Mod Settings once the quiz that unlocks what it is for has
            // been won, the same quiz the calculator checks.
            Enabled = Config.Bind("Calculator", "Enabled", true,
                Described("Show the calculator's button in the left panel's menu, once it is unlocked.", "calculate de button",
                    gate: QuizGate(Panel.Quiz2)));

            Figures = Config.Bind("Calculator", "SignificantFigures", 4,
                Described("Results of 7 to this power and above, which have one more whole-number place than this, are written in scientific form. Places the game has not shown yet are never used.",
                    "num amount", new AcceptableValueList<int>(1, 2, 3, 4, 5, 6),
                    Enumerable.Range(1, 6).ToDictionary(i => i.ToString(), i => i.ToString()), QuizGate(Panel.Quiz11)));
            Division = Config.Bind("Calculator", "Division", DivisionMode.Fraction,
                Described("Once fractions are unlocked, show division results as fractions or as a whole number and a remainder.",
                    "divide", null,
                    new Dictionary<string, string> { ["Fraction"] = Keys.Point, ["Remainder"] = Keys.Remainder }, QuizGate(Panel.Quiz4)));

            // Redraw the current page, so a change from Mod Settings shows at once.
            Config.SettingChanged += (_, _) => Global.manager?.UpdateText();

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void LateUpdate()
        {
            try
            {
                Panel.Tick();
            }
            catch (Exception e)
            {
                if (!failed)
                {
                    failed = true;
                    Log.LogError($"LateUpdate failed: {e}");
                }
            }
        }

        // Mod Settings shows a setting tagged with its name in glyphs, as game word IDs.
        // gate: a gate token, as the game's menu lines start with, so Mod Settings shows the setting
        // only once it passes.
        private static ConfigDescription Described(string description, string glyphs,
            AcceptableValueBase values = null, Dictionary<string, string> valueGlyphs = null, string gate = null)
        {
            var tags = new Dictionary<string, string> { ["MessageFromAliens.Label"] = glyphs };
            if (gate != null)
            {
                tags["MessageFromAliens.Gate"] = gate;
            }
            if (valueGlyphs != null)
            {
                foreach (var pair in valueGlyphs)
                {
                    tags["MessageFromAliens.Value." + pair.Key] = pair.Value;
                }
            }
            return new ConfigDescription(description, values, tags);
        }

        // The gate token that passes once this quiz is won. Global.GetWin reads a gate whose quiz
        // number is a whole number, such as 4, as a count: it passes after
        // that many of the chapter's quizzes, whichever they are. A quiz number below 7 is written
        // instead as its digit at place 0, which GetWin cannot count with and turns back into the
        // quiz's own name. GetWin already checks numbers from 7 up, such as "1e1-4", exactly.
        private static string QuizGate(string quiz)
        {
            const string marker = "riddle-";
            var number = quiz.Substring(quiz.IndexOf(marker) + marker.Length);
            return "[" + quiz + (int.TryParse(number, out _) ? "e0" : "") + "]";
        }
    }
}
