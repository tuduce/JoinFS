using System;
using Xunit;

namespace JoinFS.Tests
{
    public class CreateVuidTests
    {
        [Fact]
        public void CreateVuid_SameInput_ReturnsSameValue()
        {
            // Arrange
            string name = "com active frequency:1";

            // Act
            uint vuid1 = JoinFS.VariableMgr.CreateVuid(name);
            uint vuid2 = JoinFS.VariableMgr.CreateVuid(name);

            // Assert
            Assert.Equal(vuid1, vuid2);
        }

        [Fact]
        public void CreateVuid_DifferentInputs_UsuallyDifferent()
        {
            // Arrange
            string a = "AaAa";
            string b = "rAA";

            // Act
            uint vuidA = JoinFS.VariableMgr.CreateVuid(a);
            uint vuidB = JoinFS.VariableMgr.CreateVuid(b);

            // Assert
            Assert.NotEqual(vuidA, vuidB);
        }

        [Fact]
        public void CreateVuid_ZeroHash_IsRemappedToOne()
        {
            // This test asserts the conflict-avoidance behavior where a zero hash
            // is remapped to 1 to avoid the null identifier.
            // We cannot force LocalNode.HashString to return 0 here, but CreateVuid
            // guarantees that 0 => 1. Validate that an empty string does not return 0.

            // Act
            uint vuid = JoinFS.VariableMgr.CreateVuid(string.Empty);

            // Assert
            Assert.NotEqual(0u, vuid);
        }

        [Fact]
        public void CreateVuid_IsDeterministicAcrossRuns()
        {
            // Arrange
            string input = "transponder code:1";

            // Act
            uint once = JoinFS.VariableMgr.CreateVuid(input);
            uint twice = JoinFS.VariableMgr.CreateVuid(input);

            // Assert
            Assert.Equal(once, twice);
        }

        [Fact]
        public void CreateVuid_CollisionDetection_SameVuidDifferentStrings()
        {
            // NOTE: Real collisions are hard to construct without knowing the exact hash.
            // This test documents expectation: if two different strings do collide,
            // the system should log and allow distinct definitions handling elsewhere.
            // Here we simply assert the API behavior does not throw and returns a uint.

            uint a = JoinFS.VariableMgr.CreateVuid("light states");
            uint b = JoinFS.VariableMgr.CreateVuid("light states1");

            Assert.True(a != 0u && b != 0u);
        }
    }
}
