namespace JoinFS
{
    class Timer
    {
        readonly double interval;
        double elapseTime;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="interval"></param>
        public Timer(double interval)
        {
            // set interval
            this.interval = interval;
            Reset();
        }

        /// <summary>
        /// reset the timer
        /// </summary>
        public void Reset()
        {
            // elapse on first call
            elapseTime = 0.0;
        }

        /// <summary>
        /// set the timer
        /// </summary>
        public void Set(double elapseTime)
        {
            // elapse on first call
            this.elapseTime = elapseTime;
        }

        /// <summary>
        /// Check if timer has elapsed
        /// </summary>
        /// <param name="now"></param>
        /// <returns></returns>
        public bool Elapsed(double now)
        {
            // check if timer has elapsed
            if (now > elapseTime)
            {
                // update elapse time
                elapseTime = now + interval;
                // elapsed
                return true;
            }
            else
            {
                // not elapsed
                return false;
            }
        }
    }
}
