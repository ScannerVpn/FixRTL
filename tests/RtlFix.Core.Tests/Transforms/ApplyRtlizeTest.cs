using System;
using Xunit;

namespace RtlFix.Core.Tests.Transforms;

public class ApplyRtlizeTest
{
    [Fact]
    public void Run()
    {
        TestRtlizeLine.Apply(43196, 21);
    }
}