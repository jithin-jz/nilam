using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace Nilam
{
    /// <summary>
    /// Thin, forgiving accessors over JavaScriptSerializer's object model. A
    /// settings file is edited by people and by older versions, so a value of
    /// the wrong type falls back to the default instead of failing the load.
    /// </summary>
    internal static class Json
    {
        /// <summary>Null when the text is empty or its root is not an object.</summary>
        internal static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            // Strip a UTF-8 BOM, which Windows PowerShell writes.
            if (text[0] == '\uFEFF') text = text.Substring(1);
            if (text.Trim().Length == 0) return null;
            return new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
        }

        internal static string Serialize(object value)
        {
            return new JavaScriptSerializer().Serialize(value);
        }

        internal static Dictionary<string, object> Object(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value)) return null;
            return value as Dictionary<string, object>;
        }

        internal static object[] Array(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value)) return null;
            return value as object[];
        }

        internal static string String(Dictionary<string, object> parent, string key)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value) || value == null) return null;
            return value as string;
        }

        internal static bool Has(Dictionary<string, object> parent, string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value) && value != null;
        }

        internal static double Number(Dictionary<string, object> parent, string key, double fallback)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value)) return fallback;
            double d;
            return TryDouble(value, out d) ? d : fallback;
        }

        internal static bool Bool(Dictionary<string, object> parent, string key, bool fallback)
        {
            object value;
            if (parent == null || !parent.TryGetValue(key, out value)) return fallback;
            return value is bool ? (bool)value : fallback;
        }

        /// <summary>
        /// JavaScriptSerializer hands back int, decimal or double depending on
        /// the literal, so normalise through Convert with the invariant culture.
        /// </summary>
        internal static bool TryDouble(object value, out double result)
        {
            result = 0;
            if (value == null || value is string || value is bool) return false;
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch (InvalidCastException) { return false; }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
    }
}
