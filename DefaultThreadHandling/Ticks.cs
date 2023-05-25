using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PWR.Common
{
    public struct Ticks : IComparable
    {
        public static readonly double TickFrequency = 10000000.0 / Stopwatch.Frequency;
        public static readonly double TimeSpanFrequency = Stopwatch.Frequency / 10000000.0;


        public readonly long Value;

        public Ticks(long value) =>
            Value = value;

        // conversion functions
        private static long ToTimeSpanTicks(in long stopwatchTicks) =>
            (long)(TickFrequency * stopwatchTicks);

        private static long ToStopwatchTicks(in long timeSpanTicks) =>
            (long)(TimeSpanFrequency * timeSpanTicks);

        // conversion methods
        public TimeSpan ToTimeSpan() =>
            TimeSpan.FromTicks(ToTimeSpanTicks(Value));

        public static Ticks FromStopwatch(Stopwatch stopwatch) =>
            new Ticks(stopwatch.ElapsedTicks);

        public static Ticks FromTimeSpan(TimeSpan timeSpan) =>
            new Ticks(ToStopwatchTicks(timeSpan.Ticks));


        public override bool Equals(object obj)
        {
            if (!(obj is Ticks))
                return false;

            var ticks = (Ticks)obj;
            return Value == ticks.Value;
        }

        public override int GetHashCode()
        {
            return -1937169414 + Value.GetHashCode();
        }

        public int CompareTo(object obj)
        {
            if (obj is Ticks ticksObj)
                return Value.CompareTo(ticksObj.Value);
            else if (obj is long longValue)
                return Value.CompareTo(longValue);

            throw new InvalidOperationException($"Unable to compare Ticks object to {obj ?? "NULL object"}");
        }


        // operators
        public static Ticks operator +(Ticks x, Ticks y) =>
            new Ticks(x.Value + y.Value);

        public static Ticks operator -(Ticks x, Ticks y) =>
            new Ticks(x.Value - y.Value);

        public static bool operator <(Ticks x, Ticks y) =>
            x.Value < y.Value;

        public static bool operator >(Ticks x, Ticks y) =>
            x.Value > y.Value;

        public static bool operator ==(Ticks x, Ticks y) =>
            x.Value == y.Value;

        public static bool operator !=(Ticks x, Ticks y) =>
            x.Value != y.Value;
    }
}
