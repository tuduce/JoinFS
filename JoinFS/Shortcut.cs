#if !CONSOLE
using System.Runtime.InteropServices;

namespace JoinFS
{
    /// <summary>
    /// A global keyboard shortcut: Ctrl, Shift and Alt in any mix, and a letter A-Z. It is polled with GetAsyncKeyState, so it works while the
    /// simulator has the focus. Both the forms (MainForm) and the new UI (Live/LiveShortcutSource) use it.
    /// </summary>
    public class Shortcut
    {
        [DllImport("User32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        public const int VK_CONTROL = 0x11;
        public const int VK_SHIFT = 0x10;
        public const int VK_ALT = 0x12;
        public const int VK_A = 0x41;
        public const int VK_Z = 0x5A;

        /// <summary>
        /// The combination as saved and shown, e.g. CTRL+SHIFT+R
        /// </summary>
        public string combination;
        public bool control;
        public bool shift;
        public bool alt;
        public int letter = VK_A;
        public bool state;

        /// <summary>
        /// Check if a particular key is pressed
        /// </summary>
        public static bool KeyPressed(int key)
        {
            return ((GetAsyncKeyState(key) >> 15) & 0x0001) == 0x0001;
        }

        /// <summary>
        /// Check if a combination is a valid shortcut: any of CTRL+, SHIFT+, ALT+ and then one letter
        /// </summary>
        public static bool IsValid(string combination)
        {
            if (string.IsNullOrEmpty(combination))
            {
                return false;
            }
            string[] keys = combination.Split('+');
            for (int index = 0; index < keys.Length - 1; index++)
            {
                if (keys[index] != "CTRL" && keys[index] != "SHIFT" && keys[index] != "ALT")
                {
                    return false;
                }
            }
            string last = keys[keys.Length - 1];
            return last.Length == 1 && last[0] >= VK_A && last[0] <= VK_Z;
        }

        /// <summary>
        /// Build the combination text from the keys held and the letter (an upper case A-Z)
        /// </summary>
        public static string Compose(bool control, bool shift, bool alt, char letter)
        {
            return (control ? "CTRL+" : "") + (shift ? "SHIFT+" : "") + (alt ? "ALT+" : "") + letter;
        }

        /// <summary>
        /// Load a shortcut from its combination setting. A part that is not understood is left out; the letter then stays A
        /// </summary>
        public void Load(string combination)
        {
            // initialize shortcut
            control = false;
            shift = false;
            alt = false;
            letter = VK_A;
            state = false;
            // get combination setting
            this.combination = combination;
            // parse keys
            string[] keys = combination.Split('+');

            // for each combination key
            for (int index = 0; index < keys.Length - 1; index++)
            {
                // set combination key
                if (keys[index].Equals("CTRL"))
                {
                    control = true;
                }
                else if (keys[index].Equals("SHIFT"))
                {
                    shift = true;
                }
                else if (keys[index].Equals("ALT"))
                {
                    alt = true;
                }
            }

            // check for valid key
            if (keys.Length > 0)
            {
                // convert to char array
                char[] chars = keys[keys.Length - 1].ToCharArray();
                // check for valid letter
                if (chars.Length > 0 && chars[0] >= VK_A && chars[0] <= VK_Z)
                {
                    letter = chars[0];
                }
            }
        }

        /// <summary>
        /// Check if the combination was just pressed: exactly its modifiers are held, and its letter went down since the last call
        /// </summary>
        public bool Pressed(bool control, bool shift, bool alt)
        {
            // update key state
            bool before = state;
            state = KeyPressed(letter);
            // check if combination pressed
            return this.control == control && this.shift == shift && this.alt == alt && before == false && state;
        }
    }
}
#endif
