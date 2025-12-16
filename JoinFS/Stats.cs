using System;
using System.Collections.Generic;
using System.Text;

namespace JoinFS
{
    public class Stats
    {
        /// <summary>
        /// Packet header size
        /// </summary>
        public const int HEADER_SIZE = 42;

        public class Stat
        {
            // recorded stat
            public int totalCount;
            public int totalBytes;

            // last totals
            public int lastMinuteCount;
            public int lastHourCount;
            public int lastDayCount;
            public int lastMinuteBytes;
            public int lastHourBytes;
            public int lastDayBytes;

            // now totals
            public int nowMinuteCount;
            public int nowHourCount;
            public int nowDayCount;
            public int nowMinuteBytes;
            public int nowHourBytes;
            public int nowDayBytes;

            // time
            int currentMinute;
            int currentHour;
            int currentDay;

            /// <summary>
            /// Constructor
            /// </summary>
            public Stat()
            {
                // get the current time
                currentMinute = DateTime.Now.Minute;
                currentHour = DateTime.Now.Hour;
                currentDay = DateTime.Now.Day;
            }

            /// <summary>
            /// Update the current and last values
            /// </summary>
            void Update()
            {
                // check if minute has changed
                if (DateTime.Now.Minute != currentMinute)
                {
                    // update last
                    lastMinuteCount = nowMinuteCount;
                    lastMinuteBytes = nowMinuteBytes;
                    // reset
                    nowMinuteCount = 0;
                    nowMinuteBytes = 0;
                    // save current minute
                    currentMinute = DateTime.Now.Minute;
                }

                // check if hour has changed
                if (DateTime.Now.Hour != currentHour)
                {
                    // update last
                    lastHourCount = nowHourCount;
                    lastHourBytes = nowHourBytes;
                    // reset
                    nowHourCount = 0;
                    nowHourBytes = 0;
                    // save current hour
                    currentHour = DateTime.Now.Hour;
                }

                // check if day has changed
                if (DateTime.Now.Day != currentDay)
                {
                    // update last
                    lastDayCount = nowDayCount;
                    lastDayBytes = nowDayBytes;
                    // reset
                    nowDayCount = 0;
                    nowDayBytes = 0;
                    // save current day
                    currentDay = DateTime.Now.Day;
                }
            }

            /// <summary>
            /// Bytes per second
            /// </summary>
            /// <param name="bytes"></param>
            /// <param name="seconds"></param>
            /// <returns></returns>
            string PerSecond(int bytes, int seconds)
            {
                // calculate bits per second
                int bps = ((bytes << 3) + seconds - 1) / seconds;
                // add k if necessary
                return bps > 1024 ? (bps >> 10).ToString() + "k" : bps.ToString();
            }

            /// <summary>
            /// Format a count value
            /// </summary>
            /// <param name="count"></param>
            /// <returns></returns>
            string Format(int count)
            {
                // add k if necessary
                return count > 1024 ? (count >> 10).ToString() + "k" : count.ToString();
            }

            /// <summary>
            /// Convert stat to string
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                // update
                Update();
                // return formatted string
                return Format(lastMinuteCount) + " " + PerSecond(lastMinuteBytes, 60) + " : " + Format(lastHourCount) + " " + PerSecond(lastHourBytes, 3600) + " : " + Format(lastDayCount) + " " + PerSecond(lastDayBytes, 86400) + " : " + Format(totalCount) + " " + PerSecond(totalBytes, (int)(DateTime.Now - start).TotalSeconds);
            }

            /// <summary>
            /// Record an update
            /// </summary>
            /// <param name="bytes"></param>
            public void Record(long size)
            {
                // update
                Update();

                // add header
                int bytes = (int)size + HEADER_SIZE;

                // update now totals
                nowMinuteCount++;
                nowHourCount++;
                nowDayCount++;
                nowMinuteBytes += bytes;
                nowHourBytes += bytes;
                nowDayBytes += bytes;

                // update total
                totalCount++;
                totalBytes += bytes;
            }
        }

        /// <summary>
        /// Start time
        /// </summary>
        static DateTime start = DateTime.Now;

        /// <summary>
        /// Stats
        /// </summary>
        public static Stat Join = new();
        public static Stat JoinReply = new();
        public static Stat JoinFail = new();
        public static Stat Login = new();
        public static Stat LoginFail = new();
        public static Stat Leave = new();
        public static Stat AddNode = new();
        public static Stat Pulse = new();
        public static Stat PulseResponse = new();
        public static Stat GuaranteedDone = new();
        public static Stat Pathfinder = new();
        public static Stat PathfinderResponse = new();

        public static Stat ObjectPosition = new();
        public static Stat AircraftPosition = new();
        public static Stat SimEvent = new();
        public static Stat WeatherRequest = new();
        public static Stat WeatherReply = new();
        public static Stat WeatherUpdate = new();
        public static Stat SharedData = new();
        public static Stat StatusRequest = new();
        public static Stat Status = new();
        public static Stat HubList = new();
        public static Stat RemoveObject = new();
        public static Stat UserListRequest = new();
        public static Stat UserList = new();
        public static Stat UserList2 = new();
        public static Stat UserPositionRequest = new();
        public static Stat UserPositions = new();
        public static Stat SessionCommsRequest = new();
        public static Stat Notes = new();
        public static Stat UserNuidRequest = new();
        public static Stat UserNuid = new();
        public static Stat Online = new();
        public static Stat FlightPlanRequest = new();
        public static Stat FlightPlan = new();
        public static Stat IntegerVariables = new();
        public static Stat FloatVariables = new();
        public static Stat String8Variables = new();

        public static Stat WrongVersion = new();
        public static Stat Total = new();
    }
}
