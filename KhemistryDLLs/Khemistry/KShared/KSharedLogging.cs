using UnityEngine;

namespace Khemistry
{
    public partial class KShared
    {
        /// <summary>
        /// Writes a log message to the KSP.log and in-game console.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="func">Optional function name the log message came from.</param>
        public static void Log(string message, string func = null)
        {
            if (func != null)
                Debug.Log("Khemistry (" + func + "): " + message);
            else
                Debug.Log("Khemistry: ()" + message);
        }

        /// <summary>
        /// Writes an error log message to the KSP.log and in-game console.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The error message to send.</param>
        /// <param name="func">Optional function name the error log message came from.</param>
        public static void LogError(string message, string func = null)
        {
            if (func != null)
                Debug.LogError("Khemistry (" + func + "): " + message);
            else
                Debug.LogError("Khemistry: ()" + message);
        }

        /// <summary>
        /// Writes a fatal error log message to the KSP.log and in-game console.
        /// This then raises an exception as the error is too severe to continue.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The error message to send.</param>
        /// <param name="func">Optional function name the error log message came from.</param>
        public static void LogFatalError(string message, string func = null)
        {
            if (func != null)
                Debug.LogError("FATAL ERROR! Khemistry (" + func + "): " + message);
            else
                Debug.LogError("FATAL ERROR! Khemistry: ()" + message);
            throw new System.OperationCanceledException("A fatal Khemistry error, stopping.");
        }

        /// <summary>
        /// Writes a warning log message to the KSP.log and in-game console.
        /// Usually, func is formatted as "class/function" or "class/constructor".
        /// </summary>
        /// <param name="message">The warning message to send.</param>
        /// <param name="func">Optional function name the warning log message came from.</param>
        public static void LogWarning(string message, string func = null)
        {
            if (func != null)
                Debug.LogWarning("Khemistry (" + func + "): " + message);
            else
                Debug.LogWarning("Khemistry: ()" + message);
        }

        /// <summary>
        /// Writes an error indicating that a value is missing from a node.
        /// Essentially a fancy wrapper for <see cref="LogError"/>.
        /// </summary>
        /// <param name="node">The name of the node containing the missing value.</param>
        /// <param name="value">The name of the missing value.</param>
        /// <param name="beginning">The first part of the log, usually includes some more location information.</param>
        /// <param name="source">The not optional source for where the error came from.</param>
        public static void LogNoValueInNode(string node, string value, string beginning, string source)
            => LogError($"{beginning} failed to load because node \"{node}\" did not have a \"{value}\" value!", source);

        /// <summary>
        /// Writes an error indicating that a node is missing.
        /// Essentially a fancy wrapper for <see cref="LogError"/>.
        /// </summary>
        /// <param name="node">The name of the missing node.</param>
        /// <param name="beginning">The first part of the log, usually includes some more location information.</param>
        /// <param name="source">The not optional source for where the error came from.</param>
        public static void LogNoNode(string node, string beginning, string source)
            => LogError($"{beginning} failed to load because node \"{node}\" was not found!", source);
    }
}
