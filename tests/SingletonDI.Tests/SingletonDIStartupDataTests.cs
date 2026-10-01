using System;
using System.Threading;
using System.Threading.Tasks;
using SingletonDI.Generated;
using Xunit;

namespace SingletonDI.Tests;

[Collection("SingletonDI runtime")]
public sealed class SingletonDIStartupDataTests
{
    [Fact]
    public void Set_RejectsDuplicateNameAcrossTypes()
    {
        try
        {
            SingletonDIStartupData.Set("connection", "first");

            var exception = Assert.Throws<InvalidOperationException>(
                () => SingletonDIStartupData.Set("connection", 42));

            Assert.Contains("connection", exception.Message, StringComparison.Ordinal);
            Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
            Assert.Equal("first", SingletonDIStartupData.Get<string>("connection"));
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public void Set_RejectsNullNameAndBlankName()
    {
        try
        {
            Assert.Throws<ArgumentNullException>(() => SingletonDIStartupData.Set<string>(null!, "value"));
            Assert.Throws<ArgumentException>(() => SingletonDIStartupData.Set("   ", "value"));
            Assert.Throws<ArgumentException>(() => SingletonDIStartupData.Set(string.Empty, "value"));
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public void Get_ReportsMissingNameAndWrongType()
    {
        try
        {
            SingletonDIStartupData.Set("port", 5432);

            var missing = Assert.Throws<KeyNotFoundException>(
                () => SingletonDIStartupData.Get<string>("missing"));
            Assert.Contains("missing", missing.Message, StringComparison.Ordinal);

            var wrongType = Assert.Throws<InvalidCastException>(() => SingletonDIStartupData.Get<long>("port"));
            Assert.Contains("port", wrongType.Message, StringComparison.Ordinal);
            Assert.Contains("System.Int32", wrongType.Message, StringComparison.Ordinal);
            Assert.Contains("System.Int64", wrongType.Message, StringComparison.Ordinal);
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public void Get_RejectsAssignableButDifferentTypeArgument()
    {
        try
        {
            // Assignability would make the declared type an afterthought: the same name would mean
            // whatever the reader happens to ask for.
            SingletonDIStartupData.Set<object>("boxed", "text");

            var exception = Assert.Throws<InvalidCastException>(
                () => SingletonDIStartupData.Get<string>("boxed"));

            Assert.Contains("System.Object", exception.Message, StringComparison.Ordinal);
            Assert.Equal("text", SingletonDIStartupData.Get<object>("boxed"));
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public void Get_ReturnsNullForAStoredNullValueAndStillReportsAMissingName()
    {
        try
        {
            SingletonDIStartupData.Set<int?>("optional", null);

            Assert.Null(SingletonDIStartupData.Get<int?>("optional"));
            Assert.Throws<KeyNotFoundException>(() => SingletonDIStartupData.Get<int?>("absent"));
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public async Task Seal_RejectsFurtherWritesAndClearMakesEveryReadFail()
    {
        try
        {
            SingletonDIStartupData.Set("value", 1);
            SingletonDIStartupData.Seal();

            var exception = Assert.Throws<InvalidOperationException>(() => SingletonDIStartupData.Set("other", 2));
            Assert.Contains("initialization", exception.Message, StringComparison.OrdinalIgnoreCase);

            await SingletonDIStartupData.ClearAndDisposeAsync();

            var readException = Assert.Throws<InvalidOperationException>(
                () => SingletonDIStartupData.Get<int>("value"));
            Assert.Contains("cleared", readException.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public async Task Clear_DisposesSynchronousValues()
    {
        try
        {
            var value = new DisposableValue();
            SingletonDIStartupData.Set("value", value);

            await SingletonDIStartupData.ClearAndDisposeAsync();

            Assert.Equal(1, value.DisposeCount);
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public async Task Clear_PrefersAsynchronousDisposalOverSynchronous()
    {
        try
        {
            var value = new AsyncDisposableValue();
            SingletonDIStartupData.Set("value", value);

            await SingletonDIStartupData.ClearAndDisposeAsync();

            Assert.Equal(1, value.AsyncDisposeCount);
            Assert.Equal(0, value.DisposeCount);
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public async Task Clear_ReleasesEveryReferenceEvenWhenDisposalFails()
    {
        try
        {
            var failing = new DisposableValue(failOnDispose: true);
            var succeeding = new DisposableValue();
            SingletonDIStartupData.Set("failing", failing);
            SingletonDIStartupData.Set("succeeding", succeeding);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await SingletonDIStartupData.ClearAndDisposeAsync());

            Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
            Assert.Equal(1, succeeding.DisposeCount);
            Assert.Throws<InvalidOperationException>(() => SingletonDIStartupData.Get<DisposableValue>("succeeding"));
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public async Task Clear_ReportsEveryFailureAsAnAggregate()
    {
        try
        {
            SingletonDIStartupData.Set("first", new DisposableValue(failOnDispose: true));
            SingletonDIStartupData.Set("second", new DisposableValue(failOnDispose: true));

            var exception = await Assert.ThrowsAsync<AggregateException>(
                async () => await SingletonDIStartupData.ClearAndDisposeAsync());

            Assert.Equal(2, exception.InnerExceptions.Count);
            Assert.All(exception.InnerExceptions, inner =>
                Assert.Contains("boom", inner.Message, StringComparison.Ordinal));
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    [Fact]
    public async Task ResetForTesting_ClearsValuesAndAllowsWritingAgain()
    {
        SingletonDIStartupData.Set("value", 1);
        SingletonDIStartupData.Seal();
        await SingletonDIStartupData.ClearAndDisposeAsync();

        __SingletonDIHost__.ResetForTesting();

        SingletonDIStartupData.Set("value", 2);
        Assert.Equal(2, SingletonDIStartupData.Get<int>("value"));

        __SingletonDIHost__.ResetForTesting();
    }

    [Fact]
    public async Task ConcurrentReads_AreServedWhileValuesAreInFlight()
    {
        try
        {
            SingletonDIStartupData.Set("value", 7);
            var failures = 0;
            var readers = new Task[8];

            for (var index = 0; index < readers.Length; index++)
            {
                readers[index] = Task.Run(() =>
                {
                    if (SingletonDIStartupData.Get<int>("value") != 7)
                    {
                        Interlocked.Increment(ref failures);
                    }
                });
            }

            await Task.WhenAll(readers);

            Assert.Equal(0, failures);
        }
        finally
        {
            __SingletonDIHost__.ResetForTesting();
        }
    }

    private sealed class DisposableValue : IDisposable
    {
        private readonly bool _failOnDispose;

        internal DisposableValue(bool failOnDispose = false)
        {
            _failOnDispose = failOnDispose;
        }

        internal int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            if (_failOnDispose)
            {
                throw new InvalidOperationException("boom");
            }
        }
    }

    private sealed class AsyncDisposableValue : IDisposable, IAsyncDisposable
    {
        internal int DisposeCount { get; private set; }

        internal int AsyncDisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCount++;
            return default;
        }
    }
}