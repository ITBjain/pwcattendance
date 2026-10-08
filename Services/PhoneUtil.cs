using System.Linq;

namespace PwcApi.Services
{
    public static class PhoneUtil
    {
        /// <summary>
        /// Converts any stored phone format ("98765 43210", "+91-9876543210", "09876543210") into the
        /// international digits-only format WhatsApp/Whapi expects ("919876543210").
        /// Returns null when the number can't be a valid WhatsApp number.
        /// </summary>
        public static string? ToWhatsAppNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("00")) digits = digits.Substring(2);           // 0091...
            if (digits.Length == 11 && digits.StartsWith("0")) digits = digits.Substring(1); // 09876543210
            if (digits.Length == 10) digits = "91" + digits;                      // local Indian mobile

            if (digits.Length < 11 || digits.Length > 15) return null;
            return digits;
        }

        /// <summary>Masks a phone number for display/logs: ••••••••3210</summary>
        public static string Mask(string? phone)
        {
            if (string.IsNullOrEmpty(phone)) return "";
            return phone.Length <= 4 ? phone : new string('•', phone.Length - 4) + phone.Substring(phone.Length - 4);
        }
    }
}
