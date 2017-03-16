using System;

namespace PinvokeKit
{
    public static class StringUtils
    {
        private static readonly string HexDigitsformatString = string.Format("X{0}", IntPtr.Size*2);

        /// <summary>
        /// Renders the pointer-sized integer as the appropriate number of hex chars, with leading zeros.
        /// </summary>
        public static string ToHexString(this IntPtr intptr)
        {
            return unchecked((ulong)(long)intptr).ToString(HexDigitsformatString);
        }

        /// <summary>
        /// If the string contains spaces, surrounds it with quotes.
        /// </summary>
        public static string QuoteIfNeeded(this string s)
        {
            if(s == null)
                return "<NULL>";

            if((s.Length != 0) && (!s.Contains(" "))) // Not needed
                return s;
            if((s.Length > 0) && (s[0] == '“') && s[s.Length - 1] == '”') // Already quoted
                return s;

            return '“' + s + '”';
        }
    }
}