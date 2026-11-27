using System;
using System.Globalization;

namespace JoinFS.Fsd
{
    /// <summary>
    /// A VHF frequency in FSD text and in kHz. FSD leaves out the leading <c>1</c> and the decimal point:
    /// <c>28550</c> is 128.550 MHz, which is 128550 kHz (the unit JoinFS carries).
    /// </summary>
    public static class FsdFrequency
    {
        /// <summary>The frequencies the field can hold: 100.000 to 199.999 MHz.</summary>
        public const int MinKhz = 100000;
        public const int MaxKhz = 199999;

        /// <summary>FSD text to kHz. False when it is not a number of 1 to 5 digits.</summary>
        public static bool TryParse(string field, out int khz)
        {
            khz = 0;
            if (field == null || field.Length == 0 || field.Length > 5) return false;
            if (!int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) return false;
            khz = MinKhz + value;
            return true;
        }

        /// <summary>kHz to FSD text: 128550 gives <c>28550</c>.</summary>
        public static string ToField(int khz)
        {
            if (khz < MinKhz || khz > MaxKhz) throw new ArgumentOutOfRangeException(nameof(khz));
            return (khz - MinKhz).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>kHz as a person writes it: 128550 gives <c>128.550</c>.</summary>
        public static string ToMegahertz(int khz) =>
            (khz / 1000).ToString(CultureInfo.InvariantCulture) + "." + (khz % 1000).ToString("D3", CultureInfo.InvariantCulture);
    }
}
