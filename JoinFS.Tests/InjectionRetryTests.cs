using JoinFS.Net;

namespace JoinFS.Tests
{
    /// <summary>
    /// An object whose injection the simulator refused (SimConnect exception 22, usually while it is
    /// still loading) is retried after settingsInjectionRetrySeconds, but only FAILED_RETRY_MAX times.
    /// </summary>
    public class InjectionRetryTests
    {
        const double RetrySeconds = 10.0;
        const double FailedAt = 100.0;

        static Sim.Obj FailedObject(int failedCount)
        {
            return new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 1)
            {
                failed = true,
                failedTime = FailedAt,
                failedCount = failedCount,
            };
        }

        [Fact]
        public void ObjectThatNeverFailed_IsEligible()
        {
            var obj = new Sim.Plane(new NodeId(0x0A000001, 6112, 1), 1);
            Assert.True(Sim.InjectionRetryEligible(obj, FailedAt, RetrySeconds));
        }

        [Fact]
        public void FailedObject_IsNotEligibleBeforeTheBackoffElapsed()
        {
            Assert.False(Sim.InjectionRetryEligible(FailedObject(1), FailedAt + 1.0, RetrySeconds));
        }

        [Fact]
        public void FailedObject_IsNotEligibleExactlyAtTheBackoff()
        {
            Assert.False(Sim.InjectionRetryEligible(FailedObject(1), FailedAt + RetrySeconds, RetrySeconds));
        }

        [Fact]
        public void FailedObject_IsEligibleJustAfterTheBackoff()
        {
            Assert.True(Sim.InjectionRetryEligible(FailedObject(1), FailedAt + RetrySeconds + 0.001, RetrySeconds));
        }

        [Fact]
        public void FailedObject_IsEligibleOneBelowTheCap()
        {
            Assert.True(Sim.InjectionRetryEligible(FailedObject(Sim.FAILED_RETRY_MAX - 1), FailedAt + 1000.0, RetrySeconds));
        }

        [Fact]
        public void FailedObject_IsNeverEligibleAtTheCap()
        {
            Assert.False(Sim.InjectionRetryEligible(FailedObject(Sim.FAILED_RETRY_MAX), FailedAt + 1000.0, RetrySeconds));
        }
    }
}
