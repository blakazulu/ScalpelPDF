using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class LongOperationGateTests
    {
        [Fact]
        public void Busy_while_any_operation_is_open()
        {
            var g = new LongOperationGate();
            Assert.False(g.IsBusy);
            var a = g.Begin();
            var b = g.Begin();
            a.Dispose();
            Assert.True(g.IsBusy);
            b.Dispose();
            Assert.False(g.IsBusy);
        }

        [Fact]
        public void Disposing_a_token_twice_does_not_go_negative()
        {
            var g = new LongOperationGate();
            var a = g.Begin();
            a.Dispose();
            a.Dispose();
            var b = g.Begin();
            Assert.True(g.IsBusy);
            b.Dispose();
        }
    }
}
