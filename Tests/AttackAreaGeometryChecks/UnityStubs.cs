using System;

namespace UnityEngine
{
    public static class Mathf
    {
        public static int Abs(int value) => Math.Abs(value);
    }

    public static class Debug
    {
        public static void LogError(object message) => throw new Exception(message.ToString());
    }
}
