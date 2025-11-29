namespace JoinFS
{
    /// <summary>
    /// Window refresher
    /// </summary>
    public class Refresher
    {
        /// <summary>
        /// counter
        /// </summary>
        volatile int count = 0;

        // do a refresh
        public void Schedule()
        {
            count += 1;
        }

        // do several refreshes
        public void Schedule(int count)
        {
            this.count += count;
        }

        // check for refresh
        public bool Refresh()
        {
            if (count > 0)
            {
                // update count
                count--;
                // refresh
                return true;
            }

            // no refresh
            return false;
        }
    }
}
