using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    internal static class MooGirlLog
    {
        private const string Prefix = "[MooGirl] ";
        private static readonly HashSet<string> warnedKeys = new HashSet<string>();

        internal static void Message(string message)
        {
            Log.Message(Prefix + message);
        }

        internal static void Warning(string message)
        {
            Log.Warning(Prefix + message);
        }

        internal static void WarningOnce(string key, string message)
        {
            if (warnedKeys.Add(key))
            {
                Warning(message);
            }
        }

        internal static void Error(string message)
        {
            Log.Error(Prefix + message);
        }
    }
}
