#nullable enable

#if (DEVELOPMENT_BUILD || UNITY_EDITOR) && EVENTTIMELINE_DEBUG
#define __EVENTTIMELINE_DEBUG
#endif

#if __EVENTTIMELINE_DEBUG && EVENTTIMELINE_DEBUG_VERBOSE
#define __EVENTTIMELINE_DEBUG_VERBOSE
#endif

#if __EVENTTIMELINE_DEBUG || __EVENTTIMELINE_DEBUG_VERBOSE
using System.Threading.Tasks;
using UnityEngine;

namespace UnityEventTimeline.Internal.Logger
{
    internal static class AsyncLogger
    {
        internal static void Log(string message)
        {
            Task.Run(() => Debug.Log(message));
        }

        internal static void LogFormat(string format, params object[] args)
        {
            Task.Run(() => Debug.LogFormat(format, args));
        }

        internal static void LogWarning(string message)
        {
            Task.Run(() => Debug.LogWarning(message));
        }

        internal static void LogWarningFormat(string format, params object[] args)
        {
            Task.Run(() => Debug.LogWarningFormat(format, args));
        }

        internal static void LogError(string message)
        {
            Task.Run(() => Debug.LogError(message));
        }

        internal static void LogErrorFormat(string format, params object[] args)
        {
            Task.Run(() => Debug.LogErrorFormat(format, args));
        }
    }
}
#endif