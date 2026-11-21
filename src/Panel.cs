using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Calculator
{
    // Shows the calculator in the left panel in place of the panel's history, from a button at
    // the right end of the panel's menu. The calculator is not a history entry: while it is
    // open, the panel's history buttons go back to the entry it covers or on to the next one,
    // and anything the game adds to the history closes it. The expression stays as it was while
    // the calculator is closed.
    internal static class Panel
    {
        private const string Gap = ".";
        private const string Newline = " newline ";
        // Glyph cells in one row of the left panel.
        private const int RowWidth = 12;
        // The calculator's rows: the expression, its result, then keys.
        private const int ExpressionRow = 0;
        private const int ResultRow = 1;
        private const int DigitRow = 2;
        private const int OperationRow = 3;
        private const int FunctionRow = 4;
        private const int ConstantRow = 5;
        private const int SettingRow = 6;
        private const int ControlRow = 7;
        private const int FirstKeyRow = DigitRow;
        private const int MaxFigures = 6;
        // The panel menu's history buttons, and its blank page-button slots.
        private const string Early = "early";
        private const string Late = "late";
        private const string BlankPages = ". .";

        private static readonly Calc calc = new Calc();
        private static bool open;
        // The history entry under the calculator, its text, and the page it was on.
        private static int savedEntry = -1;
        private static string savedText;
        private static int savedPage;

        private static readonly Func<Draw, bool> IsEditNote =
            AccessTools.MethodDelegate<Func<Draw, bool>>(AccessTools.Method(typeof(Draw), "IsEditNote"));

        private static Draw Manager => Global.manager;

        // Unlocks and the seen place are checked many times a redraw, and some checks run every
        // frame, so each is cached for a frame. The keys also hold what a change within the frame
        // would change: the save, and its count of wins or of dictionary words, which only grow.
        private static Features unlocked;
        private static (int Frame, object Save, int Wins) unlockedKey;
        private static int seenPlace;
        private static (int Frame, object Dict, int Words) seenPlaceKey;

        internal static Features Unlocked()
        {
            var save = Global.savedata;
            var key = (Time.frameCount, (object)save, save?.win?.Count ?? -1);
            if (unlocked == null || key != unlockedKey)
            {
                unlocked = Gated();
                unlockedKey = key;
            }
            return unlocked;
        }

        private static Features Gated() => new Features
        {
            Open = Gates.Passed(Gates.Open),
            MultiplyDivide = Gates.Passed(Gates.MultiplyDivide),
            Fractions = Gates.Passed(Gates.Fractions),
            Squares = Gates.Passed(Gates.Squares),
            PowerLog = Gates.Passed(Gates.PowerLog),
            SquareRoot = Gates.Passed(Gates.Scientific),
            Scientific = Gates.Passed(Gates.Scientific),
            Approximate = Gates.Passed(Gates.Scientific),
            Pi = Gates.Passed(Gates.Pi),
            CubeRoot = Gates.Passed(Gates.CubeRoot),
            Trig = Gates.Passed(Gates.Trig),
            E = Gates.Passed(Gates.E),
            Chance = Gates.Passed(Gates.Chance),
        };

        // The largest place the player has seen a digit in, as the dictionary records every
        // glyph shown. Higher places would show glyphs the game has not.
        private static int SeenPlace()
        {
            var dict = Global.savedata?.dict;
            var key = (Time.frameCount, (object)dict, dict?.Count ?? -1);
            if (key != seenPlaceKey)
            {
                seenPlace = FindSeenPlace(dict);
                seenPlaceKey = key;
            }
            return seenPlace;
        }

        private static int FindSeenPlace(Dictionary<string, string> dict)
        {
            var seen = 0;
            if (dict == null)
            {
                return seen;
            }
            foreach (var word in dict.Keys)
            {
                if (word.Length >= 3 && word[0] >= '1' && word[0] <= '6' && word[1] == 'e' && int.TryParse(word.Substring(2), out var place) && place > seen)
                {
                    seen = place;
                }
            }
            return seen;
        }

        // Results use places up to the largest seen place, or once the scientific form is
        // unlocked, up to the significant-figures setting. Typed numbers use the seen places.
        private static int ValuePlace(Features f) =>
            f.Scientific ? Math.Min(Plugin.Figures.Value, SeenPlace() + 1) - 1 : SeenPlace();

        // The significant-figures counter's next value. It cycles only through the values that
        // make a difference, up to one above the largest seen place.
        private static int NextFigures()
        {
            var top = Math.Min(MaxFigures, SeenPlace() + 1);
            return Plugin.Figures.Value >= top ? 1 : Plugin.Figures.Value + 1;
        }

        private static DivisionMode Mode(Features f) => f.Fractions ? Plugin.Division.Value : DivisionMode.Remainder;

        // Whether the button belongs in the menu: unlocked, and the panel is not editing a note.
        private static bool Available(Draw d) =>
            Plugin.Enabled.Value && Unlocked().Open && d != null && !IsEditNote(d);

        private static bool Showing(Draw d) => open && Available(d);

        private static void Open(Draw d)
        {
            open = true;
            savedEntry = d.currenttext;
            savedText = savedEntry >= 0 && savedEntry < d.text_history.Count ? d.text_history[savedEntry] : null;
            savedPage = savedEntry >= 0 && savedEntry < d.currentleftpage.Count ? d.currentleftpage[savedEntry] : 0;
            d.UpdateText();
        }

        // Back to the history entry, on the page it was on; drawing the calculator's single page
        // resets the entry's page. The note editor replaces its entry's text when it opens a
        // note, and that note keeps the page the game gave it.
        private static void Close(Draw d)
        {
            if (!open)
            {
                return;
            }
            open = false;
            if (d != null && savedEntry >= 0 && savedEntry < d.currentleftpage.Count
                && savedEntry < d.text_history.Count && d.text_history[savedEntry] == savedText)
            {
                d.currentleftpage[savedEntry] = savedPage;
            }
            savedEntry = -1;
            savedText = null;
            d?.UpdateText();
        }

        // While the calculator is open: no page buttons, and the history buttons lead back to the
        // entry under the calculator and the one after it.
        private static string OpenMenu(Draw d)
        {
            var count = d.text_history.Count;
            var early = d.currenttext >= 0 && d.currenttext < count ? Early : Gap;
            var late = d.currenttext + 1 < count ? Late : Gap;
            return BlankPages + " " + early + " " + late;
        }

        private static string Row(IEnumerable<string> glyphs)
        {
            var row = string.Join(" ", glyphs);
            return row == "" ? Gap : row;
        }

        private static string Spaced(IEnumerable<string> keys) => string.Join(" " + Gap + " ", keys);

        // The result the calculator shows, from its last redraw, for the result key's underline
        // and cursor; null for none.
        private static Num? shownCarry;

        private static string Body(out Num? carry)
        {
            var f = Unlocked();
            // The newest part of a long expression, leaving the last cell for the sign that the
            // expression is approximate.
            var width = calc.Approximate ? RowWidth - 1 : RowWidth;
            var expression = calc.ExpressionRow(SeenPlace(), ValuePlace(f), f.Scientific, width);
            if (calc.Approximate)
            {
                expression.AddRange(Enumerable.Repeat(Gap, width - expression.Count));
                expression.Add(Keys.Approximately);
            }
            var rows = new string[ControlRow + 1];
            rows[ExpressionRow] = Row(expression);
            rows[ResultRow] = Row(Result(f, out carry, out _));

            var digits = string.Join(" ", Enumerable.Range(0, 7).Select(i => i.ToString()));
            var extra = new List<string>();
            if (f.Fractions)
            {
                extra.Add(Keys.Point);
            }
            extra.Add(Keys.Negative);
            if (f.Allows(Op.Scientific))
            {
                extra.Add(Keys.Word(Op.Scientific));
            }
            // The other number keys sit side by side so the row fits in the panel's width.
            rows[DigitRow] = digits + " " + Gap + " " + string.Join(" ", extra);
            // The scientific operator builds numbers, so it sits on the digit row.
            rows[OperationRow] = Spaced(Values<Op>().Where(op => op != Op.Scientific && f.Allows(op)).Select(Keys.Word));
            rows[FunctionRow] = Spaced(Values<Func>().Where(f.Allows).Select(Keys.Word));
            var constants = Values<Constant>().Where(f.Allows).Select(Keys.Word).ToList();
            if (f.Chance)
            {
                constants.Add(Keys.Chance);
            }
            rows[ConstantRow] = Spaced(constants);
            var settings = new List<string>();
            if (f.Fractions)
            {
                settings.Add(Keys.Word(Op.Divide) + " " + Gap + " " + Keys.Word(DivisionMode.Fraction) + " " + Keys.Word(DivisionMode.Remainder));
            }
            if (f.Scientific)
            {
                settings.Add(Keys.Figures + " " + Plugin.Figures.Value);
            }
            rows[SettingRow] = Spaced(settings);
            rows[ControlRow] = Spaced(new[] { Keys.Back, Keys.Clear, Keys.Equal });
            return string.Join(Newline, rows.Select(r => string.IsNullOrEmpty(r) ? Gap : r));
        }

        private static IEnumerable<T> Values<T>() where T : Enum => Enum.GetValues(typeof(T)).Cast<T>();

        private static List<string> Result(Features f, out Num? carry, out bool approximate) =>
            calc.ResultRow(f, Mode(f), ValuePlace(f), RowWidth, out carry, out approximate);

        // [=]: carry the result into a new expression. Once anything carried is approximate, so
        // is everything after it, until the expression is cleared.
        private static void Equal(Features f)
        {
            Result(f, out var carry, out var approximate);
            if (carry.HasValue)
            {
                calc.Carry(carry.Value, approximate);
            }
        }

        private static void Press(Draw d, string word, int line)
        {
            var handled = true;
            calc.Record(() => handled = Apply(word, line));
            if (handled)
            {
                d.UpdateText();
            }
        }

        // Applies a key to the expression or the settings; false for a word that is no key.
        private static bool Apply(string word, int line)
        {
            var f = Unlocked();
            if (line == DigitRow && word.Length == 1 && word[0] >= '0' && word[0] <= '6')
            {
                calc.Digit(word[0] - '0', f, SeenPlace() + 1);
            }
            else if (line == DigitRow && word == Keys.Point)
            {
                calc.Point(f);
            }
            else if (line == SettingRow)
            {
                if (Keys.TryParse<DivisionMode>(word, Keys.Word, out var mode))
                {
                    if (f.Fractions)
                    {
                        Plugin.Division.Value = mode;
                    }
                }
                else if (word != Keys.Word(Op.Divide))
                {
                    // The counter and its label cycle the significant figures.
                    Plugin.Figures.Value = NextFigures();
                }
            }
            else if (word == Keys.Negative)
            {
                calc.Negate();
            }
            else if (Keys.TryParse<Op>(word, Keys.Word, out var op))
            {
                calc.Operation(op, f);
            }
            else if (Keys.TryParse<Func>(word, Keys.Word, out var func))
            {
                calc.Function(func, f);
            }
            else if (Keys.TryParse<Constant>(word, Keys.Word, out var constant))
            {
                calc.Constant(constant, f);
            }
            else if (word == Keys.Chance)
            {
                calc.Random(f);
            }
            else if (word == Keys.Back)
            {
                calc.Back();
            }
            else if (word == Keys.Clear)
            {
                calc.Clear();
            }
            else if (word == Keys.Equal)
            {
                Equal(f);
            }
            else
            {
                return false;
            }
            return true;
        }

        // Whether pressing a key would do anything, mirroring Press. carry is the expression's
        // result, so it is evaluated once per redraw.
        private static bool Pressable(string word, int line, Features f, Num? carry)
        {
            if (line == DigitRow && word.Length == 1 && word[0] >= '0' && word[0] <= '6')
            {
                return calc.CanDigit(f, SeenPlace() + 1);
            }
            if (line == DigitRow && word == Keys.Point)
            {
                return calc.CanPoint(f);
            }
            if (line == SettingRow)
            {
                if (Keys.TryParse<DivisionMode>(word, Keys.Word, out var mode))
                {
                    return f.Fractions && mode != Plugin.Division.Value;
                }
                return word != Keys.Word(Op.Divide) && f.Scientific && NextFigures() != Plugin.Figures.Value;
            }
            if (word == Keys.Negative)
            {
                return calc.CanNegate;
            }
            if (Keys.TryParse<Op>(word, Keys.Word, out var op))
            {
                return calc.CanOperate(op, f);
            }
            if (Keys.TryParse<Func>(word, Keys.Word, out var func))
            {
                return calc.CanFunction(func, f);
            }
            if (Keys.TryParse<Constant>(word, Keys.Word, out var constant))
            {
                return f.Allows(constant) && calc.CanLiteral;
            }
            if (word == Keys.Chance)
            {
                return f.Chance && calc.CanLiteral;
            }
            if (word == Keys.Back)
            {
                return calc.CanBack;
            }
            if (word == Keys.Clear)
            {
                return calc.CanClear;
            }
            if (word == Keys.Equal)
            {
                return carry.HasValue && !calc.OnlyCarried;
            }
            return false;
        }

        // The division mode's current choice is boxed, like a choice on the game's settings page,
        // and keys that would do anything are underlined, like the game's menu choices. Each
        // key's underline stops short of the next glyph, except within the significant-figures
        // label and count, which act as one key and share one underline.
        private static void Layout(Draw d)
        {
            if (!Showing(d) || d.alien_left_text == null)
            {
                return;
            }
            var f = Unlocked();
            var current = Keys.Word(Plugin.Division.Value);
            var glyphs = d.alien_left_text
                .Where(a => a != null && a.gameObject.activeSelf && a.line >= FirstKeyRow && a.alien_txt != "")
                .ToList();
            bool InFigures(AlienCharacter a) =>
                a != null && a.line == SettingRow && a.alien_txt != Keys.Word(Op.Divide) &&
                !Keys.TryParse<DivisionMode>(a.alien_txt, Keys.Word, out _);
            AlienCharacter At(int line, int x) => glyphs.FirstOrDefault(a => a.line == line && a.line_x == x);
            foreach (var glyph in glyphs)
            {
                var position = glyph.sp.transform.position;
                if (f.Fractions && glyph.line == SettingRow && glyph.alien_txt == current)
                {
                    d.selected.Add(d.DrawRectRenderer(1f, 1f, position, 2));
                }
                if (!Pressable(glyph.alien_txt, glyph.line, f, shownCarry))
                {
                    continue;
                }
                var joined = InFigures(glyph);
                var left = joined && InFigures(At(glyph.line, glyph.line_x - 1)) ? -0.5f : -0.45f;
                var right = joined && InFigures(At(glyph.line, glyph.line_x + 1)) ? 0.5f : 0.45f;
                d.selected.Add(d.DrawLineRenderer(position + new Vector3(left, -0.92f), position + new Vector3(right, -0.92f), 3));
            }
        }

        [HarmonyPatch(typeof(Draw), nameof(Draw.UpdateText1))]
        private static class UpdateText1Patch
        {
            // Before other mods' prefixes, so the redraw and everything in it see the history
            // entry a history button led to.
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Draw __instance)
            {
                drawing = true;
                if (historyTarget >= 0)
                {
                    __instance.currenttext = historyTarget;
                    historyTarget = -1;
                }
            }

            private static void Finalizer() => drawing = false;

            private static void Postfix(Draw __instance)
            {
                try
                {
                    Layout(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"UpdateText1 postfix failed: {e}");
                }
            }
        }

        private static bool IsButton(AlienCharacter glyph) =>
            glyph != null && glyph.sector == "leftmenu" && glyph.alien_txt == Keys.Button;

        // A calculator key that would do anything now, as underlined.
        private static bool IsKey(Draw d, AlienCharacter glyph) =>
            glyph != null && Showing(d) && glyph.sector == "lefttext" && glyph.line >= FirstKeyRow && glyph.alien_txt != ""
            && Pressable(glyph.alien_txt, glyph.line, Unlocked(), shownCarry);

        private static AlienCharacter HitGlyph()
        {
            var cam = Camera.main;
            if (cam == null || !Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit))
            {
                return null;
            }
            return hit.transform.gameObject.GetComponentInParent<AlienCharacter>();
        }

        // Keys typed while the calculator is open and no text field has focus, as presses of the
        // calculator's keys. Minus is the sign key where a number may start, and the subtraction
        // key elsewhere. Backspace deletes the last key pressed.
        private static void Type(Draw d)
        {
            var typed = Input.inputString;
            // A field whose edit Enter ended earlier this frame no longer has focus, but its keys
            // were typed into it.
            if (string.IsNullOrEmpty(typed) || TextFieldFocused() || focusedLastFrame)
            {
                return;
            }
            var any = false;
            foreach (var c in typed)
            {
                calc.Record(() => any |= TypeKey(c));
            }
            if (any)
            {
                d.UpdateText();
            }
        }

        // Applies one typed character; false for a character that is no key.
        private static bool TypeKey(char c)
        {
            var f = Unlocked();
            var point = DecimalSeparator.Value;
            {
                if (c >= '0' && c <= '6')
                {
                    calc.Digit(c - '0', f, SeenPlace() + 1);
                }
                else if (point.Length > 0 && c == point[0])
                {
                    calc.Point(f);
                }
                else if (c == '-' && calc.AwaitsNumber)
                {
                    calc.Negate();
                }
                else if (TypedOp(c) is Op op)
                {
                    calc.Operation(op, f);
                }
                else if (c == 'E')
                {
                    calc.Operation(Op.Scientific, f);
                }
                else if (c == 'e')
                {
                    calc.Constant(Constant.E, f);
                }
                else if (c == 'p' || c == 'P')
                {
                    calc.Constant(Constant.Pi, f);
                }
                else if (c == '\b')
                {
                    calc.Back();
                }
                else if (c == '=' || c == '\n' || c == '\r')
                {
                    Equal(f);
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        // Whether a text field had the keyboard at the end of the last frame.
        private static bool focusedLastFrame;

        // The player's decimal separator. Unity runs with the invariant culture, so ask Windows
        // (or Wine, which follows the Unix locale) for the user's setting.
        private static readonly Lazy<string> DecimalSeparator = new Lazy<string>(() =>
        {
            try
            {
                var buffer = new System.Text.StringBuilder(8);
                if (GetLocaleInfoEx(null, LocaleSDecimal, buffer, buffer.Capacity) > 1)
                {
                    return buffer.ToString();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read the decimal separator: {e.Message}");
            }
            return System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        });

        private const int LocaleSDecimal = 0x0E;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetLocaleInfoEx(string localeName, int type, System.Text.StringBuilder data, int size);

        private static Op? TypedOp(char c) => c switch
        {
            '+' => Op.Plus,
            '-' => Op.Minus,
            '*' => Op.Multiply,
            '/' => Op.Divide,
            '^' => Op.Exponent,
            _ => (Op?)null,
        };

        // Whether a text field, such as one of the game's translation fields or an IMGUI field
        // in another plugin's window, has the keyboard. IMGUI keeps a field's keyboard focus
        // after its window closes, so an IMGUI field counts only while it is still drawn.
        private static bool TextFieldFocused()
        {
            if (GUIUtility.keyboardControl != 0 && Time.frameCount - imguiFieldFrame <= 1)
            {
                return true;
            }
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected == null)
            {
                return false;
            }
            var tmp = selected.GetComponent<TMPro.TMP_InputField>();
            if (tmp != null && tmp.isFocused)
            {
                return true;
            }
            var legacy = selected.GetComponent<UnityEngine.UI.InputField>();
            return legacy != null && legacy.isFocused;
        }

        // The last frame an IMGUI text field with the keyboard was drawn. OnGUI runs after
        // LateUpdate, so a field drawn last frame still counts.
        private static int imguiFieldFrame = int.MinValue / 2;

        // Every IMGUI text field, password field, and text area is drawn through this method.
        [HarmonyPatch]
        private static class ImguiTextFieldPatch
        {
            private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(GUI))
                    .Where(m => m.Name == "DoTextField" && m.GetParameters().Any(p => p.Name == "id" && p.ParameterType == typeof(int)))
                    .Cast<System.Reflection.MethodBase>();

            // Without the method, IMGUI fields are not seen, and the rest of the plugin still loads.
            private static bool Prepare() => TargetMethods().Any();

            private static void Prefix(int id)
            {
                if (id != 0 && id == GUIUtility.keyboardControl)
                {
                    imguiFieldFrame = Time.frameCount;
                }
            }
        }

        // Clicks, from the plugin's LateUpdate: the game ignores clicks on glyphs it does not know.
        internal static void Tick()
        {
            var d = Manager;
            if (d == null)
            {
                return;
            }
            if (open && !Available(d))
            {
                Close(d);
            }
            if (Showing(d))
            {
                Type(d);
            }
            focusedLastFrame = TextFieldFocused();
            if (!Input.GetMouseButtonDown(0))
            {
                return;
            }
            var glyph = HitGlyph();
            if (IsButton(glyph) && Available(d))
            {
                if (open)
                {
                    Close(d);
                }
                else
                {
                    Open(d);
                }
            }
            else if (IsKey(d, glyph))
            {
                Press(d, glyph.alien_txt, glyph.line);
            }
        }

        [HarmonyPatch]
        private static class WritePatch
        {
            private static System.Reflection.MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Draw), nameof(Draw.Write), new[]
                {
                    typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool), typeof(bool),
                    typeof(bool), typeof(float), typeof(string), typeof(float), typeof(bool), typeof(int), typeof(int),
                });

            private static void Prefix(Draw __instance, ref string s, string sec, ref int page)
            {
                try
                {
                    var d = __instance;
                    switch (sec)
                    {
                        case "lefttext" when Showing(d):
                            s = Body(out shownCarry);
                            page = 0;
                            break;
                        case "lefttitle" when Showing(d):
                            s = Keys.Title;
                            break;
                        case "leftmenu" when Available(d):
                            s = (open ? OpenMenu(d) : s) + " " + Keys.Button;
                            break;
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Write prefix failed: {e}");
                }
            }
        }

        // Set while the game's Update handles a mouse click, outside its redraw.
        private static bool clicking;

        // Set while the game redraws, where one of its pages pushes its own entry again.
        private static bool drawing;

        // A click on a link or button that opens a left panel page replaces the calculator, even
        // when the page is the entry under it. Other pushes replace it only when they add an
        // entry, since one page pushes its own entry again on every redraw. A push of the entry
        // under the calculator adds nothing: the game itself skips only entries stored with one
        // separator, and stores a word's information with two. This runs after other mods'
        // prefixes, which can change the text.
        [HarmonyPatch(typeof(Draw), nameof(Draw.PushLeftText))]
        private static class PushLeftTextPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static bool Prefix(Draw __instance, string title, string info, bool noparse)
            {
                try
                {
                    var d = __instance;
                    if (!open)
                    {
                        return true;
                    }
                    var same = d.currenttext >= 0 && d.currenttext < d.text_history.Count
                        && d.text_history[d.currenttext] == title + (noparse ? "&&" : "&") + info;
                    if (same && !(clicking && !drawing))
                    {
                        return true;
                    }
                    Close(d);
                    return !same;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"PushLeftText prefix failed: {e}");
                    return true;
                }
            }
        }

        // Another save slot has its own unlocks, so the expression does not carry over.
        [HarmonyPatch(typeof(Global), nameof(Global.LoadGame))]
        private static class LoadGamePatch
        {
            private static void Postfix()
            {
                try
                {
                    calc.Forget();
                    Close(Manager);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"LoadGame postfix failed: {e}");
                }
            }
        }

        // The game sets the cursor each frame from what is under it, in the color of nothing
        // clickable for the calculator's keys; give it the clickable color instead. Setting it
        // again after the game would change the cursor twice every frame.
        [HarmonyPatch(typeof(HardwareCursor), nameof(HardwareCursor.Apply))]
        private static class CursorPatch
        {
            private static void Prefix(ref Color c)
            {
                try
                {
                    var d = Manager;
                    if (Showing(d) && c == d.DefaultColor(5) && IsKey(d, HitGlyph()))
                    {
                        c = d.DefaultColor(0);
                    }
                }
                catch (Exception e)
                {
                    LogOnce($"Cursor prefix failed: {e}");
                }
            }
        }

        private static bool loggedPatchError;

        // Patches that run every frame report only their first failure.
        private static void LogOnce(string message)
        {
            if (!loggedPatchError)
            {
                loggedPatchError = true;
                Plugin.Log.LogError(message);
            }
        }

        // The history entry a history button clicked with the calculator open leads to, for the
        // UpdateText1 prefix; -1 for none.
        private static int historyTarget = -1;

        [HarmonyPatch(typeof(Draw), "Update")]
        private static class UpdatePatch
        {
            // The history buttons, clicked while the calculator is open, go to the entry under the
            // calculator or the one after it. The game handles the click during Update as a step
            // from that entry and redraws at the end of Update, so note the entry to show here and
            // set it as the redraw starts.
            private static void Prefix(Draw __instance)
            {
                clicking = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
                try
                {
                    var d = __instance;
                    if (!Showing(d) || !Input.GetMouseButtonDown(0))
                    {
                        return;
                    }
                    var glyph = HitGlyph();
                    if (glyph == null || glyph.sector != "leftmenu")
                    {
                        return;
                    }
                    var target = glyph.alien_txt == Early ? d.currenttext
                        : glyph.alien_txt == Late ? d.currenttext + 1
                        : -1;
                    if (target >= 0 && target < d.text_history.Count)
                    {
                        historyTarget = target;
                        Close(d);
                    }
                }
                catch (Exception e)
                {
                    LogOnce($"Update prefix failed: {e}");
                }
            }

            // A target the redraw did not use does not carry over to a later one.
            private static void Finalizer()
            {
                clicking = false;
                historyTarget = -1;
            }
        }
    }
}
